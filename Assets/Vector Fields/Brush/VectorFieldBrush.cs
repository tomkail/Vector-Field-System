using System;
using UnityEngine;
using UnityEngine.Rendering;

// The brush's spatial profile used by the runtime painting API (Stamp / PaintLine / strokes). Three flavours:
//  - Radial(softness): a pure-CPU radial falloff. No direction of its own — the painted direction comes from the
//    stroke tangent (or a Stamp's direction argument). This is all most runtime effects need.
//  - FromMap(map): wraps a prebuilt 2D brush map for textured / per-cell-directional brushes. The sampled VECTOR is
//    the brush contribution: its magnitude is the coverage weight and its direction (in the brush's local frame,
//    local +Y = "forward") is the emitter/cookie direction.
//  - FromCookie(cookie, emitter): builds that 2D map on the GPU from a cookie mask + directional/spot emitter (the
//    same pipeline the editor tool uses), reads it back to the CPU, and wraps it via FromMap — the turnkey way to get
//    a textured/directional brush at runtime.
// Built once and reused.
public sealed class VectorFieldBrushShape {
    readonly float softness;      // radial: 0 = hard edge, 1 = fully soft
    readonly Vector2Map map;      // map flavour: the 2D brush map (null for radial)

    VectorFieldBrushShape(float softness) { this.softness = Mathf.Clamp01(softness); }
    VectorFieldBrushShape(Vector2Map map) { this.map = map; }

    public static VectorFieldBrushShape Radial(float softness = 0.5f) => new VectorFieldBrushShape(softness);
    public static VectorFieldBrushShape FromMap(Vector2Map map) => new VectorFieldBrushShape(map);

    // Build a textured/directional brush from a cookie mask + emitter, on the GPU, and cache it on the CPU. This is a
    // one-time build (a synchronous GPU readback stalls briefly) — do it in setup, not per frame, and reuse the shape.
    // Consumes the cookie's generated GPU mask (Falloff/Curve regenerate on demand if the cookie is reused).
    public static VectorFieldBrushShape FromCookie(VectorFieldCookieSource cookie, VectorFieldBrushSettings emitter,
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

    // True when this shape carries a 2D map (textured/directional); false for a pure radial falloff.
    public bool IsMap => map != null;

    // 0..1 weight at a normalized distance from the centre (0 = centre, 1 = edge). Radial flavour.
    public float Weight(float normalizedDistance) {
        float d = Mathf.Clamp01(normalizedDistance);
        float core = 1f - softness;                 // inside `core` the weight is full
        if (d <= core) return 1f;
        float t = (d - core) / Mathf.Max(1e-4f, 1f - core);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
    }

    // Sample the 2D map at a local position in [-1,1] (0 = centre, ±1 = edge). Returns the local-frame brush vector
    // (magnitude = weight). Outside the unit square the brush doesn't reach, so returns zero. Map flavour only.
    public Vector2 Sample2D(Vector2 local) {
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
public enum VectorFieldDirectionMode { FollowStroke, FixedAngle }

// A reusable, configured brush: shape + op + size + pressure + tip/direction mode. The friendly runtime API takes one
// of these. Cheap to copy (holds two references plus scalars); build once on a prefab/field and reuse every frame.
public struct VectorFieldBrush {
    public VectorFieldBrushShape shape;
    public IVectorFieldBrushOp op;
    public float size;                        // brush radius in WORLD units
    public float pressure;                    // op strength / magnitude reference (0..1 for the set ops)
    public TipMode tipMode;                   // how a continuous stroke renders its head; ignored by Stamp/PaintLine
    public VectorFieldDirectionMode directionMode;   // how a map/directional brush orients its flow (see enum)

    public VectorFieldBrush(VectorFieldBrushShape shape, IVectorFieldBrushOp op, float size = 1.5f,
                            float pressure = 1f, TipMode tipMode = TipMode.Smoothed,
                            VectorFieldDirectionMode directionMode = VectorFieldDirectionMode.FollowStroke) {
        this.shape = shape;
        this.op = op;
        this.size = size;
        this.pressure = pressure;
        this.tipMode = tipMode;
        this.directionMode = directionMode;
    }

    public bool IsValid => shape != null && op != null;
}
