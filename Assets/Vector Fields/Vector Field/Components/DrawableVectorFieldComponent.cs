using UnityEngine;
using UnityEngine.Serialization;

public class DrawableVectorFieldComponent : VectorFieldComponent, ISerializationCallbackReceiver, IPaintTarget<Vector2> {
    // IPaintTarget<Vector2>: the generic painting core (PaintStroke<Vector2>, the brush kernel) drives this component.
    // gridRenderer and MarkRegionDirty already satisfy the interface; PaintField/CreateMap need explicit impls because
    // the interface is typed on the base TypeMap<Vector2> while our members are the Vector2Map subtype.
    TypeMap<Vector2> IPaintTarget<Vector2>.PaintField => PaintField;
    TypeMap<Vector2> IPaintTarget<Vector2>.CreateMap(Point size) => new Vector2Map(size);

    // Optional shareable source. When assigned, this component paints INTO the asset (the data lives in the asset and
    // is reusable across components); when null, the field is stored on this component in the scene — the default, so
    // there are no orphans. Switch modes without losing data via ExtractToAsset / BakeIntoComponent.
    [SerializeField, Tooltip("Optional: store this field in a reusable asset instead of on the component. Leave empty " +
                             "to keep it on the component (saved in the scene).")]
    VectorFieldAsset sourceAsset;

    // The painted field — the authored source of truth, edited by the drawing tool. The render texture (and the
    // cookie-masked CPU copy in base.vectorField that consumers read back) are derived from this each render, so
    // masking and readback never touch the paint data. It's the WORKING copy, not serialized directly: it's written
    // to / rebuilt from the backing store below (see OnBeforeSerialize / OnAfterDeserialize) so the on-disk format can
    // be chosen per project (see VectorFieldStorage) without changing the runtime representation. Used only when no
    // sourceAsset is assigned (otherwise the asset's map is the working copy — see ActiveMap).
    [System.NonSerialized] Vector2Map paintField;

    // Serialized backing. The data always lives on the component (never an asset), stored in exactly ONE of these per
    // VectorFieldStorage.format: `storedValues` (verbose Vector2 array) or `storedBytes` (compact packed blob). Reading
    // detects which is populated, so switching the project setting doesn't break existing scenes.
    [SerializeField, HideInInspector] Point storedSize;
    [SerializeField, HideInInspector] Vector2[] storedValues;   // Vector2Array format
    [SerializeField, HideInInspector] string[] storedRows;      // ByteArray format: one base64 row per line (local diffs)
    [SerializeField, HideInInspector] byte[] storedBytes;       // legacy single-blob byte format — read only, for migration
    // Migration: older scenes serialized the whole Vector2Map under `paintField`. FormerlySerializedAs redirects that
    // to here; OnAfterDeserialize lifts it into the working field and clears it, so it re-saves in the current format.
    [SerializeField, HideInInspector, FormerlySerializedAs("paintField")] Vector2Map legacyPaintField;

    // The map currently backing this field: the linked asset's when `sourceAsset` is set (data in the asset, shared),
    // otherwise the component's own `paintField` (data in the scene). All paint/read/render go through this, so the
    // two modes are otherwise identical.
    Vector2Map ActiveMap {
        get => sourceAsset != null ? sourceAsset.Field : paintField;
        set { if (sourceAsset != null) sourceAsset.Field = value; else paintField = value; }
    }

    // Ensure the active map exists at the current grid size; returns true if it had to be (re)created (a resize),
    // which RenderInternal uses to choose between a region and a full GPU upload. A deserialized Vector2Map can come
    // back non-null with a null `values` array, so IsValid treats that as "needs (re)building" too.
    bool EnsurePaintField() {
        var size = gridRenderer != null ? gridRenderer.gridSize : (ActiveMap != null ? ActiveMap.size : Point.zero);
        if (IsValid(ActiveMap, size)) return false;
        ActiveMap = new Vector2Map(size);
        return true;
    }

    // The painted field, created/resized to the current grid on demand. The drawing tool reads and writes this.
    public Vector2Map PaintField {
        get { EnsurePaintField(); return ActiveMap; }
    }

