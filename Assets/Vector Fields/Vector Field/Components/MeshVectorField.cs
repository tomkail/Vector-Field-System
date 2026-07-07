using System;
using System.Collections.Generic;
using UnityEngine;

// Editor-facing wrapper around the code-callable MeshVectorFieldGenerator: every cell points toward (or away from) the
// nearest boundary contributed by its sources, restricted to the chosen side(s) and shaped by a distance falloff.
// Aggregates any number of boundary sources into one segment soup:
//   - 3D meshes contribute their *cross-section* where they intersect the grid plane.
//   - 2D sprites / colliders contribute their *silhouette* outline.
// Both reduce to 2D segments in this field's local plane, so they share MeshVectorField.compute. Assign any mix of
// sources below (or register IVectorFieldSegmentSource at runtime via AddSource); they all combine into one field.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Mesh Vector Field")]
public class MeshVectorField : VectorFieldComponent {
	public List<MeshFilter> crossSectionMeshes = new();
	// Animated/skinned meshes: baked each render (enable continuousUpdate to re-slice as they animate).
	public List<SkinnedMeshRenderer> crossSectionSkinnedMeshes = new();

	public List<Collider2D> silhouetteColliders = new();
	public List<SpriteRenderer> silhouetteSprites = new();

	[Space]
	// Which sides of the shape get a vector. Drawn as Inside/Outside toggle buttons (by the custom inspector); enable both for the whole grid.
	public MeshVectorFieldGenerator.Sides sides = MeshVectorFieldGenerator.Sides.Outside;

	// By default inside and outside flow the same way (outward, away from the shape) — continuous across the boundary.
	// Reverse one side to make the field diverge from (FlipInside) or converge on (FlipOutside) the outline.
	public MeshVectorFieldGenerator.BoundaryFlip boundaryFlip = MeshVectorFieldGenerator.BoundaryFlip.None;

	// Distance from the edge (in this field's local units) over which the vector fades from full strength (at the edge)
	// to zero. Inner controls the inside region, outer the outside. 0 = no falloff, constant strength throughout.
	[Min(0)] public float innerFalloff = 1f;
	[Min(0)] public float outerFalloff = 1f;

	// Rotates each vector around the plane normal. 0 points straight toward the nearest edge; 90 circulates around the
	// shape; 180 points away from the edge.
	public float angle = 0f;

	[Space]
	[Tooltip("Re-slice every frame. Enable for animated/skinned meshes or moving colliders whose motion the change hash can't see (e.g. live vertex edits, skinned animation).")]
	public bool continuousUpdate = false;

	// GPU buffer holding the aggregated segment endpoints. Owned here (created/grown by the generator, released on
	// disable) so its lifetime is explicit, like the base render texture.
	ComputeBuffer segmentBuffer;

	// Reused scratch so a steady-state re-slice allocates nothing.
	readonly List<Vector2> segments = new();
	readonly List<Vector3> vertexScratch = new();
	readonly List<int> triangleScratch = new();
	readonly List<Vector2> pointScratch = new();
	readonly List<IVectorFieldSegmentSource> runtimeSources = new();
	Mesh bakedMesh;

	// Register a source from code (e.g. a procedurally spawned mesh). Combines with the inspector lists.
	public void AddSource(IVectorFieldSegmentSource source) {
		if (source == null || runtimeSources.Contains(source)) return;
		runtimeSources.Add(source);
		SetDirty();
	}

	public void RemoveSource(IVectorFieldSegmentSource source) {
		if (source != null && runtimeSources.Remove(source)) SetDirty();
	}

	// Sources are external objects whose transforms/shapes don't route through this component's OnValidate, so fold
	// their identity + transform into the change hash. Skinned/animated motion and in-place shape edits still need
	// continuousUpdate (below) since they don't change these values.
	protected override void CollectParameters(ref HashCode hash) {
		base.CollectParameters(ref hash);
		hash.Add((int)sides);
		hash.Add((int)boundaryFlip);
		hash.Add(innerFalloff);
		hash.Add(outerFalloff);
		hash.Add(angle);

		foreach (var mf in crossSectionMeshes) {
			hash.Add(mf != null ? mf.transform.localToWorldMatrix : Matrix4x4.identity);
			hash.Add(mf != null && mf.sharedMesh != null ? mf.sharedMesh.GetEntityId().GetHashCode() : 0);
		}
		foreach (var smr in crossSectionSkinnedMeshes)
			hash.Add(smr != null ? smr.transform.localToWorldMatrix : Matrix4x4.identity);
		foreach (var col in silhouetteColliders) {
			hash.Add(col != null ? col.transform.localToWorldMatrix : Matrix4x4.identity);
			hash.Add(col != null ? col.GetEntityId().GetHashCode() : 0);
			hash.Add(col != null ? col.offset : Vector2.zero);
		}
		foreach (var sr in silhouetteSprites) {
			hash.Add(sr != null ? sr.transform.localToWorldMatrix : Matrix4x4.identity);
			hash.Add(sr != null && sr.sprite != null ? sr.sprite.GetEntityId().GetHashCode() : 0);
		}
	}

