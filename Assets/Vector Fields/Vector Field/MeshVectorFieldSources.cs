using System;
using System.Collections.Generic;
using UnityEngine;

// A pluggable contributor of 2D boundary geometry to a MeshVectorField. Every source appends its geometry as endpoint
// pairs (a0, b0, a1, b1, ...) expressed in the field's *local plane* space (XY; z dropped). The field aggregates every
// source into one segment buffer, so 3D cross-sections, 2D silhouettes, and any number of objects all flow through the
// same GPU kernel. This is the code-usable seam: MeshVectorField exposes typed inspector lists for the common cases and
// wraps them in the built-in sources below, but any runtime code can register its own IVectorFieldSegmentSource.
public interface IVectorFieldSegmentSource {
	bool IsValid { get; }

	// True when this source contributes closed loops (so the field's even/odd inside test is meaningful). Open sources
	// (e.g. EdgeCollider2D) return false.
	bool IsClosed { get; }

	// Append endpoint pairs in field-local plane space. worldToFieldLocal maps world -> the field's local space; a source
	// composes it with its own localToWorld.
	void AppendSegments(List<Vector2> segments, Matrix4x4 worldToFieldLocal);

	// Contribute identity/transform to the field's change hash so edits re-slice. (Live vertex/point edits that don't
	// move the transform aren't captured — use MeshVectorField.continuousUpdate for animated content.)
	void AppendHash(ref HashCode hash);
}

// Stateless extraction helpers shared by the built-in sources and MeshVectorField's inspector lists. Each Append* writes
// endpoint pairs into `segments`, already transformed into field-local plane space by the supplied matrix. Scratch lists
// are passed in so callers can reuse them across many objects and allocate nothing per frame.
public static class MeshVectorFieldExtractors {

	// --- 3D mesh cross-section -----------------------------------------------------------------------------------------
	// Slices `mesh` with the field plane (z = 0 in field-local): each triangle straddling the plane contributes exactly
	// one segment. `meshToFieldLocal` maps mesh-local vertices straight into field-local space. Watertight meshes yield
	// closed loops, so the field's inside test holds; open meshes just contribute an unsigned distance boundary.
	public static void AppendMeshCrossSection(List<Vector2> segments, Mesh mesh, Matrix4x4 meshToFieldLocal,
		List<Vector3> vertexScratch, List<int> triangleScratch) {
		if (mesh == null) return;
		if (!mesh.isReadable) {
			Debug.LogWarning($"MeshVectorField: mesh '{mesh.name}' is not readable (enable Read/Write in its import settings) — skipped.");
			return;
		}

		vertexScratch.Clear();
		mesh.GetVertices(vertexScratch);
		// Fold vertices into field-local once; the plane is z = 0 there, so a vertex's z is its signed distance to it.
		for (int i = 0; i < vertexScratch.Count; i++)
			vertexScratch[i] = meshToFieldLocal.MultiplyPoint3x4(vertexScratch[i]);

		for (int s = 0; s < mesh.subMeshCount; s++) {
			triangleScratch.Clear();
			mesh.GetTriangles(triangleScratch, s);
			for (int i = 0; i + 2 < triangleScratch.Count; i += 3)
				AppendTrianglePlaneCrossing(segments,
					vertexScratch[triangleScratch[i]], vertexScratch[triangleScratch[i + 1]], vertexScratch[triangleScratch[i + 2]]);
		}
	}

	// --- mesh watertightness (drives the inside test automatically, so there's no user toggle) ------------------------
	// A *closed* (no-boundary-edge) mesh's planar cross-section is always closed loops, so the even/odd inside test is
	// valid; an open surface (quad, plane, foliage card, mesh with holes) slices to open arcs where parity is garbage.
	// We detect this by welding vertices by position (imported meshes duplicate verts at UV/normal seams, so an
	// index-only test would wrongly call a solid mesh "open") and checking for any edge used by fewer than two triangles.
	// Cached per mesh: the scan is O(triangles) but runs once per unique mesh, never per frame. A baked skinned mesh
	// keeps the same reference and topology across frames, so its result caches correctly too.
	static readonly Dictionary<Mesh, bool> meshClosedCache = new();
	static readonly List<Vector3> weldVertexScratch = new();
	static readonly List<int> weldTriangleScratch = new();
	static readonly Dictionary<Vector3Int, int> weldMapScratch = new();
	static readonly Dictionary<long, int> edgeCountScratch = new();