    // Write the working field into the chosen backing (clearing the other), so the scene stores it in the project's
    // format. Reads only a plain static (VectorFieldStorage.format) — safe from a serialization callback. Runs in the
    // editor at save time; players never call this.
    public void OnBeforeSerialize() {
        legacyPaintField = null;   // migrated away; don't keep re-serializing the old representation
        storedBytes = null;        // legacy blob is never written now; clearing it completes migration on re-save
        // In asset mode the data lives in the asset, so the component stores nothing but the (serialized) asset
        // reference — never a stale copy of the grid.
        if (sourceAsset != null || paintField == null || paintField.values == null || paintField.values.Length == 0) {
            storedValues = null;
            storedRows = null;
            return;
        }
        storedSize = paintField.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedRows = VectorFieldStorage.PackRows(paintField.values, paintField.size);
            storedValues = null;
        } else {
            storedValues = paintField.values;
            storedRows = null;
        }
    }

    // Rebuild the working field from whichever backing is populated (or migrate a legacy paintField). Pure array work,
    // no Unity API, so it's safe on the deserialization thread. The grid-size reconciliation stays in PaintField's getter.
    public void OnAfterDeserialize() {
        if (legacyPaintField != null && legacyPaintField.values != null && legacyPaintField.values.Length > 0) {
            paintField = legacyPaintField;
            legacyPaintField = null;
        } else if (storedRows != null && storedRows.Length > 0) {
            paintField = new Vector2Map(storedSize, VectorFieldStorage.UnpackRows(storedRows, storedSize));
        } else if (storedBytes != null && storedBytes.Length > 0) {   // migrate old single-blob byte format
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
        MarkSourceAssetDirty();
    }

    // When painting into a linked asset, flag it dirty so the edit is written back to the .asset file.
    void MarkSourceAssetDirty() {
#if UNITY_EDITOR
        if (sourceAsset != null) UnityEditor.EditorUtility.SetDirty(sourceAsset);
#endif
    }

    protected override void RenderInternal() {
        bool resized = EnsurePaintField();

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
        MarkSourceAssetDirty();
    }

#if UNITY_EDITOR
    // Move the current field into a new reusable asset and link this component to it (switch to asset mode). The data
    // now lives in the .asset; the component stores only the reference.
    [EasyButtons.Button]
    public void ExtractToAsset() {
        var current = new Vector2Map(PaintField);   // copy so the asset owns its data
        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Extract Vector Field to Asset", name + " Field", "asset",
            "Save this painted field as a reusable asset and link the component to it.");
        if (string.IsNullOrEmpty(path)) return;
        var asset = ScriptableObject.CreateInstance<VectorFieldAsset>();
        asset.Field = current;
        UnityEditor.AssetDatabase.CreateAsset(asset, path);
        UnityEditor.AssetDatabase.SaveAssets();
        sourceAsset = asset;
        paintField = null;                          // now sourced from the asset
        UnityEditor.EditorUtility.SetDirty(this);
        SetDirty();
    }

    // Copy the linked asset's field onto this component and unlink (switch to on-component mode), so the data is saved
    // in the scene and no longer depends on the asset. "Saving directly on the object" is never lost.
    [EasyButtons.Button]
    public void BakeIntoComponent() {
        if (sourceAsset == null || sourceAsset.Field == null) return;
        paintField = new Vector2Map(sourceAsset.Field);   // copy asset data onto the component
        sourceAsset = null;                               // unlink; data now lives in the scene
        UnityEditor.EditorUtility.SetDirty(this);
        SetDirty();
    }
#endif

    // Replaces the painted field with a copy of `source`, resizing the grid to match. Code-callable entry point used
    // by the editor's Rasterize (baking any field type into an editable Drawable) and usable from script to seed a
    // drawable field. Writes into paintField — the authored source of truth — not base.vectorField (the readback
    // target), so the painting actually shows up and serializes.
    public void LoadPaintField(Vector2Map source) {
        if (source == null) return;
        if (gridRenderer != null) gridRenderer.gridSize = source.size;
        ActiveMap = new Vector2Map(source);   // writes to the asset in asset mode, else the component
        SetDirty();
        MarkSourceAssetDirty();
    }

    static RectInt Union(RectInt a, RectInt b) {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax);
        int yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
