using UnityEngine;

// Displays a vector field's render texture on a mesh (the raw encoded field, color = vector * 0.5 + 0.5), sized to
// overlay the field in world space. Reads the GPU renderTexture directly, so it stays live without a CPU readback.
//
// The field texture is pushed into the renderer's material as _MainTex via a MaterialPropertyBlock, so it overrides
// only this renderer's instance — it never edits the shared material asset, and never replaces the material you
// assigned in the inspector (e.g. the Vector Field Flow Visualization material).
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class VectorFieldTextureRenderer : MonoBehaviour {
	static readonly int MainTex = Shader.PropertyToID("_MainTex");
	static readonly int MainTexTexelSize = Shader.PropertyToID("_MainTex_TexelSize");
	static readonly int AmplitudeRamp = Shader.PropertyToID("_AmplitudeRamp");
	static readonly int ColorGradient = Shader.PropertyToID("_ColorGradient");
	const int RampResolution = 256;

	static Gradient WhiteGradient() {
		var g = new Gradient();
		g.SetKeys(
			new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
			new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
		return g;
	}

	[SerializeField] VectorFieldComponent _vectorFieldComponent;
	public VectorFieldComponent vectorFieldComponent {
		get => _vectorFieldComponent;
		set {
			if (_vectorFieldComponent == value) return;
			if (isActiveAndEnabled) Unsubscribe();
			_vectorFieldComponent = value;
			if (isActiveAndEnabled) Subscribe();
		}
	}

	// Optional. Leave empty to use the material already on the MeshRenderer (the common case); assign one to have the
	// script drive the renderer's material too.
	[SerializeField] Material materialPrefab;

	// Shifts the quad along the field's plane normal (forward = positive). MatchFieldBounds otherwise pins us to the
	// field centre every tick; this lets you push the quad in front of / behind other geometry to control draw order.
	[SerializeField] float depthOffset;

	// Maps flow magnitude (0..1 along the X axis) to an alpha multiplier (Y), baked into the _AmplitudeRamp texture the
	// flow shader samples. Default rolls opacity in linearly with magnitude; edit it to fade still regions out, add a
	// threshold, ease the rolloff, etc.
	[SerializeField, CurveRange(0, 0, 1, 1)] AnimationCurve amplitudeAlphaCurve = AnimationCurve.Linear(0, 0, 1, 1);
	Texture2D rampTexture;

	// Recolors the white streaks when the material's "Use Texture Color" is off, sampled by the material's gradient
	// source (flow magnitude or streak luminance). Baked into the _ColorGradient ramp the shader samples. Defaults to
	// solid white, which reproduces the plain white-streak look.
	[SerializeField] Gradient colorGradient = WhiteGradient();
	Texture2D colorGradientTexture;

	MeshRenderer meshRenderer => GetComponent<MeshRenderer>();
	MaterialPropertyBlock propertyBlock;

	void OnEnable() {
		BakeRamp();
		Subscribe();
	}

	void OnDisable() {
		Unsubscribe();
	}

	void Subscribe() {
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnRendered += BindTexture;
		BindTexture(); // pick up whatever has already been rendered
	}

	void Unsubscribe() {
		if (_vectorFieldComponent == null) return;
		_vectorFieldComponent.OnRendered -= BindTexture;
	}

	// Re-align every tick (not just on the field's OnRendered) so the quad tracks moves of our own parent — which
	// don't re-render the field. [ExecuteAlways] runs this in edit mode too, on every scene repaint.
	void LateUpdate() {
		MatchFieldBounds();
	}

	// Point the material at the field's live render texture. Driven by OnRendered, since that's when the texture (and
	// its reference, after a resize/recreate) can change.
	void BindTexture() {
		if (_vectorFieldComponent == null) return;

		if (materialPrefab != null && meshRenderer.sharedMaterial != materialPrefab)
			meshRenderer.sharedMaterial = materialPrefab;

		var fieldTexture = _vectorFieldComponent.renderTexture;
		if (fieldTexture == null) return; // nothing rendered yet; OnRendered will call us again once it has

		if (rampTexture == null || colorGradientTexture == null) BakeRamp();

		propertyBlock ??= new MaterialPropertyBlock();
		meshRenderer.GetPropertyBlock(propertyBlock);
		propertyBlock.SetTexture(MainTex, fieldTexture);
		// Bicubic field sampling in the shader needs the field dimensions; set explicitly so we don't rely on Unity
		// auto-populating _MainTex_TexelSize for a property-block-bound texture.
		propertyBlock.SetVector(MainTexTexelSize, new Vector4(
			1f / fieldTexture.width, 1f / fieldTexture.height, fieldTexture.width, fieldTexture.height));
		propertyBlock.SetTexture(AmplitudeRamp, rampTexture);
		propertyBlock.SetTexture(ColorGradient, colorGradientTexture);
		meshRenderer.SetPropertyBlock(propertyBlock);

		MatchFieldBounds();
	}

	// Bake the amplitude->alpha curve into the ramp texture the shader samples. Reuses the existing texture in place
	// (only reallocates if missing), so re-baking on a curve edit is cheap.
	void BakeRamp() {
		if (amplitudeAlphaCurve != null)
			VectorFieldUtils.CreateRampTextureFromAnimationCurve(amplitudeAlphaCurve, RampResolution, ref rampTexture);
		if (colorGradient != null)
			VectorFieldUtils.CreateColorRampTextureFromGradient(colorGradient, RampResolution, ref colorGradientTexture);
	}

#if UNITY_EDITOR
	void OnValidate() {
		BakeRamp();
		if (isActiveAndEnabled) BindTexture();
	}
#endif

	void OnDestroy() {
		if (rampTexture != null) ObjectX.DestroyAutomatic(rampTexture);
		if (colorGradientTexture != null) ObjectX.DestroyAutomatic(colorGradientTexture);
	}

	// Lay the quad over the field's world rect (a unit-quad mesh centred at the origin maps exactly onto it). Replaces
	// the legacy "scale to grid cell count" assumption, which only held when the field sat at the origin with one world
	// unit per cell. Divides by the parent's lossy scale so the world size is correct even under a scaled ancestor.
	void MatchFieldBounds() {
		if (_vectorFieldComponent == null) return;

		var bounds = _vectorFieldComponent.GetBounds();
		transform.position = bounds.center + _vectorFieldComponent.planeNormal * depthOffset;

		var worldSize = new Vector3(bounds.size.x, bounds.size.y, 1);
		var parent = transform.parent;
		if (parent == null) {
			transform.localScale = worldSize;
		} else {
			var parentScale = parent.lossyScale;
			transform.localScale = new Vector3(
				parentScale.x != 0 ? worldSize.x / parentScale.x : worldSize.x,
				parentScale.y != 0 ? worldSize.y / parentScale.y : worldSize.y,
				parentScale.z != 0 ? worldSize.z / parentScale.z : worldSize.z);
		}
	}
}