	public static bool IsMeshClosed(Mesh mesh) {
		if (mesh == null) return false;
		if (meshClosedCache.TryGetValue(mesh, out bool closed)) return closed;
		closed = ComputeMeshClosed(mesh);
		meshClosedCache[mesh] = closed;
		return closed;
	}

	static bool ComputeMeshClosed(Mesh mesh) {
		if (!mesh.isReadable) return true; // can't inspect it — assume closed (the cross-section is skipped anyway)

		weldVertexScratch.Clear();
		mesh.GetVertices(weldVertexScratch);
		int vertexCount = weldVertexScratch.Count;
		if (vertexCount == 0) return true;

		// Weld positions onto a coarse lattice so seam-duplicated verts collapse to one index.
		const float eps = 1e-4f;
		weldMapScratch.Clear();
		var remap = new int[vertexCount];
		for (int i = 0; i < vertexCount; i++) {
			var p = weldVertexScratch[i];
			var key = new Vector3Int(Mathf.RoundToInt(p.x / eps), Mathf.RoundToInt(p.y / eps), Mathf.RoundToInt(p.z / eps));
			if (!weldMapScratch.TryGetValue(key, out int idx)) { idx = weldMapScratch.Count; weldMapScratch[key] = idx; }
			remap[i] = idx;
		}

		edgeCountScratch.Clear();
		for (int s = 0; s < mesh.subMeshCount; s++) {
			weldTriangleScratch.Clear();
			mesh.GetTriangles(weldTriangleScratch, s);
			for (int i = 0; i + 2 < weldTriangleScratch.Count; i += 3) {
				int a = remap[weldTriangleScratch[i]], b = remap[weldTriangleScratch[i + 1]], c = remap[weldTriangleScratch[i + 2]];
				CountEdge(a, b);
				CountEdge(b, c);
				CountEdge(c, a);
			}
		}

		// Any edge touched by only one triangle is a boundary edge → the surface is open.
		foreach (var count in edgeCountScratch.Values)
			if (count < 2) return false;
		return true;
	}

	static void CountEdge(int u, int v) {
		if (u > v) (u, v) = (v, u);
		long key = ((long)u << 32) | (uint)v;
		edgeCountScratch.TryGetValue(key, out int count);
		edgeCountScratch[key] = count + 1;
	}

	// A triangle crosses the z = 0 plane on exactly two of its edges when it straddles it; those two crossings are the
	// segment. Sign convention: z >= 0 is "positive", so a vertex exactly on the plane counts as one side consistently.
	static void AppendTrianglePlaneCrossing(List<Vector2> segments, Vector3 a, Vector3 b, Vector3 c) {
		Vector2 h0 = default, h1 = default;
		int n = 0;
		if (EdgePlaneCrossing(a, b, out var hit)) { h0 = hit; n++; }
		if (EdgePlaneCrossing(b, c, out hit)) { if (n == 0) h0 = hit; else h1 = hit; n++; }
		if (EdgePlaneCrossing(c, a, out hit)) { if (n == 0) h0 = hit; else h1 = hit; n++; }
		if (n == 2) { segments.Add(h0); segments.Add(h1); }
	}

	static bool EdgePlaneCrossing(Vector3 a, Vector3 b, out Vector2 hit) {
		bool aNeg = a.z < 0f;
		bool bNeg = b.z < 0f;
		if (aNeg == bNeg) { hit = default; return false; }
		float t = a.z / (a.z - b.z);
		hit = new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
		return true;
	}

