using UnityEngine;

public class DrawableVectorFieldComponent : VectorFieldComponent {
    // The painted field — the authored source of truth, edited by the drawing tool and serialized with the
    // component. The render texture (and the cookie-masked CPU copy in base.vectorField that consumers read back)
    // are derived from this each render, so masking and readback never touch the paint data.
    [SerializeField] Vector2Map paintField;

    // The painted field, created/resized to the current grid on demand. The drawing tool reads and writes this.
    public Vector2Map PaintField {
        get {
            var size = gridRenderer != null ? gridRenderer.gridSize : (paintField != null ? paintField.size : Point.zero);
            if (paintField == null || paintField.values.Length != size.x * size.y)
                paintField = new Vector2Map(size);
            return paintField;
        }
    }

    // Uploads to the GPU come from the painted field, not from base.vectorField (which is the readback target).
    protected override Vector2Map UploadSource => PaintField;

    // The drawing tool paints into paintField directly, then reports the touched grid rect via MarkRegionDirty so we
    // can upload just that sub-rect to renderTexture. Accumulates the union of regions painted since the last render
    // (the dirty pump coalesces several paint steps into one render); null means "re-upload the whole field".
    RectInt? pendingDirtyRegion;

    public void MarkRegionDirty(RectInt gridRegion) {
        pendingDirtyRegion = pendingDirtyRegion.HasValue ? Union(pendingDirtyRegion.Value, gridRegion) : gridRegion;
        SetDirty();
    }

    protected override void RenderInternal() {
        bool resized = paintField == null || paintField.values.Length != gridRenderer.gridSize.x * gridRenderer.gridSize.y;
        if (resized)
            paintField = new Vector2Map(gridRenderer.gridSize);

        // Painted into directly on the CPU (see VectorFieldDrawingTool), but the draw path, GPU group blend, and
        // shader visualizer all sample renderTexture. Mirror the painted field into it so the strokes show up —
        // uploading just the painted region when we have one (and the texture's already a valid full copy), else the
        // whole field (first render, resize, Clear, or any non-paint change).
        //
        // A cookie multiplies the whole render texture each render; the region path leaves earlier texels untouched,
        // so they'd be masked repeatedly and compound. Force a full re-upload (from the unmasked paint field) whenever
        // a cookie is active, so each render masks exactly once.
        bool useRegion = pendingDirtyRegion.HasValue && !resized && !(cookie != null && cookie.Enabled);
        if (useRegion)
            WriteVectorFieldRegionToRenderTexture(pendingDirtyRegion.Value);
        else
            WriteVectorFieldToRenderTexture();
        pendingDirtyRegion = null;
    }

    [EasyButtons.Button]
    public void Clear() {
        PaintField.Clear();
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
