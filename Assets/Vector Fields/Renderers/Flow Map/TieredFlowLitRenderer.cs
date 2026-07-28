using System.Collections.Generic;
using UnityEngine;

namespace VectorFields {
    // VectorFieldTextureRenderer specialised for the "Vector Fields/Flow Map/Flow Lit (Tiered)" shader. Drives the field
    // texture plus N SPEED TIERS of water looks (height texture + tiling + flow params) keyed to positions on the
    // normalised speed axis; per pixel the shader height-blends the two tiers straddling the local flow speed before
    // deriving the lit normal. Surface/colour/specular stay on the material (they're global, like the plain Flow Lit).
    //
    // Tier textures are packed into a Texture2DArray (one slice per tier, sorted by speed); the per-tier scalar params +
    // speeds go into float[] uniforms — same scheme as TieredFlowMapRenderer.
    [ExecuteAlways]
    [AddComponentMenu("Vector Fields/Renderers/Flow Lit (Tiered)")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class TieredFlowLitRenderer : VectorFieldTextureRenderer {
        public const int MaxTiers = 8;   // keep in sync with VF_MAX_TIERS in VectorFieldSpeedTiers.cginc

        [System.Serializable]
        public struct WaterFlowTier {
            [Tooltip("Position on the normalised speed axis (0 = still, 1 = Max Speed) where this look sits.")]
            [Range(0f, 1f)] public float speed;
            [Tooltip("Water height texture for this tier (luminance = height). Empty = white.")]
            public Texture texture;
            [Tooltip("Tiling of the water texture across the quad.")]
            public float tiling;
            [Tooltip("How far UVs push per cycle — apparent turbulence/distortion.")]
            [Range(0f, 2f)] public float flowStrength;
            [Tooltip("How fast the ping-pong flow cycle runs.")]
            [Range(0f, 4f)] public float flowSpeed;
        }

        [Tooltip("Water looks keyed to flow speed. Each pixel blends the two tiers straddling its local speed (calm ↔ choppy).")]
        [SerializeField] List<WaterFlowTier> tiers = DefaultTiers();

        // Global look, shared across tiers.
        [Tooltip("Add a second layer at a different scale/speed to hide the single-tiling repetition.")]
        [SerializeField] bool dualScale = true;
        [SerializeField] float detailTiling = 2.17f;
        [SerializeField] float detailSpeed = 1.7f;
        [Tooltip("Flow speed that maps to the top of the tier axis (tier position 1).")]
        [SerializeField] float maxSpeed = 1f;
        [Tooltip("Resolution each tier texture is resampled to inside the packed array.")]
        [SerializeField] int arrayResolution = 256;

        RenderTexture waterArray;                 // Texture2DArray, one slice per tier (sorted by speed)
        readonly List<WaterFlowTier> sorted = new();

        static readonly int WaterArray = Shader.PropertyToID("_WaterArray");
        static readonly int DualScale = Shader.PropertyToID("_DualScale");
        static readonly int DetailTiling = Shader.PropertyToID("_DetailTiling");
        static readonly int DetailSpeed = Shader.PropertyToID("_DetailSpeed");
        static readonly int MaxSpeed_ = Shader.PropertyToID("_MaxSpeed");
        static readonly int TierSpeed = Shader.PropertyToID("_TierSpeed");
        static readonly int TierTiling = Shader.PropertyToID("_TierTiling");
        static readonly int TierStrength = Shader.PropertyToID("_TierStrength");
        static readonly int TierFlowSpeed = Shader.PropertyToID("_TierFlowSpeed");
        static readonly int TierCount = Shader.PropertyToID("_TierCount");

        protected override void OnEnable() {
            BakeArray();
            base.OnEnable(); // subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
        }

        protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
            if (waterArray != null) block.SetTexture(WaterArray, waterArray);
            block.SetFloat(DualScale, dualScale ? 1f : 0f);
            block.SetFloat(DetailTiling, detailTiling);
            block.SetFloat(DetailSpeed, detailSpeed);
            block.SetFloat(MaxSpeed_, Mathf.Max(1e-4f, maxSpeed));

            // Push the sorted tiers as float[] uniforms, padded to MaxTiers with the last entry so a dynamic index is safe.
            int count = Mathf.Max(1, sorted.Count);
            var speeds = new float[MaxTiers];
            var tilings = new float[MaxTiers];
            var strengths = new float[MaxTiers];
            var flowSpeeds = new float[MaxTiers];
            for (int i = 0; i < MaxTiers; i++) {
                var t = sorted[Mathf.Min(i, count - 1)];
                speeds[i] = t.speed; tilings[i] = t.tiling; strengths[i] = t.flowStrength; flowSpeeds[i] = t.flowSpeed;
            }
            block.SetFloatArray(TierSpeed, speeds);
            block.SetFloatArray(TierTiling, tilings);
            block.SetFloatArray(TierStrength, strengths);
            block.SetFloatArray(TierFlowSpeed, flowSpeeds);
            block.SetInt(TierCount, count);
        }

    #if UNITY_EDITOR
        protected override void OnValidate() {
            BakeArray();
            base.OnValidate();
        }
    #endif

        void OnDestroy() {
            VectorFieldRendererUtils.ReleaseTextureArray(ref waterArray);
        }

        // Sort the tiers by speed (into `sorted`, capped at MaxTiers) and pack their textures into the Texture2DArray.
        // Slice order matches the sorted order, so it lines up with the float[] uniforms pushed in ConfigurePropertyBlock.
        void BakeArray() {
            sorted.Clear();
            if (tiers != null) sorted.AddRange(tiers);
            if (sorted.Count == 0) sorted.Add(new WaterFlowTier { speed = 0f, tiling = 4f, flowStrength = 0.3f, flowSpeed = 1f });
            sorted.Sort((a, b) => a.speed.CompareTo(b.speed));
            if (sorted.Count > MaxTiers) sorted.RemoveRange(MaxTiers, sorted.Count - MaxTiers);

            var textures = new List<Texture>(sorted.Count);
            foreach (var t in sorted) textures.Add(t.texture);
            VectorFieldRendererUtils.BakeTextureArray(ref waterArray, textures, arrayResolution);
        }

        static List<WaterFlowTier> DefaultTiers() => new() {
            new WaterFlowTier { speed = 0f, tiling = 4f, flowStrength = 0.3f, flowSpeed = 1f },   // calm
            new WaterFlowTier { speed = 1f, tiling = 4f, flowStrength = 0.5f, flowSpeed = 1.5f }, // choppy
        };
    }
}
