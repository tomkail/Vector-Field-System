using System.Collections.Generic;
using UnityEngine;

namespace VectorFields {
    // VectorFieldFlowIBFV specialised for the "Vector Fields/IBFV/IBFV (Tiered)" update shader. Inherits the whole
    // ping-pong feedback loop and present pass, and adds N SPEED TIERS of injection noise (texture + scale + amount) keyed
    // to positions on the normalised speed axis; per pixel the update pass blends the two tiers straddling the local flow
    // speed — e.g. faint fine twinkle where the flow is slow, strong coarse twinkle where it's fast. The base's single
    // Noise Scale / Noise Amount are unused (the tiers replace them); Flow Step and Noise Rate stay global.
    //
    // Tier noise textures are packed into a Texture2DArray (one slice per tier, sorted by speed); empty tier textures fall
    // back to the base's auto-generated white noise (R = value, G = twinkle phase — assign textures with the same layout).
    [ExecuteAlways]
    [AddComponentMenu("Vector Fields/Renderers/Flow (IBFV, Tiered)")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class TieredVectorFieldFlowIBFV : VectorFieldFlowIBFV {
        public const int MaxTiers = 8;   // keep in sync with VF_MAX_TIERS in VectorFieldSpeedTiers.cginc

        [System.Serializable]
        public struct NoiseTier {
            [Tooltip("Position on the normalised speed axis (0 = still, 1 = Max Speed) where this look sits.")]
            [Range(0f, 1f)] public float speed;
            [Tooltip("Injection noise for this tier (R = value, G = twinkle phase). Empty = the auto-generated white noise.")]
            public Texture texture;
            [Tooltip("Tiling of this tier's injection noise across the quad. Higher = finer streaks.")]
            public float noiseScale;
            [Tooltip("Fraction of fresh noise injected each frame in this tier. Lower = longer-lived streaks.")]
            [Range(0f, 1f)] public float noiseAmount;
        }

        [Tooltip("Noise looks keyed to flow speed. Each pixel blends the two tiers straddling its local speed.")]
        [SerializeField] List<NoiseTier> tiers = DefaultTiers();

        [Tooltip("Resolution each tier noise texture is resampled to inside the packed array.")]
        [SerializeField] int arrayResolution = 256;

        RenderTexture noiseArray;                 // Texture2DArray, one slice per tier (sorted by speed)
        readonly List<NoiseTier> sorted = new();
        bool arrayDirty = true;

        static readonly int NoiseArray = Shader.PropertyToID("_NoiseArray");
        static readonly int MaxSpeed_ = Shader.PropertyToID("_MaxSpeed");
        static readonly int TierSpeed = Shader.PropertyToID("_TierSpeed");
        static readonly int TierNoiseScale = Shader.PropertyToID("_TierNoiseScale");
        static readonly int TierNoiseAmount = Shader.PropertyToID("_TierNoiseAmount");
        static readonly int TierCount = Shader.PropertyToID("_TierCount");

        protected override string UpdateShaderName => "Vector Fields/IBFV/IBFV (Tiered)";

        protected override void OnEnable() {
            arrayDirty = true;
            base.OnEnable();
        }

    #if UNITY_EDITOR
        protected override void OnValidate() {
            arrayDirty = true;
            base.OnValidate();
        }
    #endif

        protected override void OnDestroy() {
            base.OnDestroy();
            VectorFieldRendererUtils.ReleaseTextureArray(ref noiseArray);
        }

        protected override void ConfigureUpdateMaterial(Material material) {
            // Bake lazily here (not in OnEnable): the fallback for empty tier textures is the base's auto-generated noise,
            // which only exists after the base's EnsureResources has run.
            if (arrayDirty || noiseArray == null) BakeArray();

            material.SetTexture(NoiseArray, noiseArray);
            material.SetFloat(MaxSpeed_, Mathf.Max(1e-4f, style.maxSpeed));

            // Push the sorted tiers as float[] uniforms, padded to MaxTiers with the last entry so a dynamic index is safe.
            int count = Mathf.Max(1, sorted.Count);
            var speeds = new float[MaxTiers];
            var noiseScales = new float[MaxTiers];
            var noiseAmounts = new float[MaxTiers];
            for (int i = 0; i < MaxTiers; i++) {
                var t = sorted[Mathf.Min(i, count - 1)];
                speeds[i] = t.speed; noiseScales[i] = t.noiseScale; noiseAmounts[i] = t.noiseAmount;
            }
            material.SetFloatArray(TierSpeed, speeds);
            material.SetFloatArray(TierNoiseScale, noiseScales);
            material.SetFloatArray(TierNoiseAmount, noiseAmounts);
            material.SetInt(TierCount, count);
        }

        // Sort the tiers by speed (into `sorted`, capped at MaxTiers) and pack their noise textures into the Texture2DArray.
        // Slice order matches the sorted order, so it lines up with the float[] uniforms pushed in ConfigureUpdateMaterial.
        void BakeArray() {
            sorted.Clear();
            if (tiers != null) sorted.AddRange(tiers);
            if (sorted.Count == 0) sorted.Add(new NoiseTier { speed = 0f, noiseScale = 6f, noiseAmount = 0.08f });
            sorted.Sort((a, b) => a.speed.CompareTo(b.speed));
            if (sorted.Count > MaxTiers) sorted.RemoveRange(MaxTiers, sorted.Count - MaxTiers);

            var textures = new List<Texture>(sorted.Count);
            foreach (var t in sorted) textures.Add(t.texture);
            VectorFieldRendererUtils.BakeTextureArray(ref noiseArray, textures, arrayResolution, fallback: SharedNoiseTexture);
            arrayDirty = false;
        }

        static List<NoiseTier> DefaultTiers() => new() {
            new NoiseTier { speed = 0f, noiseScale = 8f, noiseAmount = 0.05f },  // fine, faint
            new NoiseTier { speed = 1f, noiseScale = 4f, noiseAmount = 0.12f },  // coarse, strong
        };
    }
}
