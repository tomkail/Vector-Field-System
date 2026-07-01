using System.Collections.Generic;
using UnityEngine;

// Replace: set the cell toward the stroke force, capping magnitude by a falloff-weighted blend between the cell's
// current magnitude and the pressure. (Ported verbatim from the tool's old Draw.)
public sealed class DrawBrushOp : IVectorFieldBrushOp {
    public string Id => "draw";
    public string DisplayName => "Draw";
    public string Tooltip => "Set the field to the brush direction.";
    public Color GizmoColor => Color.green;
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;   // sets toward a target; stable under re-apply
    public bool UsesBrushDirection => true;     // paints the emitter's direction

    public Vector2 Apply(in BrushApplyContext ctx) {
        // Take only the DIRECTION from the stroke — its magnitude carries a 1/size factor (good for Add's accumulation,
        // wrong for a set op). Target magnitude is the pressure, blended toward the current magnitude by the falloff,
        // so the painted strength is independent of brush size.
        if (ctx.strokeForce.sqrMagnitude < 1e-12f) return ctx.current;
        var newValue = ((Vector2)ctx.strokeForce.normalized) * ctx.pressure;
        return Vector2.ClampMagnitude(newValue, Mathf.Lerp(ctx.current.magnitude, ctx.pressure, ctx.Weight));
    }
}

// Accumulate the stroke-applied brush vector onto the cell. (Ported verbatim from the tool's old DrawAdditive.)
public sealed class AdditiveBrushOp : IVectorFieldBrushOp {
    public string Id => "additive";
    public string DisplayName => "Add";
    public string Tooltip => "Add the brush vector to the field.";
    public Color GizmoColor => new Color(0.4f, 0.7f, 1f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // current + force accumulates
    public bool UsesBrushDirection => true;     // paints the emitter's direction

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current + ctx.finalForce * ctx.pressure;
    }
}

// True advection (finger-paint smudge): each cell pulls the field from a little upstream of the stroke — where the
// brush just came from — and blends toward it, so existing flow is dragged along with the cursor rather than a new
// vector being deposited. Reads a pre-stroke snapshot (ctx.source) so the smear is order-independent within a step;
// successive stroke steps build the streak.
public sealed class SmudgeBrushOp : IVectorFieldBrushOp {
    // How far upstream (grid cells) to sample per application. Small, so repeated stroke steps accumulate the smear.
    const float Reach = 1.5f;

    public string Id => "smudge";
    public string DisplayName => "Smudge";
    public string Tooltip => "Drag existing flow along the stroke (advection).";
    public Color GizmoColor => new Color(1f, 0.85f, 0.3f);
    public bool NeedsSnapshot => true;          // samples the field upstream; must read a stable snapshot
    public bool CompoundsOnReapply => true;     // each pass drags further; not idempotent
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        Vector2 dir = ctx.strokeForce.sqrMagnitude > 1e-8f ? ctx.strokeForce.normalized : Vector2.zero;
        if (dir == Vector2.zero || ctx.source == null) return ctx.current;
        // Sample where the brush came from and blend the cell toward it, dragging the existing flow forward.
        Vector2 upstream = new Vector2(ctx.gridPoint.x, ctx.gridPoint.y) - dir * Reach;
        Vector2 dragged = ctx.source.GetValueAtGridPosition(upstream);
        return Vector2.Lerp(ctx.current, dragged, Mathf.Clamp01(ctx.Weight * ctx.pressure));
    }
}

// Fade the cell toward zero by the brush weight: full weight erases completely, the soft edge partially. Strongest at
// the brush centre (weight 1), fading out at the edge — the whole erase for one stroke pass (repeated passes compound).
public sealed class EraseBrushOp : IVectorFieldBrushOp {
    public string Id => "erase";
    public string DisplayName => "Erase";
    public string Tooltip => "Fade the field toward zero.";
    public Color GizmoColor => new Color(1f, 0.4f, 0.3f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // multiplies down; re-apply darkens further
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current * (1f - Mathf.Clamp01(ctx.Weight * ctx.pressure));
    }
}

// Grow magnitude along the cell's existing direction (intensify). Ported from the legacy Burn tool. A zero cell has no
// direction to grow along, so it stays zero. Pressure acts as the strength.
public sealed class BurnBrushOp : IVectorFieldBrushOp {
    public string Id => "burn";
    public string DisplayName => "Burn";
    public string Tooltip => "Increase vector magnitude.";
    public Color GizmoColor => new Color(1f, 0.6f, 0.2f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // grows magnitude; re-apply grows further
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current + ((Vector2)ctx.current.normalized) * (ctx.Weight * ctx.pressure);
    }
}

