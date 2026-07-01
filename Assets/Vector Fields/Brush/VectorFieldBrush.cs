using UnityEngine;

// The brush's spatial profile — a CPU radial falloff used by the runtime painting API (Stamp / PaintLine / strokes).
// Built once and reused. (A FromMap variant that wraps the editor's GPU cookie brush could be added later for
// textured/directional brushes; the runtime path only needs a radial weight.)
public sealed class VectorFieldBrushShape {
    readonly float softness;   // 0 = hard edge, 1 = fully soft

    VectorFieldBrushShape(float softness) { this.softness = Mathf.Clamp01(softness); }

    public static VectorFieldBrushShape Radial(float softness = 0.5f) => new VectorFieldBrushShape(softness);

    // 0..1 weight at a normalized distance from the centre (0 = centre, 1 = edge).
    public float Weight(float normalizedDistance) {
        float d = Mathf.Clamp01(normalizedDistance);
        float core = 1f - softness;                 // inside `core` the weight is full
        if (d <= core) return 1f;
        float t = (d - core) / Mathf.Max(1e-4f, 1f - core);
        return 1f - Mathf.SmoothStep(0f, 1f, t);
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
