using System.Collections.Generic;
using UnityEngine;

// Core stroke application: runs a brush op over the cells it touches and reports the dirty grid rect. No editor or
// scene dependency, so runtime/code can paint a field the same way the tool does. The caller owns grid<->world
// geometry and supplies the already-resolved cells (see VectorFieldBrushCell).
public static class VectorFieldBrush {
    // Applies `op` to every cell, writing results back into `field`. Returns false (and an empty region) when there
    // is nothing to do. `strokeForce` is this step's stroke vector; `brushCenter` is its grid-space position.
    public static bool ApplyStroke(Vector2Map field, IReadOnlyList<VectorFieldBrushCell> cells, Vector2 strokeForce,
                                   float pressure, Vector2 brushCenter, IVectorFieldBrushOp op, out RectInt dirtyRegion) {
        dirtyRegion = default;
        if (field == null || op == null || cells == null || cells.Count == 0)
            return false;

        // Neighbour-reading ops sample a pre-stroke snapshot so the result doesn't depend on cell iteration order;
        // per-cell ops read the live field directly (no allocation).
        Vector2Map source = op.NeedsSnapshot ? new Vector2Map(field.size, (Vector2[])field.values.Clone()) : field;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < cells.Count; i++) {
            var cell = cells[i];
            var ctx = new BrushApplyContext(
                field.GetValueAtGridPoint(cell.gridPoint), cell.brushForce, cell.finalForce,
                strokeForce, pressure, cell.gridPoint, brushCenter, source);
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
