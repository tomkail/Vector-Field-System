using UnityEngine;

[AddComponentMenu("Vector Fields/Drawable Vector Field")]
public class DrawableVectorFieldComponent : VectorFieldComponent, ISerializationCallbackReceiver, IPaintTarget<Vector2> {
    // IPaintTarget<Vector2>: the generic painting core (PaintStroke<Vector2>, the brush kernel) drives this component.
    // grid and MarkRegionDirty already satisfy the interface; PaintField/CreateMap need explicit impls because
    // the interface is typed on the base FieldMap<Vector2> while our members are the VectorFieldMap subtype.
    FieldMap<Vector2> IPaintTarget<Vector2>.PaintField => PaintField;
    FieldMap<Vector2> IPaintTarget<Vector2>.CreateMap(Vector2Int size) => new VectorFieldMap(size);

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
    [System.NonSerialized] VectorFieldMap paintField;

    // Resampling support. When the grid resizes (a manual Grid Size edit, or Auto-resolution following the transform
    // scale), we bilinearly resample the painted field into the new resolution instead of discarding it. `baseline` is
    // the authored map captured at the START of a resize sequence; every resize in the sequence resamples from it (not
    // from the previous, already-resampled result) so a continuous scale drag stays single-generation — no cumulative
    // blur. `baselineStale` (set on any paint/clear/load/undo) forces the baseline to refresh on the next resize.
    [System.NonSerialized] VectorFieldMap resampleBaseline;
    [System.NonSerialized] bool resampleBaselineStale;

    // Serialized backing. The data always lives on the component (never an asset), stored in exactly ONE of these per
    // VectorFieldStorage.format: `storedValues` (verbose Vector2 array) or `storedRows` (compact base64 per row).
    // Reading detects which is populated, so switching the project setting doesn't break existing scenes.
    [SerializeField, HideInInspector] Vector2Int storedSize;
    [SerializeField, HideInInspector] Vector2[] storedValues;   // Vector2Array format
    [SerializeField, HideInInspector] string[] storedRows;      // ByteArray format: one base64 row per line (local diffs)

    // The map currently backing this field: the linked asset's when `sourceAsset` is set (data in the asset, shared),
    // otherwise the component's own `paintField` (data in the scene). All paint/read/render go through this, so the
    // two modes are otherwise identical.
    VectorFieldMap ActiveMap {
        get => sourceAsset != null ? sourceAsset.Field : paintField;
        set { if (sourceAsset != null) sourceAsset.Field = value; else paintField = value; }
    }

    // Ensure the active map exists at the current grid size; returns true if it had to be (re)created (a resize),
    // which RenderInternal uses to choose between a region and a full GPU upload. A deserialized VectorFieldMap can come
    // back non-null with a null `values` array, so IsValid treats that as "needs (re)building" too.
    //
    // On a resize with existing painted data we RESAMPLE (bilinear) rather than clear, so changing resolution — whether
    // a manual Grid Size edit or Auto-resolution tracking the transform scale — never discards the painting. See the
    // resampleBaseline field for how a continuous scale drag avoids cumulative blur.
    bool EnsurePaintField() {
        var size = new Vector2Int(grid.Size.x, grid.Size.y);
        if (IsValid(ActiveMap, size)) return false;

        var current = ActiveMap;
        bool hasData = current != null && current.values != null && current.values.Length > 0
            && current.size.x > 0 && current.size.y > 0;
        if (hasData) {
            if (resampleBaseline == null || resampleBaselineStale) {
                resampleBaseline = new VectorFieldMap(current);   // capture the authored map for this resize sequence
                resampleBaselineStale = false;
            }
            ActiveMap = ResampleMap(resampleBaseline, size);
        } else {
            ActiveMap = new VectorFieldMap(size);
            resampleBaseline = null;
            resampleBaselineStale = false;
        }
        return true;
    }

    // Bilinearly resample `src` into a `newSize` map (normalized 0..1 sampling), preserving the painted flow across a
    // resolution change. Directions are resampled the same way the GPU texture would be when the render target resizes.
    static VectorFieldMap ResampleMap(VectorFieldMap src, Vector2Int newSize) {
        var dst = new VectorFieldMap(newSize);
        for (int y = 0; y < newSize.y; y++) {
            float ny = newSize.y > 1 ? (float)y / (newSize.y - 1) : 0f;
            for (int x = 0; x < newSize.x; x++) {
                float nx = newSize.x > 1 ? (float)x / (newSize.x - 1) : 0f;
                dst.SetValueAtGridPoint(x, y, src.GetValueAtNormalizedPosition(new Vector2(nx, ny)));
            }
        }
        return dst;
    }

