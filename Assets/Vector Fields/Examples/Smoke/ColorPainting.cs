using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime painting for a colour field (IPaintTarget<Color>) — the Color counterpart of VectorFieldPainting. Drives the
// SAME generic PaintStroke<Color>, so smoke strokes get the identical smooth, frame-rate-independent, no-dabbing
// behaviour the vector field's drawing tool has, and any fix to the stroke core applies to both.
//
//   var brush = new PaintBrush<Color>(BrushShape.Radial(0.6f), new SmokeDrawOp(Color.cyan), size: 1.5f);
//   var stroke = smoke.BeginStroke(brush);   // smoke : IPaintTarget<Color>
//   stroke.To(worldPos);                      // ...each frame while dragging
//   stroke.End();
public static class ColorPainting {
    static void Validate(IPaintTarget<Color> field, in PaintBrush<Color> brush) {
        if (field == null)
            throw new ArgumentNullException(nameof(field), "Cannot paint into a null colour field.");
        if (field.grid == null)
            throw new InvalidOperationException("Colour paint target has no grid yet — paint after it's enabled.");
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

    static readonly List<VectorFieldBrushCell> _stampCells = new List<VectorFieldBrushCell>();

    // Stamp a single radial dab of the brush op centred at a world position — the Color counterpart of
    // VectorFieldPainting.Stamp. Unlike a stroke (whose coverage builds by drag distance, so a stationary brush emits
    // nothing), a stamp deposits every call, so a held brush can keep emitting. Radial only: the op reads coverage
    // (brushForce magnitude) and smoke ops ignore direction, so no emitter map is involved.
    public static void Stamp(this IPaintTarget<Color> field, in PaintBrush<Color> brush, Vector3 worldPosition) {
        Validate(field, brush);
        var cc = field.grid;
        Vector2 gridCenter = cc.WorldToGridPosition(worldPosition);
        float gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(brush.size, 0f, 0f)).magnitude);
        float invR = 1f / gridRadius;

        _stampCells.Clear();
        var size = field.PaintField.size;
        int minX = Mathf.Max(0, Mathf.FloorToInt(gridCenter.x - gridRadius));
        int maxX = Mathf.Min(size.x - 1, Mathf.CeilToInt(gridCenter.x + gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(gridCenter.y - gridRadius));
        int maxY = Mathf.Min(size.y - 1, Mathf.CeilToInt(gridCenter.y + gridRadius));

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                Vector2 offset = new Vector2(x - gridCenter.x, y - gridCenter.y);
                float w = brush.shape.Weight(offset.magnitude * invR);
                if (w <= 0f) continue;
                _stampCells.Add(new VectorFieldBrushCell {
                    gridPoint = new Vector2Int(x, y),
                    brushForce = Vector2.up * w,   // magnitude = coverage; direction unused by scalar/colour ops
                    finalForce = Vector2.up * w,
                    strokeForce = Vector2.up,
                    brushCenter = gridCenter,
                });
            }
        }

        if (_stampCells.Count > 0 &&
            VectorFieldBrushKernel.Apply(field.PaintField, _stampCells, brush.pressure, brush.op, out RectInt dirty))
            field.MarkRegionDirty(dirty);
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
