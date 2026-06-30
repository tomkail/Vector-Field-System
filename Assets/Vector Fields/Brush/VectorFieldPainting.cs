using System.Collections.Generic;
using UnityEngine;

// The friendly runtime painting API: extension methods on DrawableVectorFieldComponent so gameplay code can paint a
// field the same way the editor tool does, without touching grid math or the kernel directly.
//
//   var brush = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.6f), VectorFieldBrushOpRegistry.ById("draw"), size: 2f);
//   field.Stamp(brush, hitPoint);                 // a single dab / burst
//   field.PaintLine(brush, origin, target);       // a straight swept line
//   var stroke = field.BeginStroke(brush);        // a continuous, smoothed stroke
//   stroke.To(player.position);                   // ...call each frame; smooth + frame-rate independent
//
// Pair with a fade strategy (VectorFieldDecay, a simulator's damping, or group-layer fade) so effects don't
// accumulate forever.
public static class VectorFieldPainting {
    static readonly List<VectorFieldBrushCell> _stampCells = new List<VectorFieldBrushCell>();

    // Begin a continuous stroke. Hold the result and call To() each frame; End() when finished.
    public static VectorFieldStroke BeginStroke(this DrawableVectorFieldComponent field, in VectorFieldBrush brush)
        => new VectorFieldStroke(field, brush);

    // Paint a single straight swept line (a one-shot stroke over two points).
    public static void PaintLine(this DrawableVectorFieldComponent field, in VectorFieldBrush brush, Vector3 fromWorld, Vector3 toWorld) {
        var stroke = field.BeginStroke(brush);
        stroke.To(fromWorld);
        stroke.To(toWorld);
        stroke.End();
    }

    // Stamp a single radial dab of the brush op centred at a world position (no path direction — best for radial ops
    // like Repel/Attract/Swirl, or a one-off Draw dab).
    public static void Stamp(this DrawableVectorFieldComponent field, in VectorFieldBrush brush, Vector3 worldPosition) {
        if (field == null || !brush.IsValid) return;
        var cc = field.gridRenderer.cellCenter;
        Vector2 gridCenter = cc.WorldToGridPosition(worldPosition);
        float gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(brush.size, 0f, 0f)).magnitude);

        _stampCells.Clear();
        var size = field.PaintField.size;
        float invR = 1f / gridRadius;
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
                    gridPoint = new Point(x, y),
                    brushForce = Vector2.up * w,        // magnitude = weight; a stamp has no path direction
                    finalForce = Vector2.up * w,
                    strokeForce = Vector2.up,
                    brushCenter = gridCenter,            // radial ops radiate from here
                });
            }
        }

        if (_stampCells.Count > 0 &&
            VectorFieldBrushKernel.Apply(field.PaintField, _stampCells, brush.pressure, brush.op, out RectInt dirty))
            field.MarkRegionDirty(dirty);
    }
}
