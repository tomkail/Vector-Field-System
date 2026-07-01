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
    [System.NonSerialized] Vector2Map field;

    [SerializeField, HideInInspector] Point storedSize;
    [SerializeField, HideInInspector] Vector2[] storedValues;
    [SerializeField, HideInInspector] byte[] storedBytes;

    // The painted grid this asset holds (may be null until a component sizes/paints it).
    public Vector2Map Field { get => field; set => field = value; }

    // The field sized to `size`, (re)creating it if missing or a different size. Called by a component sourcing this
    // asset so the grid matches the component's GridRenderer.
    public Vector2Map GetField(Point size) {
        if (field == null || field.values == null || field.values.Length != size.x * size.y)
            field = new Vector2Map(size);
        return field;
    }

    public void OnBeforeSerialize() {
        if (field == null || field.values == null || field.values.Length == 0) {
            storedValues = null; storedBytes = null; return;
        }
        storedSize = field.size;
        if (VectorFieldStorage.format == VectorFieldStorage.Format.ByteArray) {
            storedBytes = VectorFieldStorage.Pack(field.values); storedValues = null;
        } else {
            storedValues = field.values; storedBytes = null;
        }
    }

    public void OnAfterDeserialize() {
        if (storedBytes != null && storedBytes.Length > 0)
            field = new Vector2Map(storedSize, VectorFieldStorage.Unpack(storedBytes, storedSize.x * storedSize.y));
        else if (storedValues != null && storedValues.Length > 0)
            field = new Vector2Map(storedSize, storedValues);
        else
            field = null;
    }
}