// Shrink magnitude along the cell's existing direction (the inverse of Burn), without letting it cross zero and flip.
public sealed class DodgeBrushOp : IVectorFieldBrushOp {
    public string Id => "dodge";
    public string DisplayName => "Dodge";
    public string Tooltip => "Decrease vector magnitude.";
    public Color GizmoColor => new Color(1f, 0.95f, 0.7f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // shrinks magnitude; re-apply shrinks further
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        float newMag = Mathf.Max(0f, ctx.current.magnitude - ctx.Weight * ctx.pressure);
        return ((Vector2)ctx.current.normalized) * newMag;
    }
}

// Pull magnitude down toward a ceiling (keeps direction), weighted by the brush falloff. Pressure is the ceiling — it
// doubles as the field's magnitude reference throughout these ops (Draw caps painted magnitude at pressure). Ported
// from the legacy Clamp tool.
public sealed class ClampBrushOp : IVectorFieldBrushOp {
    public string Id => "clamp";
    public string DisplayName => "Clamp";
    public string Tooltip => "Cap magnitude at the pressure value.";
    public Color GizmoColor => new Color(0.7f, 0.5f, 1f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;   // drives toward min(mag, ceiling); stable under re-apply
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        float mag = ctx.current.magnitude;
        float target = Mathf.Min(mag, ctx.pressure);
        return ((Vector2)ctx.current.normalized) * Mathf.Lerp(mag, target, ctx.Weight);
    }
}

// Drive magnitude toward a fixed length (pressure) while keeping direction, weighted by the brush falloff. A zero cell
// has no direction, so it stays zero.
public sealed class NormalizeBrushOp : IVectorFieldBrushOp {
    public string Id => "normalize";
    public string DisplayName => "Normalize";
    public string Tooltip => "Set magnitude to the pressure value.";
    public Color GizmoColor => new Color(0.3f, 0.9f, 0.8f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;   // drives toward a fixed length; stable under re-apply
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        if (ctx.current == Vector2.zero) return ctx.current;
        return ((Vector2)ctx.current.normalized) * Mathf.Lerp(ctx.current.magnitude, ctx.pressure, ctx.Weight);
    }
}

// --- Radial ops: derive direction from the cell's offset to brushCenter, not from the brush sample --------------
// Set the cell to point away from the brush centre — a source / outward blast (the "explode at the end" case).
public sealed class RepelBrushOp : IVectorFieldBrushOp {
    public string Id => "repel";
    public string DisplayName => "Repel";
    public string Tooltip => "Point vectors outward from the brush (burst).";
    public Color GizmoColor => new Color(1f, 0.5f, 0.2f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;   // sets toward an outward target; stable under re-apply
    public bool UsesBrushDirection => false;    // direction comes from the brush centre, not the emitter

    public Vector2 Apply(in BrushApplyContext ctx) {
        Vector2 toCell = new Vector2(ctx.gridPoint.x, ctx.gridPoint.y) - ctx.brushCenter;
        Vector2 dir = toCell.sqrMagnitude > 1e-6f ? toCell.normalized : Vector2.zero;
        return Vector2.Lerp(ctx.current, dir * ctx.pressure, ctx.Weight);
    }
}

// Set the cell to point toward the brush centre — a sink / inward pull.
public sealed class AttractBrushOp : IVectorFieldBrushOp {
    public string Id => "attract";
    public string DisplayName => "Attract";
    public string Tooltip => "Point vectors inward toward the brush (sink).";
    public Color GizmoColor => new Color(0.5f, 0.6f, 1f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        Vector2 toCenter = ctx.brushCenter - new Vector2(ctx.gridPoint.x, ctx.gridPoint.y);
        Vector2 dir = toCenter.sqrMagnitude > 1e-6f ? toCenter.normalized : Vector2.zero;
        return Vector2.Lerp(ctx.current, dir * ctx.pressure, ctx.Weight);
    }
}

// Set the cell tangent to a circle around the brush centre — a vortex / whirlpool.
public sealed class SwirlBrushOp : IVectorFieldBrushOp {
    public string Id => "swirl";
    public string DisplayName => "Swirl";
    public string Tooltip => "Circulate vectors around the brush (vortex).";
    public Color GizmoColor => new Color(0.6f, 0.4f, 1f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;
    public bool UsesBrushDirection => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        Vector2 toCell = new Vector2(ctx.gridPoint.x, ctx.gridPoint.y) - ctx.brushCenter;
        // 90° CCW perpendicular gives the tangential (circulating) direction.
        Vector2 tangent = new Vector2(-toCell.y, toCell.x);
        Vector2 dir = tangent.sqrMagnitude > 1e-6f ? tangent.normalized : Vector2.zero;
        return Vector2.Lerp(ctx.current, dir * ctx.pressure, ctx.Weight);
    }
}

// A named, ordered set of related ops, used to lay out the overlay's mode selector in tidy groups.
public sealed class VectorFieldBrushOpGroup {
    public readonly string name;
    public readonly IReadOnlyList<IVectorFieldBrushOp> ops;
    public VectorFieldBrushOpGroup(string name, params IVectorFieldBrushOp[] ops) {
        this.name = name;
        this.ops = ops;
    }
}

// The brush ops the editor exposes, grouped for display (overlay buttons) and flattened for the mode-cycle shortcut
// and id lookup. New ops are added to a group here; the tool and overlay have no per-op knowledge. Ops are stateless
// singletons.
public static class VectorFieldBrushOpRegistry {
    public static readonly IReadOnlyList<VectorFieldBrushOpGroup> Groups = new[] {
        new VectorFieldBrushOpGroup("Paint",     new DrawBrushOp(), new AdditiveBrushOp(), new SmudgeBrushOp(), new EraseBrushOp()),
        new VectorFieldBrushOpGroup("Magnitude", new BurnBrushOp(), new DodgeBrushOp(), new ClampBrushOp(), new NormalizeBrushOp()),
        new VectorFieldBrushOpGroup("Shape",     new RepelBrushOp(), new AttractBrushOp(), new SwirlBrushOp()),
    };

