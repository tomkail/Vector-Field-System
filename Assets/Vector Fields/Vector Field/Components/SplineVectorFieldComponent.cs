// Only compiled when the optional com.unity.splines package is installed — the VectorFields asmdef's versionDefines
// set this define when the package is present (and its GUID reference to Unity.Splines resolves only then), so
// installing/removing the package is all it takes. The generator (SplineVectorFieldGenerator) stays available either
// way; it's polyline-generic and has no splines dependency.
#if VECTOR_FIELDS_SPLINES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.Interpolators;

// Editor-facing wrapper around the code-callable SplineVectorFieldGenerator: traces a Unity spline (every spline in
// the referenced SplineContainer) and makes each cell's vector from its nearest point on the path — either following
// the path's flow (the tangent there) or a fixed direction — rotated around the plane normal and faded by a distance
// falloff. Rotation and falloff each have a per-component value plus optional SplineData authored at points along the
// spline (interpolated between points): rotationAlongSpline adds to `rotation`, falloffAlongSpline multiplies
// `falloff`. The spline is flattened to samplesPerSpline attribute-carrying samples per spline each render.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Spline Vector Field")]
public class SplineVectorFieldComponent : VectorFieldComponent {
	// The spline(s) to trace. Falls back to a SplineContainer on this GameObject when unset.
	public SplineContainer splineContainer;

	[Space]
	// Flow: vectors follow the path (tangent at the nearest point). Fixed: every cell uses fixedDirection.
	public SplineVectorFieldGenerator.DirectionMode directionMode = SplineVectorFieldGenerator.DirectionMode.Flow;
	// Only used in Fixed mode. In this field's local plane space; normalized before use.
	public Vector2 fixedDirection = Vector2.right;

	[Space]
	// Rotates every vector around the plane normal, in degrees. Applied in both direction modes.
	public float rotation = 0f;
	// Extra rotation (degrees) authored at points along the spline and interpolated between them; each cell uses the
	// value at its nearest point on the path, added to `rotation`. Empty = no contribution.
	public SplineData<float> rotationAlongSpline = new SplineData<float>();

	[Space]
	// Distance from the path (in this field's local units) over which the vector fades from full strength (on the
	// path) to zero. 0 = no falloff, constant strength everywhere.
	[Min(0)] public float falloff = 1f;
	// Multiplier on `falloff` authored at points along the spline and interpolated between them; each cell uses the
	// value at its nearest point on the path. Empty = 1 everywhere.
	public SplineData<float> falloffAlongSpline = new SplineData<float>();

	[Space]
	// How many samples each spline is flattened into (per render). More samples follow tight curves more closely.
	[Min(2)] public int samplesPerSpline = 64;

	// GPU buffer holding the flattened segment-pair samples. Owned here (created/grown by the generator, released on
	// disable) so its lifetime is explicit, like the base render texture.
	ComputeBuffer sampleBuffer;

	// Reused scratch so a steady-state re-render allocates nothing.
	readonly List<SplineVectorFieldGenerator.Sample> samples = new();
	readonly List<SplineVectorFieldGenerator.Sample> splineScratch = new();

	SplineContainer Container => splineContainer != null ? splineContainer : GetComponent<SplineContainer>();

	protected override void OnEnable() {
		// Knot edits (scene tools, code) mutate the Spline object without touching any field this component's
		// OnValidate or parameter hash can see, so listen to the splines' own change notification.
		Spline.Changed += OnSplineChanged;
		base.OnEnable();
	}

	protected override void OnDisable() {
		Spline.Changed -= OnSplineChanged;
		base.OnDisable();
		// Render textures aren't GC'd and ComputeBuffers must be released explicitly; rebuilt on the next dispatch.
		sampleBuffer?.Release();
		sampleBuffer = null;
	}

