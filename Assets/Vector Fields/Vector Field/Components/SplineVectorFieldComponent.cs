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
// the path's flow (the tangent there) or a fixed direction. The field has a *width*: each cell's distance from the
// path, normalized against the width at its nearest point (base `width` × the optional per-point `widthAlongSpline`
// multiplier), drives everything across the path — strength comes from `falloffCurve` sampled at that normalized
// distance, and `rotationAlongSpline` adds a rotation offset that scales with SIGNED normalized distance (0 on the
// path, ±value at the edges), fanning the flow out from or into the centreline. The per-point SplineData channels are
// authored at points along the spline and interpolated between them; both have scene-view editor tools (see
// SplineVectorFieldWidthTool / SplineVectorFieldRotationTool). The spline is flattened to samplesPerSpline
// attribute-carrying samples per spline each render.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Spline Vector Field")]
public class SplineVectorFieldComponent : VectorFieldComponent {
	// The spline(s) to trace. Falls back to a SplineContainer on this GameObject when unset (Reset assigns it).
	public SplineContainer splineContainer;

	[Space]
	// Flow: vectors follow the path (tangent at the nearest point). Fixed: every cell uses fixedDirection.
	public SplineVectorFieldGenerator.DirectionMode directionMode = SplineVectorFieldGenerator.DirectionMode.Flow;
	// Only used in Fixed mode. In this field's local plane space; normalized before use.
	public Vector2 fixedDirection = Vector2.right;

	[Space]
	// Rotates every vector around the plane normal, in degrees. Applied in both direction modes.
	public float rotation = 0f;
	// Rotation offset (degrees) at the field's *edge*, authored at points along the spline and interpolated between
	// them. Each cell scales the value at its nearest point by its signed normalized distance from the path (0 on the
	// path, +1 at the width edge on the path's left, -1 on its right), so positive values fan the flow outward from
	// the centreline and negative values pull it inward. Empty = no contribution. Requires a width to normalize
	// against — with width 0 it does nothing.
	public SplineData<float> rotationAlongSpline = new SplineData<float>();

	[Space]
	// Half-extent of the field either side of the path, in this field's local units: the distance from the path over
	// which falloffCurve is evaluated (and rotationAlongSpline reaches full effect). 0 = no width — constant
	// falloffCurve(0) strength everywhere and no edge rotation.
	[Min(0)] public float width = 1f;
	// Multiplier on `width` authored at points along the spline and interpolated between them; each cell uses the
	// value at its nearest point on the path. Empty = 1 everywhere.
	public SplineData<float> widthAlongSpline = new SplineData<float>();
	// Strength across the width: sampled at each cell's normalized distance from the path (0 = on the path, 1 = at
	// the width edge; clamped, so the curve's end value holds beyond the edge). The default linear 1→0 fade
	// reproduces the classic distance falloff.
	public AnimationCurve falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

	[Space]
	// How many samples each spline is flattened into (per render). More samples follow tight curves more closely.
	[Min(2)] public int samplesPerSpline = 64;

	// GPU buffers holding the flattened segment-pair samples and the baked falloff curve. Owned here (created/grown
	// by the generator, released on disable) so their lifetime is explicit, like the base render texture.
	ComputeBuffer sampleBuffer;
	ComputeBuffer falloffCurveBuffer;

	// falloffCurve baked to evenly-spaced samples for the GPU; rebaked each render (cheap for this resolution).
	const int falloffCurveResolution = 64;
	readonly float[] falloffCurveSamples = new float[falloffCurveResolution];

	// Reused scratch so a steady-state re-render allocates nothing.
	readonly List<SplineVectorFieldGenerator.Sample> samples = new();
	readonly List<SplineVectorFieldGenerator.Sample> splineScratch = new();

	public SplineContainer Container => splineContainer != null ? splineContainer : GetComponent<SplineContainer>();

