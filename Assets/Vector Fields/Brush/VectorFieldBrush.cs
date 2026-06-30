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

// A reusable, configured brush: shape + op + size + pressure. The friendly runtime API takes one of these. Cheap to
// copy (holds two references plus two floats); build once on a prefab/field and reuse every frame.
public struct VectorFieldBrush {
    public VectorFieldBrushShape shape;
    public IVectorFieldBrushOp op;
    public float size;       // brush radius in WORLD units
    public float pressure;   // op strength / magnitude reference (0..1 for the set ops)

    public VectorFieldBrush(VectorFieldBrushShape shape, IVectorFieldBrushOp op, float size = 1.5f, float pressure = 1f) {
        this.shape = shape;
        this.op = op;
        this.size = size;
        this.pressure = pressure;
    }

    public bool IsValid => shape != null && op != null;
}
