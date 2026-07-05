using UnityEngine;

// A serializable spatial grid: owns a grid size and, given an owner Transform, maps between world space and grid
// (cell) space. Cell-center convention; the field always occupies a 1×1 quad in the owner's local space regardless of
// grid resolution (so resolution is a purely visual knob). Folded out of the old UnityX GridRenderer — it keeps only
// the slice the vector-field system actually used (Manhattan mode, scaleWithGridSize = false, cell-center conversion),
// as a plain serializable class rather than a required MonoBehaviour.
//
// This is a generic spatial grid, not vector-field-specific (the smoke sim uses it too) — hence GridTransform, not
// VectorFieldGrid. Not to be confused with the data-map base class (a 2D array with bilinear sampling), which is a
// different concept entirely.
[System.Serializable]
public class GridTransform {
	[SerializeField] Vector2Int _size = new Vector2Int(64, 64);

	// Grid resolution in cells. Clamped to a minimum of 1 on each axis (a zero-dimension grid can't allocate a texture).
	public Vector2Int Size {
		get => _size;
		set {
			var clamped = new Vector2Int(Mathf.Max(1, value.x), Mathf.Max(1, value.y));
			if (_size == clamped) return;
			_size = clamped;
			_dirty = true;
		}
	}

	// The transform that places the grid in the world. Not serialized — re-bound each init via Bind().
	Transform _owner;

	// Cached matrices. Recomputed when the size changes, when Bind() swaps the owner, or when the owner's
	// localToWorldMatrix changes (detected by comparing against the value the cache was built from, so this doesn't
	// consume the shared Transform.hasChanged flag that other code may rely on).
	bool _dirty = true;
	Matrix4x4 _cachedOwnerMatrix = Matrix4x4.identity;
	Matrix4x4 _gridToLocal = Matrix4x4.identity;
	Matrix4x4 _gridToWorld = Matrix4x4.identity;
	Matrix4x4 _worldToGrid = Matrix4x4.identity;

	public GridTransform() { }
	public GridTransform(Vector2Int size) { _size = new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)); }

	// Bind (or re-bind) to the owner transform. Cheap and idempotent — call from the owner's init.
	public void Bind(Transform owner) {
		if (_owner == owner) return;
		_owner = owner;
		_dirty = true;
	}

	Matrix4x4 OwnerLocalToWorld => _owner != null ? _owner.localToWorldMatrix : Matrix4x4.identity;

	void EnsureUpToDate() {
		var ownerMatrix = OwnerLocalToWorld;
		if (!_dirty && ownerMatrix == _cachedOwnerMatrix) return;
		_cachedOwnerMatrix = ownerMatrix;
		_dirty = false;

		// Cell-center conversion, Manhattan mode, scaleWithGridSize = false (exactly the old GridRenderer path):
		//   cellSize    = (1/sx, 1/sy, 1/sy)
		//   gridToLocal = TRS(0, id, cellSize) · TRS((-sx/2, -sy/2, 0), id, 1) · TRS((0.5, 0.5, 0), id, 1)
		Vector3 cellSize = new Vector3(1f / _size.x, 1f / _size.y, 1f / _size.y);
		Matrix4x4 m = Matrix4x4.Scale(cellSize);
		m *= Matrix4x4.Translate(new Vector3(-_size.x * 0.5f, -_size.y * 0.5f, 0f));
		m *= Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f));
		_gridToLocal = m;
		_gridToWorld = ownerMatrix * _gridToLocal;
		_worldToGrid = _gridToWorld.inverse;
	}

	public Matrix4x4 GridToLocalMatrix { get { EnsureUpToDate(); return _gridToLocal; } }
	public Matrix4x4 GridToWorldMatrix { get { EnsureUpToDate(); return _gridToWorld; } }

	// World position → grid (cell) position (fractional; bilinear samplers floor it).
	public Vector2 WorldToGridPosition(Vector3 worldPosition) { EnsureUpToDate(); return _worldToGrid.MultiplyPoint3x4(worldPosition); }

	// World direction/vector → grid-space vector (no translation).
	public Vector3 WorldToGridVector(Vector3 worldVector) { EnsureUpToDate(); return _worldToGrid.MultiplyVector(worldVector); }

	// Grid (cell) position → world position.
	public Vector3 GridToWorldPosition(Vector2 gridPosition) { EnsureUpToDate(); return _gridToWorld.MultiplyPoint3x4(gridPosition); }

	// Grid-space vector → world direction/vector (no translation).
	public Vector3 GridToWorldVector(Vector2 gridVector) { EnsureUpToDate(); return _gridToWorld.MultiplyVector(gridVector); }

	// The plane the grid lies in, in world space (normal = -owner.forward, origin = owner.position). Matches the old
	// GridRenderer.floorPlane, used for screen-ray hit-testing while painting.
	public Plane FloorPlane => _owner != null ? new Plane(-_owner.forward, _owner.position) : new Plane(Vector3.back, Vector3.zero);

	// World-space bounds of the field's 1×1 local quad. Independent of grid resolution. Replaces the old
	// edge.NormalizedToWorldRect(0,0,1,1) + BoundsX.CreateEncapsulating path.
	public Bounds GetWorldBounds() {
		var ltw = OwnerLocalToWorld;
		var bounds = new Bounds(ltw.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, 0f)), Vector3.zero);
		bounds.Encapsulate(ltw.MultiplyPoint3x4(new Vector3(0.5f, -0.5f, 0f)));
		bounds.Encapsulate(ltw.MultiplyPoint3x4(new Vector3(0.5f, 0.5f, 0f)));
		bounds.Encapsulate(ltw.MultiplyPoint3x4(new Vector3(-0.5f, 0.5f, 0f)));
		return bounds;
	}

	// The 4 world-space corners of the field quad (CCW from bottom-left), for drawing the outline. Fills `corners`
	// (allocated to length 4 if needed) rather than allocating each call.
	public void GetWorldCorners(ref Vector3[] corners) {
		if (corners == null || corners.Length != 4) corners = new Vector3[4];
		var ltw = OwnerLocalToWorld;
		corners[0] = ltw.MultiplyPoint3x4(new Vector3(-0.5f, -0.5f, 0f));
		corners[1] = ltw.MultiplyPoint3x4(new Vector3(0.5f, -0.5f, 0f));
		corners[2] = ltw.MultiplyPoint3x4(new Vector3(0.5f, 0.5f, 0f));
		corners[3] = ltw.MultiplyPoint3x4(new Vector3(-0.5f, 0.5f, 0f));
	}
}