	// The width the field spans at `t` (normalized) along the given spline: base width × the interpolated per-point
	// multiplier. Used by the renderer and by the scene tools' envelope drawing.
	public float WidthAt(Spline spline, float t) {
		float w = width;
		if (widthAlongSpline != null && widthAlongSpline.Count > 0)
			w *= widthAlongSpline.Evaluate(spline, t, PathIndexUnit.Normalized, new LerpFloat());
		return w;
	}

	// Editor-only, runs when the component is first added: pick up the SplineContainer the create-menu (or the user)
	// put on the same GameObject so the reference is visible in the inspector rather than an invisible fallback.
	void Reset() {
		if (splineContainer == null) splineContainer = GetComponent<SplineContainer>();
	}

	protected override void OnValidate() {
		// A width multiplier's neutral value is 1; SplineData's serialized default is 0, which would collapse the
		// field at any point added via the scene handles. 0 is never a useful default here, so migrate it.
		if (widthAlongSpline != null && widthAlongSpline.DefaultValue == 0f) widthAlongSpline.DefaultValue = 1f;
		base.OnValidate();
	}

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
		falloffCurveBuffer?.Release();
		falloffCurveBuffer = null;
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
		hash.Add(width);
		hash.Add(samplesPerSpline);
		HashCurve(ref hash, falloffCurve);

		var container = Container;
		hash.Add(container != null ? container.transform.localToWorldMatrix : Matrix4x4.identity);
		hash.Add(container != null ? container.Splines.Count : 0);
		HashSplineData(ref hash, rotationAlongSpline);
		HashSplineData(ref hash, widthAlongSpline);
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

	// Curve edits through scene/curve-editor windows still route through OnValidate, but hash the keys anyway so any
	// programmatic edit is caught by the same polling that covers the SplineData channels.
	static void HashCurve(ref HashCode hash, AnimationCurve curve) {
		if (curve == null) { hash.Add(0); return; }
		hash.Add(curve.length);
		for (int i = 0; i < curve.length; i++) {
			var key = curve[i];
			hash.Add(key.time);
			hash.Add(key.value);
			hash.Add(key.inTangent);
			hash.Add(key.outTangent);
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

		for (int i = 0; i < falloffCurveResolution; i++)
			falloffCurveSamples[i] = falloffCurve != null ? falloffCurve.Evaluate(i / (float)(falloffCurveResolution - 1)) : 1f;

		// Unit strength: the base applies `magnitude` (and cookie) as an output transform in Render(), so passing
		// `magnitude` here would double-apply it.
		var direction = fixedDirection.sqrMagnitude > 1e-10f ? fixedDirection.normalized : Vector2.zero;
		SplineVectorFieldGenerator.Dispatch(renderTexture, ref sampleBuffer, ref falloffCurveBuffer, gridSize,
			samples, falloffCurveSamples, GridToLocalMatrix, directionMode, direction, 1f);
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

		splineScratch.Clear();
		for (int i = 0; i < count; i++) {
			float t = i / (float)(count - 1);

			// Container evaluation is world space; fold into this field's local plane so the samples and the per-cell
			// points share one frame (matching the mesh field's segment convention).
			Vector3 localPosition = worldToFieldLocal.MultiplyPoint3x4(container.EvaluatePosition(splineIndex, t));
			Vector3 localTangent = worldToFieldLocal.MultiplyVector(container.EvaluateTangent(splineIndex, t));
			var tangent = new Vector2(localTangent.x, localTangent.y);
			if (tangent.sqrMagnitude > 1e-10f) tangent.Normalize();

			splineScratch.Add(new SplineVectorFieldGenerator.Sample {
				position = new Vector2(localPosition.x, localPosition.y),
				tangent = tangent,
				rotation = rotation,
				edgeRotation = hasRotationData ? rotationAlongSpline.Evaluate(spline, t, PathIndexUnit.Normalized, new LerpFloat()) : 0f,
				width = WidthAt(spline, t),
			});
		}

		for (int i = 0; i < splineScratch.Count - 1; i++) {
			samples.Add(splineScratch[i]);
			samples.Add(splineScratch[i + 1]);
		}
	}
}
#endif
