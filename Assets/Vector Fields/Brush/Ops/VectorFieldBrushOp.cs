using UnityEngine;

// A single brush operation, applied per cell under the brush. Implementations are stateless and pure: they read the
// context and return the cell's new value. This is the reusable core of the painting system — no editor dependency —
// so the same ops drive the scene-view tool, runtime painting, and code-driven edits. The editor tool is a thin shell
// that gathers input, picks an op, and feeds it through VectorFieldBrushKernel.Apply.
public interface IVectorFieldBrushOp {
    // Stable identifier persisted in settings; must survive class renames and display-name changes.
    string Id { get; }
    // Label shown in the overlay's mode selector.
    string DisplayName { get; }
    // One-line description shown as the selector button's tooltip.
    string Tooltip { get; }
    // Colour of the scene-view brush cursor while this op is active (and the selector button's accent).
    Color GizmoColor { get; }
    // True for ops that read neighbouring cells (smooth, sharpen, ...). When set, the kernel gives the op a
    // pre-stroke snapshot in ctx.source so neighbour reads are order-independent (writes go to the live field).
    bool NeedsSnapshot { get; }
    // True when re-applying the op at the same coverage changes the result (additive/erase/burn/…), i.e. it is NOT
    // idempotent. A swept stroke revisits cells behind its moving head; for ops that compound, VectorFieldStroke
    // re-applies from a pre-stroke snapshot using max coverage so the stroke equals one coverage-weighted pass.
    // Set-style ops (Draw/Clamp/Normalize) are stable under re-apply and skip that (cheaper) path.
    bool CompoundsOnReapply { get; }
    // True for ops that paint a vector derived from the brush emitter's direction (Draw/Add). The editor shows the
    // emitter direction controls and the scene-view direction arrow only for these; magnitude/radial/smudge ops
    // ignore the emitter direction, so it's hidden for them.
    bool UsesBrushDirection { get; }

    Vector2 Apply(in BrushApplyContext ctx);
}

// One cell touched by the brush, with the brush's contribution at that cell. Produced by the cell builders (which own
// the grid<->world geometry) and consumed by VectorFieldBrushKernel.Apply. strokeForce/brushCenter are per-cell (not
// per-batch) so a curved swept stroke can vary the painted direction and radial centre along its length.
public struct VectorFieldBrushCell {
    public Point gridPoint;
    // Raw brush sample at this cell (emitter/cookie value, or radial falloff). Its magnitude is the 0..1 weight.
    public Vector2 brushForce;
    // brushForce scaled by the stroke magnitude and rotated to the stroke/path direction.
    public Vector2 finalForce;
    // The local stroke vector at this cell (direction * step magnitude), independent of the cookie. For a swept
    // stroke this is the spline tangent; for a point stamp it is the stamp's stroke direction.
    public Vector2 strokeForce;
    // Grid-space reference point for radial ops (swirl/attract/repel): the stamp centre, or the nearest point on the
    // stroke path.
    public Vector2 brushCenter;
}

// Everything an op needs to compute a cell's new value. Readonly struct, passed by `in` to avoid copies.
public readonly struct BrushApplyContext {
    public readonly Vector2 current;     // the cell's existing value
    public readonly Vector2 brushForce;  // raw brush sample (see VectorFieldBrushCell)
    public readonly Vector2 finalForce;  // stroke-applied brush vector (see VectorFieldBrushCell)
    public readonly Vector2 strokeForce; // local stroke force (direction * magnitude), independent of the cookie
    public readonly float pressure;
    public readonly Point gridPoint;
    public readonly Vector2 brushCenter; // grid-space reference point, for radial ops (swirl/attract/repel)
    public readonly Vector2Map source;   // where neighbour reads sample from; == the live field when not needed

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
