using System.Collections.Generic;
using UnityEngine;

// Replace: set the cell toward the stroke force, capping magnitude by a falloff-weighted blend between the cell's
// current magnitude and the pressure. (Ported verbatim from the tool's old Draw.)
public sealed class DrawBrushOp : IVectorFieldBrushOp {
    public string Id => "draw";
    public string DisplayName => "Draw";
    public Color GizmoColor => Color.green;
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        var newValue = ctx.strokeForce * ctx.pressure;
        return Vector2.ClampMagnitude(newValue, Mathf.Lerp(ctx.current.magnitude, ctx.pressure, ctx.Weight));
    }
}

// Accumulate the stroke-applied brush vector onto the cell. (Ported verbatim from the tool's old DrawAdditive.)
public sealed class AdditiveBrushOp : IVectorFieldBrushOp {
    public string Id => "additive";
    public string DisplayName => "Add";
    public Color GizmoColor => new Color(0.4f, 0.7f, 1f);
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current + ctx.finalForce * ctx.pressure;
    }
}

// Scale the cell down by the brush weight, fading it toward zero. (Ported verbatim from the tool's old Erase.)
public sealed class EraseBrushOp : IVectorFieldBrushOp {
    public string Id => "erase";
    public string DisplayName => "Erase";
    public Color GizmoColor => new Color(1f, 0.4f, 0.3f);
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current * (ctx.finalForce.magnitude * ctx.pressure);
    }
}

// Push the field along the stroke direction (finger-paint advection), weighted by the brush falloff. Unlike Additive
// this ignores the brush emitter's own direction (uses strokeForce, not finalForce), so it drags existing flow.
// Ported from the legacy Smudge tool (minus its per-frame dt; stepping is handled by the stroke loop).
public sealed class SmudgeBrushOp : IVectorFieldBrushOp {
    public string Id => "smudge";
    public string DisplayName => "Smudge";
    public Color GizmoColor => new Color(1f, 0.85f, 0.3f);
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current + ctx.strokeForce * (ctx.Weight * ctx.pressure);
    }
}

// Grow magnitude along the cell's existing direction (intensify). Ported from the legacy Burn tool. A zero cell has no
// direction to grow along, so it stays zero. Pressure acts as the strength.
public sealed class BurnBrushOp : IVectorFieldBrushOp {
    public string Id => "burn";
    public string DisplayName => "Burn";
    public Color GizmoColor => new Color(1f, 0.6f, 0.2f);
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        return ctx.current + ((Vector2)ctx.current.normalized) * (ctx.Weight * ctx.pressure);
    }
}

// Shrink magnitude along the cell's existing direction (the inverse of Burn), without letting it cross zero and flip.
public sealed class DodgeBrushOp : IVectorFieldBrushOp {
    public string Id => "dodge";
    public string DisplayName => "Dodge";
    public Color GizmoColor => new Color(1f, 0.95f, 0.7f);
    public bool NeedsSnapshot => false;

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
    public Color GizmoColor => new Color(0.7f, 0.5f, 1f);
    public bool NeedsSnapshot => false;

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
    public Color GizmoColor => new Color(0.3f, 0.9f, 0.8f);
    public bool NeedsSnapshot => false;

    public Vector2 Apply(in BrushApplyContext ctx) {
        if (ctx.current == Vector2.zero) return ctx.current;
        return ((Vector2)ctx.current.normalized) * Mathf.Lerp(ctx.current.magnitude, ctx.pressure, ctx.Weight);
    }
}

// The ordered set of brush ops the editor exposes (overlay buttons, mode-cycle shortcut). New ops are added here;
// the tool and overlay have no per-op knowledge. Ops are stateless singletons.
public static class VectorFieldBrushOpRegistry {
    public static readonly IReadOnlyList<IVectorFieldBrushOp> Ops = new IVectorFieldBrushOp[] {
        new DrawBrushOp(),
        new AdditiveBrushOp(),
        new SmudgeBrushOp(),
        new EraseBrushOp(),
        new BurnBrushOp(),
        new DodgeBrushOp(),
        new ClampBrushOp(),
        new NormalizeBrushOp(),
    };

    // The action key forces this op as a temporary override; also the fallback when an id is unknown.
    public static readonly IVectorFieldBrushOp Erase = ById("erase");
    public static readonly IVectorFieldBrushOp Default = ById("draw");

    public static IVectorFieldBrushOp ById(string id) {
        for (int i = 0; i < Ops.Count; i++)
            if (Ops[i].Id == id) return Ops[i];
        return Ops[0];
    }

    public static int IndexOf(string id) {
        for (int i = 0; i < Ops.Count; i++)
            if (Ops[i].Id == id) return i;
        return 0;
    }
}
