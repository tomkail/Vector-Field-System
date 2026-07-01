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

    // Begin a continuous stroke. Hold the result and call To() each frame; End() when finished (End returns the stroke
    // to a pool for reuse, so don't touch it after — begin a new one).
    public static VectorFieldStroke BeginStroke(this DrawableVectorFieldComponent field, in VectorFieldBrush brush)
        => VectorFieldStroke.Rent(field, brush);

    // Paint a single straight swept line (a one-shot stroke over two points).
    public static void PaintLine(this DrawableVectorFieldComponent field, in VectorFieldBrush brush, Vector3 fromWorld, Vector3 toWorld) {
        var stroke = field.BeginStroke(brush);
        stroke.To(fromWorld);
        stroke.To(toWorld);
        stroke.End();
    }

    // Stamp a single dab of the brush op centred at a world position. `direction` is the vector painted by
    // direction-using ops (Draw/Add) — pass a facing/velocity for a directional dab, or leave it default (Vector2.up).
    // Radial ops (Repel/Attract/Swirl) derive their direction from the stamp centre and ignore it.
    public static void Stamp(this DrawableVectorFieldComponent field, in VectorFieldBrush brush, Vector3 worldPosition,
                             Vector2 direction = default) {
        if (field == null || !brush.IsValid) return;
        var cc = field.gridRenderer.cellCenter;
        Vector2 gridCenter = cc.WorldToGridPosition(worldPosition);
        float gridRadius = Mathf.Max(0.5f, cc.WorldToGridVector(new Vector3(brush.size, 0f, 0f)).magnitude);
        Vector2 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.up;

        _stampCells.Clear();
        var size = field.PaintField.size;
        float invR = 1f / gridRadius;
        int minX = Mathf.Max(0, Mathf.FloorToInt(gridCenter.x - gridRadius));
        int maxX = Mathf.Min(size.x - 1, Mathf.CeilToInt(gridCenter.x + gridRadius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(gridCenter.y - gridRadius));
        int maxY = Mathf.Min(size.y - 1, Mathf.CeilToInt(gridCenter.y + gridRadius));

        // Map shapes are sampled in a frame oriented by `dir` (+Y = dir, +X = dir rotated -90), so a textured /
        // directional brush stamps with its emitter direction; radial shapes use `dir` for the whole dab.
        bool isMap = brush.shape.IsMap;
        Vector2 right = new Vector2(dir.y, -dir.x);

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                Vector2 offset = new Vector2(x - gridCenter.x, y - gridCenter.y);
                Vector2 force, strokeDir;
                if (isMap) {
                    Vector2 local = new Vector2(Vector2.Dot(offset, right), Vector2.Dot(offset, dir)) * invR;
                    Vector2 sample = brush.shape.Sample2D(local);
                    if (sample.sqrMagnitude <= 0f) continue;
                    force = sample.x * right + sample.y * dir;   // emitter vector rotated into world
                    strokeDir = force.normalized;
                } else {
                    float w = brush.shape.Weight(offset.magnitude * invR);
                    if (w <= 0f) continue;
                    force = dir * w;                              // magnitude = weight; direction used only by Draw/Add
                    strokeDir = dir;
                }
                _stampCells.Add(new VectorFieldBrushCell {
                    gridPoint = new Point(x, y),
                    brushForce = force,
                    finalForce = force,
                    strokeForce = strokeDir,
                    brushCenter = gridCenter,            // radial ops radiate from here
                });
            }
        }

        if (_stampCells.Count > 0 &&
            VectorFieldBrushKernel.Apply(field.PaintField, _stampCells, brush.pressure, brush.op, out RectInt dirty))
            field.MarkRegionDirty(dirty);
    }
}
