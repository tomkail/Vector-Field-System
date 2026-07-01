using UnityEngine;

// The brush's spatial profile used by the runtime painting API (Stamp / PaintLine / strokes). Two flavours:
//  - Radial(softness): a pure-CPU radial falloff. No direction of its own — the painted direction comes from the
//    stroke tangent (or a Stamp's direction argument). This is all most runtime effects need.
//  - FromMap(map): wraps a prebuilt 2D brush map (e.g. the editor's cookie-shaped directional/spot emitter, read
//    back from the GPU) for textured / per-cell-directional brushes. The sampled VECTOR is the brush contribution:
//    its magnitude is the coverage weight and its direction (in the brush's local frame, local +Y = "forward") is
//    the emitter/cookie direction.
// Built once and reused.
public sealed class VectorFieldBrushShape {
    readonly float softness;      // radial: 0 = hard edge, 1 = fully soft
    readonly Vector2Map map;      // map flavour: the 2D brush map (null for radial)

    VectorFieldBrushShape(float softness) { this.softness = Mathf.Clamp01(softness); }
    VectorFieldBrushShape(Vector2Map map) { this.map = map; }

    public static VectorFieldBrushShape Radial(float softness = 0.5f) => new VectorFieldBrushShape(softness);
    public static VectorFieldBrushShape FromMap(Vector2Map map) => new VectorFieldBrushShape(map);

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

// A reusable, configured brush: shape + op + size + pressure + tip mode. The friendly runtime API takes one of these.
// Cheap to copy (holds two references plus scalars); build once on a prefab/field and reuse every frame.
public struct VectorFieldBrush {
    public VectorFieldBrushShape shape;
    public IVectorFieldBrushOp op;
    public float size;         // brush radius in WORLD units
    public float pressure;     // op strength / magnitude reference (0..1 for the set ops)
    public TipMode tipMode;    // how a continuous stroke renders its head; ignored by Stamp/PaintLine

    public VectorFieldBrush(VectorFieldBrushShape shape, IVectorFieldBrushOp op, float size = 1.5f,
                            float pressure = 1f, TipMode tipMode = TipMode.Smoothed) {
        this.shape = shape;
        this.op = op;
        this.size = size;
        this.pressure = pressure;
        this.tipMode = tipMode;
    }

    public bool IsValid => shape != null && op != null;
}