	// --- 2D collider silhouette ---------------------------------------------------------------------------------------
	// The collider's outline, projected onto the field plane. The common closed shapes give the field a real inside;
	// EdgeCollider2D is an open polyline (no inside). `pointScratch` is reused for the path queries.
	public static void AppendCollider2DSilhouette(List<Vector2> segments, Collider2D collider,
		Matrix4x4 worldToFieldLocal, List<Vector2> pointScratch) {
		if (collider == null) return;
		Matrix4x4 m = worldToFieldLocal * collider.transform.localToWorldMatrix;
		Vector2 offset = collider.offset;

		switch (collider) {
			case PolygonCollider2D poly:
				for (int i = 0; i < poly.pathCount; i++) {
					pointScratch.Clear();
					poly.GetPath(i, pointScratch);
					AppendPath(segments, pointScratch, offset, m, closed: true);
				}
				break;
			case CompositeCollider2D composite:
				for (int i = 0; i < composite.pathCount; i++) {
					pointScratch.Clear();
					composite.GetPath(i, pointScratch);
					AppendPath(segments, pointScratch, offset, m, closed: true);
				}
				break;
			case EdgeCollider2D edge:
				pointScratch.Clear();
				edge.GetPoints(pointScratch);
				AppendPath(segments, pointScratch, offset, m, closed: false);
				break;
			case BoxCollider2D box: {
				Vector2 h = box.size * 0.5f;
				pointScratch.Clear();
				pointScratch.Add(new Vector2(-h.x, -h.y));
				pointScratch.Add(new Vector2(h.x, -h.y));
				pointScratch.Add(new Vector2(h.x, h.y));
				pointScratch.Add(new Vector2(-h.x, h.y));
				AppendPath(segments, pointScratch, offset, m, closed: true);
				break;
			}
			case CircleCollider2D circle:
				AppendCircle(segments, offset, circle.radius, m);
				break;
			case CapsuleCollider2D capsule: {
				// Approximated as its bounding box in the collider's local space — good enough for a field boundary.
				Vector2 h = capsule.size * 0.5f;
				pointScratch.Clear();
				pointScratch.Add(new Vector2(-h.x, -h.y));
				pointScratch.Add(new Vector2(h.x, -h.y));
				pointScratch.Add(new Vector2(h.x, h.y));
				pointScratch.Add(new Vector2(-h.x, h.y));
				AppendPath(segments, pointScratch, offset, m, closed: true);
				break;
			}
			default:
				Debug.LogWarning($"MeshVectorField: collider type {collider.GetType().Name} is not supported as a silhouette source.", collider);
				break;
		}
	}

	// --- 2D sprite silhouette -----------------------------------------------------------------------------------------
	// Uses the sprite's physics shape outline (holes come back as separate paths, which even/odd handles), falling back
	// to the sprite's rect when no physics shape is defined. flipX/flipY are applied as a sign flip about the sprite's
	// local origin (exact when the pivot is centred).
	public static void AppendSprite(List<Vector2> segments, SpriteRenderer spriteRenderer,
		Matrix4x4 worldToFieldLocal, List<Vector2> pointScratch) {
		if (spriteRenderer == null || spriteRenderer.sprite == null) return;
		var sprite = spriteRenderer.sprite;
		Vector2 flip = new Vector2(spriteRenderer.flipX ? -1f : 1f, spriteRenderer.flipY ? -1f : 1f);
		Matrix4x4 m = worldToFieldLocal * spriteRenderer.transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(flip.x, flip.y, 1f));

