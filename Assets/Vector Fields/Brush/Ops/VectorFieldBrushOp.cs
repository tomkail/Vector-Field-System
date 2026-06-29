using UnityEngine;

// A single brush operation, applied per cell under the brush. Implementations are stateless and pure: they read the
// context and return the cell's new value. This is the reusable core of the painting system — no editor dependency —
// so the same ops drive the scene-view tool, runtime painting, and code-driven edits. The editor tool is a thin shell
// that gathers input, picks an op, and feeds it through VectorFieldBrush.ApplyStroke.
public interface IVectorFieldBrushOp {
    // Stable identifier persisted in settings; must survive class renames and display-name changes.
    string Id { get; }
    // Label shown in the overlay's mode selector.
    string DisplayName { get; }
    // Colour of the scene-view brush cursor while this op is active.
    Color GizmoColor { get; }
    // True for ops that read neighbouring cells (smooth, sharpen, ...). When set, ApplyStroke gives the op a
    // pre-stroke snapshot in ctx.source so neighbour reads are order-independent (writes go to the live field).
    bool NeedsSnapshot { get; }

    Vector2 Apply(in BrushApplyContext ctx);
}

// One cell touched by the brush, with the brush's contribution at that cell. Produced by the editor (which owns the
// grid<->world geometry) and consumed by VectorFieldBrush.ApplyStroke.
public struct VectorFieldBrushCell {
    public Point gridPoint;
    // Raw brush-map sample at this cell (the emitter/cookie value). Its magnitude is the 0..1 falloff weight.
    public Vector2 brushForce;
    // brushForce scaled by the stroke magnitude and rotated to the stroke direction.
    public Vector2 finalForce;
}

// Everything an op needs to compute a cell's new value. Readonly struct, passed by `in` to avoid copies.
public readonly struct BrushApplyContext {
    public readonly Vector2 current;     // the cell's existing value
    public readonly Vector2 brushForce;  // raw brush-map sample (see VectorFieldBrushCell)
    public readonly Vector2 finalForce;  // stroke-applied brush vector (see VectorFieldBrushCell)
    public readonly Vector2 strokeForce; // this step's stroke force (direction * magnitude), independent of the cookie
    public readonly float pressure;
    public readonly Point gridPoint;
    public readonly Vector2 brushCenter; // grid-space stroke position, for radial ops (swirl/attract/repel)
    public readonly Vector2Map source;   // pre-stroke snapshot for neighbour reads; == the live field when not needed

    // The 0..1 falloff weight at this cell.
    public float Weight => brushForce.magnitude;

    public BrushApplyContext(Vector2 current, Vector2 brushForce, Vector2 finalForce, Vector2 strokeForce,
                             float pressure, Point gridPoint, Vector2 brushCenter, Vector2Map source) {
        this.current = current;
        this.brushForce = brushForce;
        this.finalForce = finalForce;
        this.strokeForce = strokeForce;
        this.pressure = pressure;
        this.gridPoint = gridPoint;
        this.brushCenter = brushCenter;
        this.source = source;
    }
}