    // The painted field, created/resized to the current grid on demand. The drawing tool reads and writes this.
    public VectorFieldMap PaintField {
        get { EnsurePaintField(); return ActiveMap; }
    }

    // Write the working field into the chosen backing (clearing the other), so the scene stores it in the project's
    // format. Reads only a plain static (VectorFieldStorage.format) — safe from a serialization callback. Runs in the
    // editor at save time; players never call this.
    public void OnBeforeSerialize() {
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

    // Rebuild the working field from whichever backing is populated. Pure array work, no Unity API, so it's safe on
    // the deserialization thread. The grid-size reconciliation stays in PaintField's getter.
    public void OnAfterDeserialize() {
        if (storedRows != null && storedRows.Length > 0) {
            paintField = new VectorFieldMap(storedSize, VectorFieldStorage.UnpackRows(storedRows, storedSize));
        } else if (storedValues != null && storedValues.Length > 0) {
            paintField = new VectorFieldMap(storedSize, storedValues);
        } else {
            paintField = null;   // built lazily by PaintField
        }
        // Restore the grid size from the serialized paint-field size (component mode), so a resized field survives a
        // reload — and so EnsurePaintField never shrinks the painted data to a stale default. No-op when there's no
        // stored field; asset mode syncs when the asset's field loads.
        if (paintField != null) grid.Size = new Vector2Int(storedSize.x, storedSize.y);
    }

    // A paint field is usable when it exists, has a backing array, and that array matches the requested grid size.
    static bool IsValid(VectorFieldMap field, Vector2Int size) =>
        field != null && field.values != null && field.values.Length == size.x * size.y;

#if UNITY_EDITOR
    // --- Undo/redo support -----------------------------------------------------------------------------------------
    // The painted data lives in a [NonSerialized] VectorFieldMap (this component's paintField, or the linked asset's), so
    // only the serialized backing (storedValues/storedRows) is visible to Unity's Undo. Naive undo fails because:
    // (1) Undo snapshots the backing but never invokes OnBeforeSerialize, so at RegisterCompleteObjectUndo time the
    //     backing is stale;
    // (2) OnAfterDeserialize can alias the map's array to the backing (FieldMap.Fill references, doesn't copy), so
    //     in-place painting mutates the very array Undo recorded; and
    // (3) the data may live in the linked VectorFieldAsset, not this component — registering the component captures
    //     nothing.
    // So RegisterPaintUndo() flushes an INDEPENDENT copy into the right object's backing and registers THAT object;
    // on undo/redo we rebuild the live map from the restored backing and re-render.
    protected override void OnEnable() {
        base.OnEnable();
        UnityEditor.Undo.undoRedoPerformed += OnUndoRedoPerformed;
    }

    protected override void OnDisable() {
        UnityEditor.Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        base.OnDisable();
    }

    void OnUndoRedoPerformed() {
        if (this == null) return;
        // Undo restored the serialized backing but didn't re-run the deserialize callback; rebuild the live map from it.
        if (sourceAsset != null) ((ISerializationCallbackReceiver)sourceAsset).OnAfterDeserialize();
        else ((ISerializationCallbackReceiver)this).OnAfterDeserialize();
        resampleBaseline = null;   // the authored map was replaced wholesale; don't resample from a stale baseline
        resampleBaselineStale = false;
        EnsurePaintField();   // guard: never leave a null/mismatched field
        SetDirty();           // re-render the GPU texture from it
    }

    // Register one undo step for a paint edit, targeting whichever object actually holds the data (the linked asset in
    // asset mode, else this component), after flushing the live field into that object's backing as an independent copy.
    public void RegisterPaintUndo(string label) {
        SnapshotForUndo();
        UnityEditor.Undo.RegisterCompleteObjectUndo(sourceAsset != null ? (UnityEngine.Object)sourceAsset : this, label);
    }

    // Flush the live field into the correct serialized backing as an INDEPENDENT copy (so Undo records a true snapshot,
    // not an alias the next paint overwrites). Call before RegisterPaintUndo and when a stroke ends (so redo captures
    // the post-state). Routes to the asset in asset mode.
    public void SnapshotForUndo() {
        if (sourceAsset != null) { sourceAsset.SnapshotForUndo(); return; }
        if (paintField == null || paintField.values == null || paintField.values.Length == 0) return;
        storedSize = paintField.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedRows = VectorFieldStorage.PackRows(paintField.values, paintField.size);   // PackRows already copies
            storedValues = null;
        } else {
            storedValues = (Vector2[])paintField.values.Clone();                            // independent copy
            storedRows = null;
        }
    }
#endif

