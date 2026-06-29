using System.Collections.Generic;
using UnityEngine;

// Core cell-application kernel: runs a brush op over a batch of cells and reports the dirty grid rect. No editor or
// scene dependency, so runtime/code paints a field the same way the tool does. The caller owns grid<->world geometry
// and supplies the already-resolved cells (see VectorFieldBrushCell). This applies each cell exactly once — overlap
// across a moving swept stroke is handled a level up by VectorFieldStroke.
public static class VectorFieldBrushKernel {
    // Applies `op` to every cell, writing results back into `field`. Returns false (empty region) when there is
    // nothing to do. strokeForce/brushCenter now live per-cell on VectorFieldBrushCell.
    public static bool Apply(Vector2Map field, IReadOnlyList<VectorFieldBrushCell> cells, float pressure,
                             IVectorFieldBrushOp op, out RectInt dirtyRegion) {
        dirtyRegion = default;
        if (field == null || op == null || cells == null || cells.Count == 0)
            return false;

        // Neighbour-reading ops sample a pre-stroke snapshot so the result doesn't depend on cell iteration order;
        // other ops read the live field directly (no allocation).
        Vector2Map source = op.NeedsSnapshot ? new Vector2Map(field.size, (Vector2[])field.values.Clone()) : field;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < cells.Count; i++) {
            var cell = cells[i];
            var ctx = new BrushApplyContext(
                field.GetValueAtGridPoint(cell.gridPoint), cell.brushForce, cell.finalForce,
                cell.strokeForce, pressure, cell.gridPoint, cell.brushCenter, source);
            field.SetValueAtGridPoint(cell.gridPoint, op.Apply(ctx));

            if (cell.gridPoint.x < minX) minX = cell.gridPoint.x;
            if (cell.gridPoint.y < minY) minY = cell.gridPoint.y;
            if (cell.gridPoint.x > maxX) maxX = cell.gridPoint.x;
            if (cell.gridPoint.y > maxY) maxY = cell.gridPoint.y;
        }

        // RectInt's max is exclusive, so +1 to include the max cell.
        dirtyRegion = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return true;
    }
}
