using UnityEngine;

// A 2D grid of values with bilinear sampling — the container the vector-field system authors, samples, and paints.
// Trimmed to exactly what the system uses: a size, a flat values array, point get/set, and bilinear sampling.
// Self-contained, so the plugin carries no external grid/map dependency. The integer point type is Vector2Int.
//
// FieldMap<T> is the generic base the brush core operates on; VectorFieldMap (Vector2) and ColorFieldMap (Color) add
// the type-specific Lerp used by bilinear sampling. Not a MonoBehaviour and never Unity-serialized directly —
// components persist their own packed representation and rebuild the map at runtime.
public abstract class FieldMap<T> {
	// Grid dimensions in cells.
	public Vector2Int size;
	// Row-major cell values, length size.x * size.y. Public for direct bulk access (encode/decode, Array.Copy).
	public T[] values;

	protected FieldMap(Vector2Int size) {
		this.size = size;
		values = new T[size.x * size.y];
	}

	protected FieldMap(Vector2Int size, T[] values) {
		this.size = size;
		this.values = values;
	}

	// Copy constructor — deep-copies the values array so the clone is independent.
	protected FieldMap(FieldMap<T> other) {
		size = other.size;
		values = new T[other.values.Length];
		System.Array.Copy(other.values, values, other.values.Length);
	}

	int Index(int x, int y) => y * size.x + x;
	public bool IsOnGrid(int x, int y) => x >= 0 && x < size.x && y >= 0 && y < size.y;

	// Reset every cell to default(T).
	public void Clear() => values = new T[size.x * size.y];

	// Fill every cell with a value.
	public void Fill(T value) {
		for (int i = 0; i < values.Length; i++) values[i] = value;
	}

	public T GetValueAtGridPoint(int x, int y) => values[Index(x, y)];
	public T GetValueAtGridPoint(Vector2Int gridPoint) => GetValueAtGridPoint(gridPoint.x, gridPoint.y);

	// Direct write (caller guarantees in-bounds).
	public void SetValueAtGridPoint(int x, int y, T value) => values[Index(x, y)] = value;
	// Bounds-checked write; a no-op when the point is off the grid.
	public void SetValueAtGridPoint(Vector2Int gridPoint, T value) {
		if (IsOnGrid(gridPoint.x, gridPoint.y)) values[Index(gridPoint.x, gridPoint.y)] = value;
	}

	// Sample at a normalized position (0..1 across the grid).
	public T GetValueAtNormalizedPosition(Vector2 normalizedPosition) =>
		GetValueAtGridPosition(new Vector2(normalizedPosition.x * (size.x - 1), normalizedPosition.y * (size.y - 1)));

	// Bilinearly sample at a (fractional) grid position, clamped to the grid.
	public T GetValueAtGridPosition(Vector2 gridPosition) {
		float gx = Mathf.Clamp(gridPosition.x, 0, size.x - 1);
		float gy = Mathf.Clamp(gridPosition.y, 0, size.y - 1);

		int left = Mathf.FloorToInt(gx);
		int bottom = Mathf.FloorToInt(gy);
		int right = Mathf.Min(left + 1, size.x - 1);
		int top = Mathf.Min(bottom + 1, size.y - 1);

		float tx = gx - left;
		float ty = gy - bottom;

		T x1 = Lerp(GetValueAtGridPoint(left, bottom), GetValueAtGridPoint(right, bottom), tx);
		T x2 = Lerp(GetValueAtGridPoint(left, top), GetValueAtGridPoint(right, top), tx);
		return Lerp(x1, x2, ty);
	}

	// Type-specific linear interpolation, used by bilinear sampling.
	protected abstract T Lerp(T a, T b, float t);

	// A deep copy of this map, preserving the concrete subtype (so a sampled clone keeps the right Lerp).
	public abstract FieldMap<T> CloneMap();
}

// A field of 2D vectors (the vector field itself, and painted brush emitter maps).
public class VectorFieldMap : FieldMap<Vector2> {
	public VectorFieldMap(Vector2Int size) : base(size) { }
	public VectorFieldMap(Vector2Int size, Vector2[] values) : base(size, values) { }
	public VectorFieldMap(FieldMap<Vector2> other) : base(other) { }

	protected override Vector2 Lerp(Vector2 a, Vector2 b, float t) => Vector2.Lerp(a, b, t);
	public override FieldMap<Vector2> CloneMap() => new VectorFieldMap(this);

	// Clamp every vector's magnitude in place.
	public void ClampMagnitude(float maxMagnitude) {
		for (int i = 0; i < values.Length; i++) values[i] = Vector2.ClampMagnitude(values[i], maxMagnitude);
	}
}

// A field of colours (the smoke sim's painted emission source).
public class ColorFieldMap : FieldMap<Color> {
	public ColorFieldMap(Vector2Int size) : base(size) { }
	public ColorFieldMap(Vector2Int size, Color[] values) : base(size, values) { }
	public ColorFieldMap(FieldMap<Color> other) : base(other) { }

	protected override Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, t);
	public override FieldMap<Color> CloneMap() => new ColorFieldMap(this);
}
