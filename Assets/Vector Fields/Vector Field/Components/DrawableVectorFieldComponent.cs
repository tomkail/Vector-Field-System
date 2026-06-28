using UnityEngine;

public class DrawableVectorFieldComponent : VectorFieldComponent {
    // The drawing tool paints into vectorField directly, then reports the touched grid rect via MarkRegionDirty so we
    // can upload just that sub-rect to renderTexture. Accumulates the union of regions painted since the last render
    // (the dirty pump coalesces several paint steps into one render); null means "re-upload the whole field".
    RectInt? pendingDirtyRegion;

    public void MarkRegionDirty(RectInt gridRegion) {
        pendingDirtyRegion = pendingDirtyRegion.HasValue ? Union(pendingDirtyRegion.Value, gridRegion) : gridRegion;
        SetDirty();
    }

    protected override void RenderInternal() {
        bool resized = vectorField == null || vectorField.values.Length != gridRenderer.gridSize.x * gridRenderer.gridSize.y;
        if (resized)
            vectorField = new Vector2Map(gridRenderer.gridSize);

        // Painted into directly on the CPU (see VectorFieldDrawingTool), but the draw path, GPU group blend, and
        // shader visualizer all sample renderTexture. Mirror the painted field into it so the strokes show up —
        // uploading just the painted region when we have one (and the texture's already a valid full copy), else the
        // whole field (first render, resize, Clear, or any non-paint change).
        if (pendingDirtyRegion.HasValue && !resized)
            WriteVectorFieldRegionToRenderTexture(pendingDirtyRegion.Value);
        else
            WriteVectorFieldToRenderTexture();
        pendingDirtyRegion = null;
    }

    [EasyButtons.Button]
    public void Clear() {
        vectorField.Clear();
        SetDirty();
    }

    static RectInt Union(RectInt a, RectInt b) {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
