using UnityEngine;

namespace VectorFields {
    // VectorFieldTextureRenderer specialised for the "Vector Fields/Flow-Aligned/Flow-Aligned" shader. Drives the shared flow
    // styling (VectorFieldFlowStyle) AND every Flow-Aligned material setting from the component, so the whole effect is
    // controlled from the inspector rather than the material asset. Everything is pushed into the base's property block.
    //
    // Scalar/enum defaults mirror the demo material (Flow Aligned.mat), so an object using that material keeps its look
    // after these moved off the material; a fresh component starts from that same known-good configuration. Textures
    // (streak texture) are only pushed when assigned — leave empty to keep whatever the material has.
    [ExecuteAlways]
    [AddComponentMenu("Vector Fields/Renderers/Flow-Aligned Texture Renderer")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class FlowAlignedTextureRenderer : VectorFieldTextureRenderer {
        public enum FlowSampling { CellBlendLegacy = 0, CellBlendSeamMasked = 1, CellBlendSeamCopy = 2 }
        public enum Rotation { Rotate0 = 0, Rotate90 = 1, Rotate180 = 2, Rotate270 = 3 }

        [SerializeField] VectorFieldFlowStyle style = new VectorFieldFlowStyle();

        // Flow-Aligned is the only renderer with a coloured streak texture, so this toggle is specific to it. On = tint the
        // speed colour by the streak texture's own RGB; off = pure speed colour.
        [SerializeField] bool useTextureColor;

        [Tooltip("The streak/sand texture combed along the flow. Leave empty to keep the material's assigned texture.")]
        [SerializeField] Texture streakTexture;
        [Tooltip("Tiling of the streak texture (bigger = finer detail).")]
        [Range(0f, 100f)] [SerializeField] float textureScale = 10f;
        [Tooltip("Rotates the sampled streak texture by 90° steps.")]
        [SerializeField] Rotation textureRotation = Rotation.Rotate0;
        [Tooltip("Streak brightness/gain.")]
        [Range(0f, 16f)] [SerializeField] float brightness = 1f;
        [Tooltip("How fast the streak texture scrolls along the flow.")]
        [Range(0f, 500f)] [SerializeField] float speed = 93f;
        [Tooltip("Streak cell count across the field — the density of the flow-aligned pattern (independent of field resolution).")]
        [Range(0f, 256f)] [SerializeField] float gridCellCount = 60f;
        [Tooltip("Sub-rect of the field to sample (uvMin.xy, uvMax.xy). Default (0,0,1,1) = whole field.")]
        [SerializeField] Vector4 rect = new Vector4(0f, 0f, 1f, 1f);

        [Tooltip("How the flow is sampled across cell edges (the per-cell seam handling — see FLOW_ALIGNED_NOTES.md).")]
        [SerializeField] FlowSampling flowSamplingMode = FlowSampling.CellBlendSeamCopy;
        [Tooltip("Sample amplitude continuously (smooth alpha). Off = legacy four-corner amplitude blend.")]
        [SerializeField] bool continuousAmplitude = true;
        [Tooltip("Seam-mask band half-width, in screen pixels (Seam Masked mode).")]
        [Range(0f, 8f)] [SerializeField] float seamBand = 1f;
        [Tooltip("Seam-mask reach across the seam, in screen pixels. Keep > Seam Band.")]
        [Range(0f, 16f)] [SerializeField] float seamReach = 1f;
        [Tooltip("Debug view of the seam-copy source (green = clean, red = still on a seam).")]
        [SerializeField] bool seamDebug;

        static readonly int UseTextureColor = Shader.PropertyToID("_UseTextureColor");
        static readonly int Tex = Shader.PropertyToID("_Tex");
        static readonly int TextureScale = Shader.PropertyToID("_TextureScale");
        static readonly int TextureRotationId = Shader.PropertyToID("_TextureRotation");
        static readonly int Brightness = Shader.PropertyToID("_Brightness");
        static readonly int Speed = Shader.PropertyToID("_Speed");
        static readonly int GridCellCount = Shader.PropertyToID("_GridCellCount");
        static readonly int Rect_ = Shader.PropertyToID("_Rect");
        static readonly int FlowSamplingModeId = Shader.PropertyToID("_FlowSamplingMode");
        static readonly int ContinuousAmplitude = Shader.PropertyToID("_ContinuousAmplitude");
        static readonly int SeamBand = Shader.PropertyToID("_SeamBand");
        static readonly int SeamReach = Shader.PropertyToID("_SeamReach");
        static readonly int SeamDebug = Shader.PropertyToID("_SeamDebug");

        protected override void OnEnable() {
            style.Bake();
            base.OnEnable(); // subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
        }

        // Push the shared styling + this shader's texture-colour toggle + all Flow-Aligned settings into the same property
        // block the base fills with _MainTex.
        protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
            style.Apply(block);
            block.SetFloat(UseTextureColor, useTextureColor ? 1f : 0f);
            if (streakTexture != null) block.SetTexture(Tex, streakTexture); // empty = keep the material's texture
            block.SetFloat(TextureScale, textureScale);
            block.SetFloat(TextureRotationId, (int)textureRotation);
            block.SetFloat(Brightness, brightness);
            block.SetFloat(Speed, speed);
            block.SetFloat(GridCellCount, gridCellCount);
            block.SetVector(Rect_, rect);
            block.SetFloat(FlowSamplingModeId, (int)flowSamplingMode);
            block.SetFloat(ContinuousAmplitude, continuousAmplitude ? 1f : 0f);
            block.SetFloat(SeamBand, seamBand);
            block.SetFloat(SeamReach, seamReach);
            block.SetFloat(SeamDebug, seamDebug ? 1f : 0f);
        }

    #if UNITY_EDITOR
        protected override void OnValidate() {
            style.Bake();
            base.OnValidate(); // re-binds if active, pushing the freshly baked styling + settings
        }
    #endif

        protected virtual void OnDestroy() {   // virtual: Unity only calls the most-derived message, so subclasses must chain
            style?.Dispose();
        }
    }
}
