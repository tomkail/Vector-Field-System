using UnityEngine;

// VectorFieldTextureRenderer specialised for the "Vector Fields/LIC/LIC" shader. Drives the shared flow
// styling (VectorFieldFlowStyle) AND every LIC material setting from the component, so the whole effect is controlled
// from the inspector rather than the material asset. All settings are pushed into the base's property block.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/LIC Texture Renderer")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class LICTextureRenderer : VectorFieldTextureRenderer {
    [SerializeField] VectorFieldFlowStyle style = new VectorFieldFlowStyle();

    // LIC look — mirrors the shader's material properties. Defaults match the shader defaults.
    [Tooltip("White-noise texture the streamlines convolve. Leave empty to keep the material's assigned noise.")]
    [SerializeField] Texture noiseTexture;
    [Tooltip("Noise tiling. Keep low — a few px per field texel; too high just looks like static.")]
    [SerializeField] float noiseScale = 2f;
    [Tooltip("Streamline steps per side. More = longer, smoother streaks (costlier).")]
    [Range(1, 64)] [SerializeField] int stepCount = 32;
    [Tooltip("Per-step march length, in UV units.")]
    [Range(0.0005f, 0.02f)] [SerializeField] float stepLength = 0.003f;
    [Tooltip("Animation phase offset (streaks flow as this advances; also driven by time via Anim Speed).")]
    [SerializeField] float phase = 0f;
    [Tooltip("Animation speed of the flowing streaks.")]
    [Range(0f, 8f)] [SerializeField] float animSpeed = 2f;

    static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
    static readonly int StepCount = Shader.PropertyToID("_StepCount");
    static readonly int StepLength = Shader.PropertyToID("_StepLength");
    static readonly int Phase = Shader.PropertyToID("_Phase");
    static readonly int AnimSpeed = Shader.PropertyToID("_AnimSpeed");

    protected override void OnEnable() {
        style.Bake();
        base.OnEnable(); // subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
    }

    // Push the shared styling + all LIC settings into the same property block the base fills with _MainTex.
    protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
        style.Apply(block);
        if (noiseTexture != null) block.SetTexture(NoiseTex, noiseTexture); // empty = keep the material's noise
        block.SetFloat(NoiseScale, noiseScale);
        block.SetFloat(StepCount, stepCount);
        block.SetFloat(StepLength, stepLength);
        block.SetFloat(Phase, phase);
        block.SetFloat(AnimSpeed, animSpeed);
    }

#if UNITY_EDITOR
    protected override void OnValidate() {
        style.Bake();
        base.OnValidate(); // re-binds if active, pushing the freshly baked styling + settings
    }
#endif

    void OnDestroy() {
        style?.Dispose();
    }
}
