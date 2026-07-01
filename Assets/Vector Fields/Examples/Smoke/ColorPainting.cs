using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime painting for a colour field (IPaintTarget<Color>) — the Color counterpart of VectorFieldPainting. Drives the
// SAME generic PaintStroke<Color>, so smoke strokes get the identical smooth, frame-rate-independent, no-dabbing
// behaviour the vector field's drawing tool has, and any fix to the stroke core applies to both.
//
//   var brush = new PaintBrush<Color>(VectorFieldBrushShape.Radial(0.6f), new SmokeDrawOp(Color.cyan), size: 1.5f);
//   var stroke = smoke.BeginStroke(brush);   // smoke : IPaintTarget<Color>
//   stroke.To(worldPos);                      // ...each frame while dragging
//   stroke.End();
public static class ColorPainting {
    static void Validate(IPaintTarget<Color> field, in PaintBrush<Color> brush) {
        if (field == null)
            throw new ArgumentNullException(nameof(field), "Cannot paint into a null colour field.");
        if (field.gridRenderer == null)
            throw new InvalidOperationException("Colour paint target has no GridRenderer yet — paint after it's enabled.");
        if (!brush.IsValid)
            throw new ArgumentException("Brush is invalid: it needs a shape and an IBrushOp<Color>.", nameof(brush));
    }

    // Begin a continuous stroke. Hold the result, call To() each frame, End() when finished (End pools it — begin a
    // new one after).
    public static ColorStroke BeginStroke(this IPaintTarget<Color> field, in PaintBrush<Color> brush) {
        Validate(field, brush);
        return ColorStroke.Rent(field, brush);
    }

    // Paint a single straight swept line (a one-shot stroke over two points).
    public static void PaintLine(this IPaintTarget<Color> field, in PaintBrush<Color> brush, Vector3 fromWorld, Vector3 toWorld) {
        var stroke = field.BeginStroke(brush);
        stroke.To(fromWorld);
        stroke.To(toWorld);
        stroke.End();
    }
}

// The Color stroke: preserves the pooled front-end pattern (like VectorFieldStroke) while all the logic lives in the
// generic PaintStroke<Color>. Owns the Color pool so many short-lived strokes don't churn GC.
public sealed class ColorStroke : PaintStroke<Color> {
    static readonly Stack<ColorStroke> s_pool = new Stack<ColorStroke>();
    const int PoolCapacity = 32;

    ColorStroke() {}   // pooled — created via Rent (see ColorPainting.BeginStroke)

    internal static ColorStroke Rent(IPaintTarget<Color> field, in PaintBrush<Color> brush) {
        var s = s_pool.Count > 0 ? s_pool.Pop() : new ColorStroke();
        s.Init(field, brush.shape, brush.op, brush.size, brush.pressure, brush.tipMode, brush.directionMode);
        return s;
    }

    protected override void ReturnToPool() {
        if (s_pool.Count < PoolCapacity) s_pool.Push(this);
    }
}
