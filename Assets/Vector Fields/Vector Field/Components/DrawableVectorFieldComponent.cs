using UnityEngine;
using UnityEngine.Serialization;

public class DrawableVectorFieldComponent : VectorFieldComponent, ISerializationCallbackReceiver, IPaintTarget<Vector2> {
    // IPaintTarget<Vector2>: the generic painting core (PaintStroke<Vector2>, the brush kernel) drives this component.
    // gridRenderer and MarkRegionDirty already satisfy the interface; PaintField/CreateMap need explicit impls because
    // the interface is typed on the base TypeMap<Vector2> while our members are the Vector2Map subtype.
    TypeMap<Vector2> IPaintTarget<Vector2>.PaintField => PaintField;
    TypeMap<Vector2> IPaintTarget<Vector2>.CreateMap(Point size) => new Vector2Map(size);

    // The painted field — the authored source of truth, edited by the drawing tool. The render texture (and the
    // cookie-masked CPU copy in base.vectorField that consumers read back) are derived from this each render, so
    // masking and readback never touch the paint data. It's the WORKING copy, not serialized directly: it's written
    // to / rebuilt from the backing store below (see OnBeforeSerialize / OnAfterDeserialize) so the on-disk format can
    // be chosen per project (see VectorFieldStorage) without changing the runtime representation.
    [System.NonSerialized] Vector2Map paintField;

    // Serialized backing. The data always lives on the component (never an asset), stored in exactly ONE of these per
    // VectorFieldStorage.format: `storedValues` (verbose Vector2 array) or `storedBytes` (compact packed blob). Reading
    // detects which is populated, so switching the project setting doesn't break existing scenes.
    [SerializeField, HideInInspector] Point storedSize;
    [SerializeField, HideInInspector] Vector2[] storedValues;
    [SerializeField, HideInInspector] byte[] storedBytes;
    // Migration: older scenes serialized the whole Vector2Map under `paintField`. FormerlySerializedAs redirects that
    // to here; OnAfterDeserialize lifts it into the working field and clears it, so it re-saves in the current format.
    [SerializeField, HideInInspector, FormerlySerializedAs("paintField")] Vector2Map legacyPaintField;

    // The painted field, created/resized to the current grid on demand. The drawing tool reads and writes this.
    // A deserialized Vector2Map can come back non-null with a null `values` array (Unity rebuilds the managed object
    // without running its constructor), so treat that as "needs (re)building" alongside null / size mismatch.
    public Vector2Map PaintField {
        get {
            var size = gridRenderer != null ? gridRenderer.gridSize : (paintField != null ? paintField.size : Point.zero);
            if (!IsValid(paintField, size))
                paintField = new Vector2Map(size);
            return paintField;
        }
    }

    // Write the working field into the chosen backing (clearing the other), so the scene stores it in the project's
    // format. Reads only a plain static (VectorFieldStorage.format) — safe from a serialization callback. Runs in the
    // editor at save time; players never call this.
    public void OnBeforeSerialize() {
        legacyPaintField = null;   // migrated away; don't keep re-serializing the old representation
        if (paintField == null || paintField.values == null || paintField.values.Length == 0) {
            storedValues = null;
            storedBytes = null;
            return;
        }
        storedSize = paintField.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedBytes = VectorFieldStorage.Pack(paintField.values);
            storedValues = null;
        } else {
            storedValues = paintField.values;
            storedBytes = null;
        }
    }

    // Rebuild the working field from whichever backing is populated (or migrate a legacy paintField). Pure array work,
    // no Unity API, so it's safe on the deserialization thread. The grid-size reconciliation stays in PaintField's getter.
    public void OnAfterDeserialize() {
        if (legacyPaintField != null && legacyPaintField.values != null && legacyPaintField.values.Length > 0) {
            paintField = legacyPaintField;
            legacyPaintField = null;
        } else if (storedBytes != null && storedBytes.Length > 0) {
            paintField = new Vector2Map(storedSize, VectorFieldStorage.Unpack(storedBytes, storedSize.x * storedSize.y));
        } else if (storedValues != null && storedValues.Length > 0) {
            paintField = new Vector2Map(storedSize, storedValues);
        } else {
            paintField = null;   // built lazily by PaintField
        }
    }

    // A paint field is usable when it exists, has a backing array, and that array matches the requested grid size.
    static bool IsValid(Vector2Map field, Point size) =>
        field != null && field.values != null && field.values.Length == size.x * size.y;

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
        bool resized = !IsValid(paintField, gridRenderer.gridSize);
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

    // Replaces the painted field with a copy of `source`, resizing the grid to match. Code-callable entry point used
    // by the editor's Rasterize (baking any field type into an editable Drawable) and usable from script to seed a
    // drawable field. Writes into paintField — the authored source of truth — not base.vectorField (the readback
    // target), so the painting actually shows up and serializes.
    public void LoadPaintField(Vector2Map source) {
        if (source == null) return;
        if (gridRenderer != null) gridRenderer.gridSize = source.size;
        paintField = new Vector2Map(source);
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
