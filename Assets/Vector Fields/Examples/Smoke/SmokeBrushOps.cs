using UnityEngine;

// Brush ops for a colour/smoke field (IBrushOp<Color>). These carry the colour to paint (unlike the vector ops, whose
// value comes from the emitter direction), so they're constructed per-brush rather than being stateless singletons.
// They drive the SAME generic PaintStroke<Color> / brush kernel the vector field uses — only the per-cell value math
// differs. The field they paint is a smoke emission source: the sim injects it into the density each step (see
// SmokeSimulationComponent), so painting deposits "smoke to release", which the velocity field then advects.

// Set the cell toward the brush colour by the coverage weight (a soft, non-compounding paint). Overlapping strokes
// settle at the colour rather than piling past it, so it reads like laying down dye.
public sealed class SmokeDrawOp : IBrushOp<Color> {
    readonly Color _color;
    public SmokeDrawOp(Color color) { _color = color; }

    public string Id => "smoke_draw";
    public string DisplayName => "Smoke";
    public string Tooltip => "Lay down coloured smoke.";
    public Color GizmoColor => _color;
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => false;   // sets toward the colour; stable under re-apply
    public bool UsesBrushDirection => false;

    public Color Apply(in BrushApplyContext<Color> ctx) {
        return Color.Lerp(ctx.current, _color, Mathf.Clamp01(ctx.Weight * ctx.pressure));
    }
}

// Accumulate colour onto the cell (piles up with repeated coverage). Useful for building dense plumes.
public sealed class SmokeAddOp : IBrushOp<Color> {
    readonly Color _color;
    public SmokeAddOp(Color color) { _color = color; }

    public string Id => "smoke_add";
    public string DisplayName => "Add Smoke";
    public string Tooltip => "Add coloured smoke (accumulates).";
    public Color GizmoColor => _color;
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // current + colour accumulates
    public bool UsesBrushDirection => false;

    public Color Apply(in BrushApplyContext<Color> ctx) {
        return ctx.current + _color * (ctx.Weight * ctx.pressure);
    }
}

// Fade the cell toward transparent by the coverage weight — an eraser for the emission source.
public sealed class SmokeEraseOp : IBrushOp<Color> {
    public string Id => "smoke_erase";
    public string DisplayName => "Erase Smoke";
    public string Tooltip => "Fade the smoke source toward empty.";
    public Color GizmoColor => new Color(1f, 0.4f, 0.3f);
    public bool NeedsSnapshot => false;
    public bool CompoundsOnReapply => true;    // multiplies down; re-apply fades further
    public bool UsesBrushDirection => false;

    public Color Apply(in BrushApplyContext<Color> ctx) {
        return ctx.current * (1f - Mathf.Clamp01(ctx.Weight * ctx.pressure));
    }
}