    // Flattened in group order — the source of truth for cycling and id lookup.
    public static readonly IReadOnlyList<IVectorFieldBrushOp> Ops = Flatten(Groups);

    // Named accessors so code doesn't hand-type op ids (a typo in ById(...) silently falls back to Draw). Prefer these
    // for hardcoded ops; ById is for ids that arrive as data (serialized fields, config). Default/Erase double as the
    // fallback op and the action-key override in the editor tool.
    public static readonly IVectorFieldBrushOp Draw      = ById("draw");
    public static readonly IVectorFieldBrushOp Additive  = ById("additive");
    public static readonly IVectorFieldBrushOp Smudge    = ById("smudge");
    public static readonly IVectorFieldBrushOp Erase     = ById("erase");
    public static readonly IVectorFieldBrushOp Burn      = ById("burn");
    public static readonly IVectorFieldBrushOp Dodge     = ById("dodge");
    public static readonly IVectorFieldBrushOp Clamp     = ById("clamp");
    public static readonly IVectorFieldBrushOp Normalize = ById("normalize");
    public static readonly IVectorFieldBrushOp Repel     = ById("repel");
    public static readonly IVectorFieldBrushOp Attract   = ById("attract");
    public static readonly IVectorFieldBrushOp Swirl     = ById("swirl");
    public static readonly IVectorFieldBrushOp Default   = Draw;

    static IVectorFieldBrushOp[] Flatten(IReadOnlyList<VectorFieldBrushOpGroup> groups) {
        var list = new List<IVectorFieldBrushOp>();
        foreach (var g in groups) list.AddRange(g.ops);
        return list.ToArray();
    }

    // Resolve an op by its stable id. Unknown ids warn and fall back to the default op (rather than silently), so a
    // typo surfaces instead of quietly painting with the wrong tool.
    public static IVectorFieldBrushOp ById(string id) {
        for (int i = 0; i < Ops.Count; i++)
            if (Ops[i].Id == id) return Ops[i];
        Debug.LogWarning($"[VectorFieldBrushOpRegistry] Unknown brush op id '{id}'; using '{Ops[0].Id}'. " +
                         "See VectorFieldBrushOpRegistry.Ops for valid ids, or use the named accessors (Draw, Erase, ...).");
        return Ops[0];
    }

    public static int IndexOf(string id) {
        for (int i = 0; i < Ops.Count; i++)
            if (Ops[i].Id == id) return i;
        return 0;
    }
}