	// continuousUpdate content (skinned animation, live shape edits) changes without moving any hashed value, so drive a
	// re-render off every tick when it's on.
	public override void Update() {
		if (continuousUpdate && isActiveAndEnabled) SetDirty();
		base.Update();
	}

	protected override void RenderInternal() {
		// On the very first frame after the component is added, OnEnable renders before the base OnValidate has applied
		// the default grid size, so GridSize can still be zero here. Bail — OnValidate marks us dirty and we re-render
		// with a valid size next tick.
		var gridSize = GridSize;
		if (gridSize.x <= 0 || gridSize.y <= 0) return;

		EnsureHasValidRenderTexture();

		segments.Clear();
		bool anyClosed = false;
		Matrix4x4 worldToFieldLocal = transform.worldToLocalMatrix;

		// 3D cross-sections. A mesh only contributes a valid interior when it's watertight (auto-detected + cached);
		// open surfaces still contribute their distance boundary but don't enable the inside test.
		foreach (var mf in crossSectionMeshes) {
			if (mf == null || mf.sharedMesh == null) continue;
			var m = worldToFieldLocal * mf.transform.localToWorldMatrix;
			MeshVectorFieldExtractors.AppendMeshCrossSection(segments, mf.sharedMesh, m, vertexScratch, triangleScratch);
			anyClosed |= MeshVectorFieldExtractors.IsMeshClosed(mf.sharedMesh);
		}
		foreach (var smr in crossSectionSkinnedMeshes) {
			if (smr == null) continue;
			if (bakedMesh == null) bakedMesh = new Mesh { name = "MeshVectorField Baked" };
			smr.BakeMesh(bakedMesh);
			var m = worldToFieldLocal * smr.transform.localToWorldMatrix;
			MeshVectorFieldExtractors.AppendMeshCrossSection(segments, bakedMesh, m, vertexScratch, triangleScratch);
			anyClosed |= MeshVectorFieldExtractors.IsMeshClosed(bakedMesh);
		}

		// 2D silhouettes.
		foreach (var col in silhouetteColliders) {
			if (col == null) continue;
			MeshVectorFieldExtractors.AppendCollider2DSilhouette(segments, col, worldToFieldLocal, pointScratch);
			if (col is not EdgeCollider2D) anyClosed = true;
		}
		foreach (var sr in silhouetteSprites) {
			if (sr == null || sr.sprite == null) continue;
			MeshVectorFieldExtractors.AppendSprite(segments, sr, worldToFieldLocal, pointScratch);
			anyClosed = true;
		}

		// Runtime-registered sources.
		foreach (var source in runtimeSources) {
			if (source == null || !source.IsValid) continue;
			source.AppendSegments(segments, worldToFieldLocal);
			if (source.IsClosed) anyClosed = true;
		}

		// gridToPlane maps a grid cell straight into this field's local plane space — the same space the segments were
		// folded into above. The inside test runs only when some source actually forms closed loops (open geometry makes
		// the parity meaningless), so sides = Inside just works when the geometry supports it and is empty when it can't.
		bool hasInsideTest = anyClosed;
		MeshVectorFieldGenerator.Dispatch(renderTexture, ref segmentBuffer, gridSize, segments, GridToLocalMatrix,
				// Unit strength: the base applies `magnitude` (and cookie) as an output transform in Render(),
				// so passing `magnitude` here would double-apply it.
				sides, boundaryFlip, innerFalloff, outerFalloff, angle, 1f, hasInsideTest);
	}

	protected override void OnDisable() {
		base.OnDisable();
		// Render textures aren't GC'd and ComputeBuffers must be released explicitly; rebuilt on the next dispatch.
		segmentBuffer?.Release();
		segmentBuffer = null;
	}

	protected override void OnDestroy() {
		base.OnDestroy();
		if (bakedMesh != null) VectorFieldObjectUtils.DestroyAutomatic(bakedMesh);
		bakedMesh = null;
	}
}
