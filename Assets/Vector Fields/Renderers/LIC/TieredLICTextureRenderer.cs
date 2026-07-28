using System.Collections.Generic;
using UnityEngine;

namespace VectorFields {
    // VectorFieldTextureRenderer specialised for the "Vector Fields/LIC/LIC (Tiered)" shader. Drives the shared flow
    // styling (VectorFieldFlowStyle) plus N SPEED TIERS of LIC looks (noise texture + scale + step length + anim speed)
    // keyed to positions on the normalised speed axis; per pixel the shader convolves the two tiers straddling the local
    // flow speed and blends them — e.g. fine short hairs where the flow is slow, long coarse streaks where it's fast.
    //
    // Tier noise textures are packed into a Texture2DArray (one slice per tier, sorted by speed); the per-tier scalar
    // params + speeds go into float[] uniforms — same scheme as TieredFlowMapRenderer.
    [ExecuteAlways]
    [AddComponentMenu("Vector Fields/Renderers/LIC (Tiered)")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class TieredLICTextureRenderer : VectorFieldTextureRenderer {
        public const int MaxTiers = 8;   // keep in sync with VF_MAX_TIERS in VectorFieldSpeedTiers.cginc

        [System.Serializable]
        public struct LICTier {
            [Tooltip("Position on the normalised speed axis (0 = still, 1 = Max Speed) where this look sits.")]
            [Range(0f, 1f)] public float speed;
            [Tooltip("White-noise texture for this tier. Empty = white. Keep the scale low — a few px per field texel.")]
            public Texture noiseTexture;
            [Tooltip("Noise tiling. Keep low — too high tiles it sub-pixel and LIC can't comb anything.")]
            public float noiseScale;
            [Tooltip("Per-step march length, in UV units. Longer steps = longer streaks.")]
            [Range(0.0005f, 0.02f)] public float stepLength;
            [Tooltip("Animation speed of the flowing streaks in this tier.")]
            [Range(0f, 8f)] public float animSpeed;
        }

        [SerializeField] VectorFieldFlowStyle style = new VectorFieldFlowStyle();

        [Tooltip("LIC looks keyed to flow speed. Each pixel convolves and blends the two tiers straddling its local speed.")]
        [SerializeField] List<LICTier> tiers = DefaultTiers();

        // Global look, shared across tiers.
        [Tooltip("Streamline steps per side. More = longer, smoother streaks (costlier — and the tiered shader marches up to twice).")]
        [Range(1, 64)] [SerializeField] int stepCount = 32;
        [Tooltip("Animation phase offset (streaks flow as this advances; also driven by time via each tier's Anim Speed).")]
        [SerializeField] float phase = 0f;
        [Tooltip("Resolution each tier noise texture is resampled to inside the packed array.")]
        [SerializeField] int arrayResolution = 256;

        RenderTexture noiseArray;                 // Texture2DArray, one slice per tier (sorted by speed)
        readonly List<LICTier> sorted = new();

        static readonly int NoiseArray = Shader.PropertyToID("_NoiseArray");
        static readonly int StepCount = Shader.PropertyToID("_StepCount");
        static readonly int Phase = Shader.PropertyToID("_Phase");
        static readonly int TierSpeed = Shader.PropertyToID("_TierSpeed");
        static readonly int TierNoiseScale = Shader.PropertyToID("_TierNoiseScale");
        static readonly int TierStepLength = Shader.PropertyToID("_TierStepLength");
        static readonly int TierAnimSpeed = Shader.PropertyToID("_TierAnimSpeed");
        static readonly int TierCount = Shader.PropertyToID("_TierCount");

        protected override void OnEnable() {
            style.Bake();
            BakeArray();
            base.OnEnable(); // subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
        }

        protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
            style.Apply(block);
            if (noiseArray != null) block.SetTexture(NoiseArray, noiseArray);
            block.SetFloat(StepCount, stepCount);
            block.SetFloat(Phase, phase);

            // Push the sorted tiers as float[] uniforms, padded to MaxTiers with the last entry so a dynamic index is safe.
            int count = Mathf.Max(1, sorted.Count);
            var speeds = new float[MaxTiers];
            var noiseScales = new float[MaxTiers];
            var stepLengths = new float[MaxTiers];
            var animSpeeds = new float[MaxTiers];
            for (int i = 0; i < MaxTiers; i++) {
                var t = sorted[Mathf.Min(i, count - 1)];
                speeds[i] = t.speed; noiseScales[i] = t.noiseScale; stepLengths[i] = t.stepLength; animSpeeds[i] = t.animSpeed;
            }
            block.SetFloatArray(TierSpeed, speeds);
            block.SetFloatArray(TierNoiseScale, noiseScales);
            block.SetFloatArray(TierStepLength, stepLengths);
            block.SetFloatArray(TierAnimSpeed, animSpeeds);
            block.SetInt(TierCount, count);
        }

    #if UNITY_EDITOR
        protected override void OnValidate() {
            style.Bake();
            BakeArray();
            base.OnValidate();
        }
    #endif

        void OnDestroy() {
            style?.Dispose();
            VectorFieldRendererUtils.ReleaseTextureArray(ref noiseArray);
        }

        // Sort the tiers by speed (into `sorted`, capped at MaxTiers) and pack their noise textures into the Texture2DArray.
        // Slice order matches the sorted order, so it lines up with the float[] uniforms pushed in ConfigurePropertyBlock.
        void BakeArray() {
            sorted.Clear();
            if (tiers != null) sorted.AddRange(tiers);
            if (sorted.Count == 0) sorted.Add(new LICTier { speed = 0f, noiseScale = 2f, stepLength = 0.003f, animSpeed = 2f });
            sorted.Sort((a, b) => a.speed.CompareTo(b.speed));
            if (sorted.Count > MaxTiers) sorted.RemoveRange(MaxTiers, sorted.Count - MaxTiers);

            var textures = new List<Texture>(sorted.Count);
            foreach (var t in sorted) textures.Add(t.noiseTexture);
            VectorFieldRendererUtils.BakeTextureArray(ref noiseArray, textures, arrayResolution);
        }

        static List<LICTier> DefaultTiers() => new() {
            new LICTier { speed = 0f, noiseScale = 2f, stepLength = 0.002f, animSpeed = 1f },  // short fine hairs
            new LICTier { speed = 1f, noiseScale = 2f, stepLength = 0.005f, animSpeed = 4f },  // long fast streaks
        };
    }
}