	void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification) {
		var container = Container;
		if (container == null) return;
		var splines = container.Splines;
		for (int i = 0; i < splines.Count; i++)
			if (splines[i] == spline) { SetDirty(); return; }
	}

	// The container is an external object whose transform doesn't route through this component's OnValidate, so fold
	// its identity + transform into the change hash (knot edits are covered by the Spline.Changed subscription; the
	// SplineData contents are hashed directly since scene-handle edits to them bypass OnValidate).
	protected override void CollectParameters(ref HashCode hash) {
		base.CollectParameters(ref hash);
		hash.Add((int)directionMode);
		hash.Add(fixedDirection);
		hash.Add(rotation);
		hash.Add(falloff);
		hash.Add(samplesPerSpline);

		var container = Container;
		hash.Add(container != null ? container.transform.localToWorldMatrix : Matrix4x4.identity);
		hash.Add(container != null ? container.Splines.Count : 0);
		HashSplineData(ref hash, rotationAlongSpline);
		HashSplineData(ref hash, falloffAlongSpline);
	}

	static void HashSplineData(ref HashCode hash, SplineData<float> data) {
		if (data == null) { hash.Add(0); return; }
		hash.Add(data.Count);
		for (int i = 0; i < data.Count; i++) {
			var point = data[i];
			hash.Add(point.Index);
			hash.Add(point.Value);
		}
	}

	protected override void RenderInternal() {
		var gridSize = GridSize;
		if (gridSize.x <= 0 || gridSize.y <= 0) return;

		EnsureHasValidRenderTexture();

		samples.Clear();
		var container = Container;
		if (container != null)
			for (int i = 0; i < container.Splines.Count; i++)
				AppendSpline(container, i);

		// Unit strength: the base applies `magnitude` (and cookie) as an output transform in Render(), so passing
		// `magnitude` here would double-apply it.
		var direction = fixedDirection.sqrMagnitude > 1e-10f ? fixedDirection.normalized : Vector2.zero;
		SplineVectorFieldGenerator.Dispatch(renderTexture, ref sampleBuffer, gridSize, samples, GridToLocalMatrix,
			directionMode, direction, 1f);
	}

	// Flattens one spline into attribute-carrying samples in this field's local plane space, then emits them as the
	// segment pairs the generator consumes (consecutive segments share endpoint samples, so interpolated attributes
	// are continuous along the path). Closed splines round-trip naturally: t = 1 evaluates back at the start.
	void AppendSpline(SplineContainer container, int splineIndex) {
		var spline = container.Splines[splineIndex];
		if (spline == null || spline.Count < 2) return;

		int count = Mathf.Max(2, samplesPerSpline);
		var worldToFieldLocal = transform.worldToLocalMatrix;
		bool hasRotationData = rotationAlongSpline != null && rotationAlongSpline.Count > 0;
		bool hasFalloffData = falloffAlongSpline != null && falloffAlongSpline.Count > 0;

		splineScratch.Clear();
		for (int i = 0; i < count; i++) {
			float t = i / (float)(count - 1);

			// Container evaluation is world space; fold into this field's local plane so the samples and the per-cell
			// points share one frame (matching the mesh field's segment convention).
			Vector3 localPosition = worldToFieldLocal.MultiplyPoint3x4(container.EvaluatePosition(splineIndex, t));
			Vector3 localTangent = worldToFieldLocal.MultiplyVector(container.EvaluateTangent(splineIndex, t));
			var tangent = new Vector2(localTangent.x, localTangent.y);
			if (tangent.sqrMagnitude > 1e-10f) tangent.Normalize();

			float pointRotation = rotation;
			if (hasRotationData) pointRotation += rotationAlongSpline.Evaluate(spline, t, PathIndexUnit.Normalized, new LerpFloat());
			float pointFalloff = falloff;
			if (hasFalloffData) pointFalloff *= falloffAlongSpline.Evaluate(spline, t, PathIndexUnit.Normalized, new LerpFloat());

			splineScratch.Add(new SplineVectorFieldGenerator.Sample {
				position = new Vector2(localPosition.x, localPosition.y),
				tangent = tangent,
				rotation = pointRotation,
				falloff = pointFalloff,
			});
		}

		for (int i = 0; i < splineScratch.Count - 1; i++) {
			samples.Add(splineScratch[i]);
			samples.Add(splineScratch[i + 1]);
		}
	}
}
#endif