		int shapeCount = sprite.GetPhysicsShapeCount();
		if (shapeCount > 0) {
			for (int i = 0; i < shapeCount; i++) {
				pointScratch.Clear();
				sprite.GetPhysicsShape(i, pointScratch);
				AppendPath(segments, pointScratch, Vector2.zero, m, closed: true);
			}
		} else {
			// No physics shape: outline the sprite's local-space bounds rect.
			var b = sprite.bounds;
			pointScratch.Clear();
			pointScratch.Add(new Vector2(b.min.x, b.min.y));
			pointScratch.Add(new Vector2(b.max.x, b.min.y));
			pointScratch.Add(new Vector2(b.max.x, b.max.y));
			pointScratch.Add(new Vector2(b.min.x, b.max.y));
			AppendPath(segments, pointScratch, Vector2.zero, m, closed: true);
		}
	}

	// --- shared path helpers ------------------------------------------------------------------------------------------
	static void AppendPath(List<Vector2> segments, List<Vector2> points, Vector2 offset, Matrix4x4 m, bool closed) {
		int count = points.Count;
		if (count < 2) return;
		Vector2 prev = ToPlane(m, points[0] + offset);
		Vector2 first = prev;
		for (int i = 1; i < count; i++) {
			Vector2 cur = ToPlane(m, points[i] + offset);
			segments.Add(prev);
			segments.Add(cur);
			prev = cur;
		}
		if (closed) { segments.Add(prev); segments.Add(first); }
	}

	static void AppendCircle(List<Vector2> segments, Vector2 center, float radius, Matrix4x4 m, int steps = 32) {
		if (radius <= 0f || steps < 3) return;
		Vector2 prev = ToPlane(m, center + new Vector2(radius, 0f));
		Vector2 first = prev;
		for (int i = 1; i <= steps; i++) {
			float ang = (i / (float)steps) * Mathf.PI * 2f;
			Vector2 pt = i == steps ? default : center + new Vector2(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius);
			Vector2 cur = i == steps ? first : ToPlane(m, pt);
			segments.Add(prev);
			segments.Add(cur);
			prev = cur;
		}
	}

	static Vector2 ToPlane(Matrix4x4 m, Vector2 p) {
		Vector3 w = m.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
		return new Vector2(w.x, w.y);
	}
}

// --- Built-in IVectorFieldSegmentSource implementations ---------------------------------------------------------------
// Thin wrappers over the extractors, for runtime code that wants to register sources directly (e.g. procedurally spawned
// meshes). MeshVectorField's inspector lists call the extractors straight, so these allocate nothing on the hot path
// unless you use them.

public class MeshFilterCrossSectionSource : IVectorFieldSegmentSource {
	public MeshFilter meshFilter;
	readonly List<Vector3> vertexScratch = new();
	readonly List<int> triangleScratch = new();

	public MeshFilterCrossSectionSource(MeshFilter meshFilter) { this.meshFilter = meshFilter; }

	public bool IsValid => meshFilter != null && meshFilter.sharedMesh != null;
	public bool IsClosed => IsValid && MeshVectorFieldExtractors.IsMeshClosed(meshFilter.sharedMesh);

	public void AppendSegments(List<Vector2> segments, Matrix4x4 worldToFieldLocal) {
		if (!IsValid) return;
		var m = worldToFieldLocal * meshFilter.transform.localToWorldMatrix;
		MeshVectorFieldExtractors.AppendMeshCrossSection(segments, meshFilter.sharedMesh, m, vertexScratch, triangleScratch);
	}

	public void AppendHash(ref HashCode hash) {
		hash.Add(meshFilter != null ? meshFilter.transform.localToWorldMatrix : Matrix4x4.identity);
		hash.Add(meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.GetEntityId().GetHashCode() : 0);
		hash.Add(meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.vertexCount : 0);
	}
}

public class Collider2DSilhouetteSource : IVectorFieldSegmentSource {
	public Collider2D collider;
	readonly List<Vector2> pointScratch = new();

	public Collider2DSilhouetteSource(Collider2D collider) { this.collider = collider; }

	public bool IsValid => collider != null;
	public bool IsClosed => collider is not EdgeCollider2D;

	public void AppendSegments(List<Vector2> segments, Matrix4x4 worldToFieldLocal) {
		if (!IsValid) return;
		MeshVectorFieldExtractors.AppendCollider2DSilhouette(segments, collider, worldToFieldLocal, pointScratch);
	}

	public void AppendHash(ref HashCode hash) {
		hash.Add(collider != null ? collider.transform.localToWorldMatrix : Matrix4x4.identity);
		hash.Add(collider != null ? collider.GetEntityId().GetHashCode() : 0);
		hash.Add(collider != null ? collider.offset : Vector2.zero);
	}
}
