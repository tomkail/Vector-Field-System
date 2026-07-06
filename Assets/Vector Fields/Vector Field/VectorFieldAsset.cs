using UnityEngine;

// An optional, shareable container for a painted vector grid, so a DrawableVectorFieldComponent can source its field
// from a reusable asset instead of storing it in the scene. Purely opt-in: a component with no asset keeps its data on
// itself (the default — no orphans). The asset holds only the grid data; the owning component provides the world
// placement (gridRenderer), so an asset can drive several components — ideally at the same grid size.
//
// Serializes with the same project storage format as in-scene fields (see VectorFieldStorage): Vector2Array (readable)
// or ByteArray (compact). The data lives in this .asset file, isolated from scenes.
[CreateAssetMenu(fileName = "Vector Field", menuName = "Vector Field/Vector Field Asset")]
public class VectorFieldAsset : ScriptableObject, ISerializationCallbackReceiver {
    [System.NonSerialized] VectorFieldMap field;

    [SerializeField, HideInInspector] Vector2Int storedSize;
    [SerializeField, HideInInspector] Vector2[] storedValues;   // Vector2Array format
    [SerializeField, HideInInspector] string[] storedRows;      // ByteArray format: one base64 row per line (local diffs)

    // The painted grid this asset holds (may be null until a component sizes/paints it).
    public VectorFieldMap Field { get => field; set => field = value; }

    // The field sized to `size`, (re)creating it if missing or a different size. Called by a component sourcing this
    // asset so the grid matches the component's grid.
    public VectorFieldMap GetField(Vector2Int size) {
        if (field == null || field.values == null || field.values.Length != size.x * size.y)
            field = new VectorFieldMap(size);
        return field;
    }

    public void OnBeforeSerialize() {
        if (field == null || field.values == null || field.values.Length == 0) {
            storedValues = null; storedRows = null; return;
        }
        storedSize = field.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedRows = VectorFieldStorage.PackRows(field.values, field.size); storedValues = null;
        } else {
            storedValues = field.values; storedRows = null;
        }
    }

    public void OnAfterDeserialize() {
        if (storedRows != null && storedRows.Length > 0)
            field = new VectorFieldMap(storedSize, VectorFieldStorage.UnpackRows(storedRows, storedSize));
        else if (storedValues != null && storedValues.Length > 0)
            field = new VectorFieldMap(storedSize, storedValues);
        else
            field = null;
    }

#if UNITY_EDITOR
    // Flush the live field into an INDEPENDENT serialized backing so Undo.RegisterCompleteObjectUndo records a true
    // snapshot (Unity's Undo doesn't call OnBeforeSerialize, and the field is [NonSerialized]). The owning
    // DrawableVectorFieldComponent registers this asset for undo and rebuilds it on undo/redo.
    public void SnapshotForUndo() {
        if (field == null || field.values == null || field.values.Length == 0) return;
        storedSize = field.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedRows = VectorFieldStorage.PackRows(field.values, field.size);   // PackRows already copies
            storedValues = null;
        } else {
            storedValues = (Vector2[])field.values.Clone();                       // independent copy
            storedRows = null;
        }
    }
#endif
}