    // Uploads to the GPU come from the painted field, not from base.vectorField (which is the readback target).
    protected override VectorFieldMap UploadSource => PaintField;

    // The drawing tool paints into paintField directly, then reports the touched grid rect via MarkRegionDirty so we
    // can upload just that sub-rect to renderTexture. Accumulates the union of regions painted since the last render
    // (the dirty pump coalesces several paint steps into one render); null means "re-upload the whole field".
    RectInt? pendingDirtyRegion;

    public void MarkRegionDirty(RectInt gridRegion) {
        pendingDirtyRegion = pendingDirtyRegion.HasValue ? Union(pendingDirtyRegion.Value, gridRegion) : gridRegion;
        resampleBaselineStale = true;   // painting changed the authored map; the next resize must re-baseline from it
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
        // The base applies the output transform (magnitude + cookie) per-region right after this, so an active cookie
        // doesn't force a full upload every render — a region upload re-transforms only its own texels. But a
        // *change* to the transform (magnitude or cookie) must re-derive the WHOLE texture from the raw paint field,
        // since a region upload would leave the rest baked with the old transform; OutputTransformChangedSinceLastRender
        // catches that (call it exactly once per render).
        bool transformChanged = OutputTransformChangedSinceLastRender();
        bool useRegion = pendingDirtyRegion.HasValue && !resized && !transformChanged;
        if (useRegion)
            WriteVectorFieldRegionToRenderTexture(pendingDirtyRegion.Value);
        else
            WriteVectorFieldToRenderTexture();
        pendingDirtyRegion = null;
    }

    public void Clear() {
#if UNITY_EDITOR
        // Record the pre-clear field (into the right object's backing) so the clear is undoable.
        RegisterPaintUndo("Clear Vector Field");
#endif
        PaintField.Clear();
        resampleBaselineStale = true;
#if UNITY_EDITOR
        // Flush the cleared field into the backing so the change persists and redo captures it.
        SnapshotForUndo();
#endif
        SetDirty();
        MarkSourceAssetDirty();
    }

#if UNITY_EDITOR
    // Move the current field into a new reusable asset and link this component to it (switch to asset mode). The data
    // now lives in the .asset; the component stores only the reference.
    public void ExtractToAsset() {
        var current = new VectorFieldMap(PaintField);   // copy so the asset owns its data
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
    public void BakeIntoComponent() {
        if (sourceAsset == null || sourceAsset.Field == null) return;
        // Record the pre-bake state (asset mode) so the bake is undoable. On undo, sourceAsset is restored and
        // OnUndoRedoPerformed rebuilds the live map from the asset again; on redo, from the baked backing below.
        UnityEditor.Undo.RegisterCompleteObjectUndo(this, "Bake Vector Field Into Component");
        paintField = new VectorFieldMap(sourceAsset.Field);   // copy asset data onto the component
        sourceAsset = null;                               // unlink; data now lives in the scene
        SnapshotForUndo();   // flush the baked data into the serialized backing so it persists and redo captures it
        UnityEditor.EditorUtility.SetDirty(this);
        SetDirty();
    }
#endif

    // Replaces the painted field with a copy of `source`, resizing the grid to match. Code-callable entry point used
    // by the editor's Rasterize (baking any field type into an editable Drawable) and usable from script to seed a
    // drawable field. Writes into paintField — the authored source of truth — not base.vectorField (the readback
    // target), so the painting actually shows up and serializes.
    public void LoadPaintField(VectorFieldMap source) {
        if (source == null) return;
        grid.Size = new Vector2Int(source.size.x, source.size.y);
        ActiveMap = new VectorFieldMap(source);   // writes to the asset in asset mode, else the component
        resampleBaselineStale = true;             // new authored data; re-baseline on the next resize
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
