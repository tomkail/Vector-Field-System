using UnityEngine;

// VectorFieldTextureRenderer specialised for the "Vector Fields/Flow-Aligned Texture" shader. On top of the base
// _MainTex binding it bakes and supplies the two ramp inputs that shader samples: an amplitude->alpha curve
// (_AmplitudeRamp) and a recolour gradient (_ColorGradient). Only the Flow-Aligned shader reads these — the other flow
// shaders (Water Flow Map / Lit, LIC) need just the field texture, so they use the plain base component instead.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow-Aligned Texture Renderer")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class FlowAlignedTextureRenderer : VectorFieldTextureRenderer {
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

	// Maps flow magnitude (0..1 along the X axis) to an alpha multiplier (Y), baked into the _AmplitudeRamp texture the
	// flow shader samples. Default rolls opacity in linearly with magnitude; edit it to fade still regions out, add a
	// threshold, ease the rolloff, etc.
	// Rendered as a 0..1 ranged curve by FlowAlignedTextureRendererEditor (was [CurveRange]).
	[SerializeField] AnimationCurve amplitudeAlphaCurve = AnimationCurve.Linear(0, 0, 1, 1);
	Texture2D rampTexture;

	// Recolors the white streaks when the material's "Use Texture Color" is off, sampled by the material's gradient
	// source (flow magnitude or streak luminance). Baked into the _ColorGradient ramp the shader samples. Defaults to
	// solid white, which reproduces the plain white-streak look.
	[SerializeField] Gradient colorGradient = WhiteGradient();
	Texture2D colorGradientTexture;

	protected override void OnEnable() {
		BakeRamp();
		base.OnEnable(); // subscribes and binds — the bind pushes our ramps via ConfigurePropertyBlock
	}

	// Add the two ramp textures to the same property block the base fills with _MainTex.
	protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
		if (rampTexture == null || colorGradientTexture == null) BakeRamp();
		block.SetTexture(AmplitudeRamp, rampTexture);
		block.SetTexture(ColorGradient, colorGradientTexture);
	}

	// Bake the amplitude->alpha curve and colour gradient into the ramp textures the shader samples. Reuses the existing
	// textures in place (only reallocates if missing), so re-baking on an edit is cheap.
	void BakeRamp() {
		if (amplitudeAlphaCurve != null)
			VectorFieldUtils.CreateRampTextureFromAnimationCurve(amplitudeAlphaCurve, RampResolution, ref rampTexture);
		if (colorGradient != null)
			VectorFieldUtils.CreateColorRampTextureFromGradient(colorGradient, RampResolution, ref colorGradientTexture);
	}

#if UNITY_EDITOR
	protected override void OnValidate() {
		BakeRamp();
		base.OnValidate(); // re-binds if active, pushing the freshly baked ramps
	}
#endif

	void OnDestroy() {
		if (rampTexture != null) VectorFieldObjectUtils.DestroyAutomatic(rampTexture);
		if (colorGradientTexture != null) VectorFieldObjectUtils.DestroyAutomatic(colorGradientTexture);
	}
}
