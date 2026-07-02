using System;
using UnityEngine;
using UnityEngine.Rendering;

// The brush's spatial profile — a value-agnostic footprint shared by every paint target (vector field, smoke, …). The
// base is a pure radial falloff: no direction of its own (the painted direction comes from the stroke tangent, or a
// Stamp's direction argument), and it's all a scalar field like smoke ever needs — so a Color brush carries no
// vector-field data. Textured / per-cell-directional brushes, which sample a 2D EMITTER MAP, are a vector-field concern
// and live in the VectorFieldBrushShape subclass below. Built once and reused.
public class BrushShape {
    readonly float softness;      // radial: 0 = hard edge, 1 = fully soft

    protected BrushShape(float softness) { this.softness = Mathf.Clamp01(softness); }

    // A pure radial falloff. This is what most runtime effects — and all scalar fields — use.
    public static BrushShape Radial(float softness = 0.5f) => new BrushShape(softness);

    // True when this shape carries a 2D emitter map (textured/directional). False for a plain radial falloff.
    public virtual bool IsMap => false;

    // 0..1 weight at a normalized distance from the centre (0 = centre, 1 = edge).
    public float Weight(float normalizedDistance) {
        float d = Mathf.Clamp01(normalizedDistance);
        float core = 1f - softness;                 // inside `core` the weight is full
        if (d <= core) return 1f;
        float t = (d - core) / Mathf.Max(1e-4f, 1f - core);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    // Sample the 2D emitter map at a local position in [-1,1] (0 = centre, ±1 = edge), returning the local-frame brush
    // vector (magnitude = weight, direction = emitter dir). The radial base has no map, so it never emits a direction;
    // the vector-field subclass overrides this.
    public virtual Vector2 Sample2D(Vector2 local) => Vector2.zero;
}

// The vector-field flavour: wraps a prebuilt 2D emitter map for textured / per-cell-directional brushes. The sampled
// VECTOR is the brush contribution — magnitude = coverage weight, direction (brush-local, +Y = "forward") = the
// emitter/cookie direction. Only vector fields consume that direction, so this stays out of the value-neutral base.
public sealed class VectorFieldBrushShape : BrushShape {
    readonly Vector2Map map;

    VectorFieldBrushShape(Vector2Map map) : base(0.5f) { this.map = map; }

    // Wrap a prebuilt 2D brush map. Returned as the base type so any paint target can hold it uniformly.
    public static BrushShape FromMap(Vector2Map map) => new VectorFieldBrushShape(map);

    // Build a textured/directional brush from a cookie mask + emitter, on the GPU, and cache it on the CPU. This is a
    // one-time build (a synchronous GPU readback stalls briefly) — do it in setup, not per frame, and reuse the shape.
    // Consumes the cookie's generated GPU mask (Falloff/Curve regenerate on demand if the cookie is reused).
    public static BrushShape FromCookie(VectorFieldCookieSource cookie, VectorFieldBrushSettings emitter,
                                        int resolution = 32) {
        if (emitter == null) throw new ArgumentNullException(nameof(emitter));
        var size = new Vector2Int(Mathf.Max(1, resolution), Mathf.Max(1, resolution));
        var mask = cookie != null ? cookie.Resolve(size) : null;

        RenderTexture rt = null;
        VectorFieldRenderTextureUtils.EnsureValid(ref rt, size);
        VectorFieldBrushTextureCreator.Dispatch(rt, size, 1f, emitter, mask);   // emitter shaped by the mask, magnitude 1

        Vector2Map built = null;
        var req = AsyncGPUReadback.Request(rt, 0, r => {
            if (r.hasError) { Debug.LogError("VectorFieldBrushShape.FromCookie: GPU readback failed."); return; }
            built = new Vector2Map(new Point(r.width, r.height), VectorFieldUtils.ColorsToVectors(r.GetData<Color>(), 1));
        });
        req.WaitForCompletion();

        VectorFieldRenderTextureUtils.Destroy(ref rt);
        cookie?.Dispose();

        if (built == null) {
            Debug.LogError("VectorFieldBrushShape.FromCookie: brush map unavailable; using a radial fallback.");
            return Radial(cookie != null ? cookie.falloffSoftness : 0.5f);
        }
        return FromMap(built);
    }

    public override bool IsMap => map != null;

    // Sample the 2D map at a local position in [-1,1]. Outside the unit square the brush doesn't reach, so returns zero.
    public override Vector2 Sample2D(Vector2 local) {
        if (map == null) return Vector2.zero;
        if (local.x < -1f || local.x > 1f || local.y < -1f || local.y > 1f) return Vector2.zero;
        return map.GetValueAtNormalizedPosition(local * 0.5f + new Vector2(0.5f, 0.5f));
    }
}

// How a continuous stroke renders its moving head (see VectorFieldStroke).
public enum TipMode {
    // ~1-point lag: the head follows the centripetal spline, using the newest point only as look-ahead for the
    // tangent. Smoothest; the default. The stroke's final tail is flushed on End() so nothing is lost.
    Smoothed,
    // Zero lag: the head is drawn all the way to the newest point, with a forward tangent extrapolated from recent
    // velocity. Use for beams / visible heads where lag reads as latency. Slightly less smooth on hard turns.
    Leading,
}

// How a stroke picks the painted flow direction for a map/directional brush. FollowStroke: the emitter direction is
// rotated to align with the stroke tangent (flow follows the drag — natural for drawing flow lines). FixedAngle: the
// map's baked emitter direction is used as-is (world frame). Ignored by radial shapes (they always follow the tangent)
// and by Stamp (a dab has no stroke direction — it uses the map's baked direction).
public enum BrushDirectionMode { FollowStroke, FixedAngle }

// A reusable, configured brush: shape + op + size + pressure + tip/direction mode. The friendly runtime API takes one
// of these. Cheap to copy (holds two references plus scalars); build once on a prefab/field and reuse every frame.
public struct VectorFieldBrush {
    public BrushShape shape;
    public IVectorFieldBrushOp op;
    public float size;                        // brush radius in WORLD units
    public float pressure;                    // op strength / magnitude reference (0..1 for the set ops)
    public TipMode tipMode;                   // how a continuous stroke renders its head; ignored by Stamp/PaintLine
    public BrushDirectionMode directionMode;   // how a map/directional brush orients its flow (see enum)

    public VectorFieldBrush(BrushShape shape, IVectorFieldBrushOp op, float size = 1.5f,
                            float pressure = 1f, TipMode tipMode = TipMode.Smoothed,
                            BrushDirectionMode directionMode = BrushDirectionMode.FollowStroke) {
        this.shape = shape;
        this.op = op;
        this.size = size;
        this.pressure = pressure;
        this.tipMode = tipMode;
        this.directionMode = directionMode;
    }

    public bool IsValid => shape != null && op != null;
}
