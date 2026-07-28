using System.Collections.Generic;
using UnityEngine;

namespace VectorFields {
    // FlowAlignedTextureRenderer specialised for the "Vector Fields/Flow-Aligned/Flow-Aligned (Tiered)" shader. Inherits
    // every global setting (styling, seam handling, rotation, brightness, grid, rect) and adds N SPEED TIERS of streak
    // looks (texture + scale + scroll speed) keyed to positions on the normalised speed axis; per sample the shader blends
    // the two tiers straddling the local flow speed. The base's single streak texture / Texture Scale / Speed are unused by
    // the tiered shader (the tiers replace them).
    //
    // Tier textures are packed into a Texture2DArray (one slice per tier, sorted by speed); the per-tier scalar params +
    // speeds go into float[] uniforms — same scheme as TieredFlowMapRenderer.
    [ExecuteAlways]
    [AddComponentMenu("Vector Fields/Renderers/Flow-Aligned Texture Renderer (Tiered)")]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class TieredFlowAlignedTextureRenderer : FlowAlignedTextureRenderer {
        public const int MaxTiers = 8;   // keep in sync with VF_MAX_TIERS in VectorFieldSpeedTiers.cginc

        [System.Serializable]
        public struct StreakTier {
            [Tooltip("Position on the normalised speed axis (0 = still, 1 = Max Speed) where this look sits.")]
            [Range(0f, 1f)] public float speed;
            [Tooltip("Streak/sand texture for this tier. Empty = white.")]
            public Texture texture;
            [Tooltip("Tiling of this tier's streak texture (bigger = finer detail).")]
            public float textureScale;
            [Tooltip("How fast this tier's streak texture scrolls along the flow.")]
            public float scrollSpeed;
        }

        [Tooltip("Streak looks keyed to flow speed. Each sample blends the two tiers straddling its local speed.")]
        [SerializeField] List<StreakTier> tiers = DefaultTiers();

        [Tooltip("Resolution each tier texture is resampled to inside the packed array.")]
        [SerializeField] int arrayResolution = 256;

        RenderTexture texArray;                   // Texture2DArray, one slice per tier (sorted by speed)
        readonly List<StreakTier> sorted = new();

        static readonly int TexArray = Shader.PropertyToID("_TexArray");
        static readonly int TierSpeed = Shader.PropertyToID("_TierSpeed");
        static readonly int TierTextureScale = Shader.PropertyToID("_TierTextureScale");
        static readonly int TierScrollSpeed = Shader.PropertyToID("_TierScrollSpeed");
        static readonly int TierCount = Shader.PropertyToID("_TierCount");

        protected override void OnEnable() {
            BakeArray();
            base.OnEnable(); // bakes the style, subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
        }

        protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
            base.ConfigurePropertyBlock(block); // styling + all the global Flow-Aligned settings
            if (texArray != null) block.SetTexture(TexArray, texArray);

            // Push the sorted tiers as float[] uniforms, padded to MaxTiers with the last entry so a dynamic index is safe.
            int count = Mathf.Max(1, sorted.Count);
            var speeds = new float[MaxTiers];
            var scales = new float[MaxTiers];
            var scrolls = new float[MaxTiers];
            for (int i = 0; i < MaxTiers; i++) {
                var t = sorted[Mathf.Min(i, count - 1)];
                speeds[i] = t.speed; scales[i] = Mathf.Max(1e-4f, t.textureScale); scrolls[i] = t.scrollSpeed;
            }
            block.SetFloatArray(TierSpeed, speeds);
            block.SetFloatArray(TierTextureScale, scales);
            block.SetFloatArray(TierScrollSpeed, scrolls);
            block.SetInt(TierCount, count);
        }

    #if UNITY_EDITOR
        protected override void OnValidate() {
            BakeArray();
            base.OnValidate();
        }
    #endif

        protected override void OnDestroy() {
            base.OnDestroy(); // disposes the style
            VectorFieldRendererUtils.ReleaseTextureArray(ref texArray);
        }

        // Sort the tiers by speed (into `sorted`, capped at MaxTiers) and pack their textures into the Texture2DArray.
        // Slice order matches the sorted order, so it lines up with the float[] uniforms pushed in ConfigurePropertyBlock.
        void BakeArray() {
            sorted.Clear();
            if (tiers != null) sorted.AddRange(tiers);
            if (sorted.Count == 0) sorted.Add(new StreakTier { speed = 0f, textureScale = 10f, scrollSpeed = 93f });
            sorted.Sort((a, b) => a.speed.CompareTo(b.speed));
            if (sorted.Count > MaxTiers) sorted.RemoveRange(MaxTiers, sorted.Count - MaxTiers);

            var textures = new List<Texture>(sorted.Count);
            foreach (var t in sorted) textures.Add(t.texture);
            VectorFieldRendererUtils.BakeTextureArray(ref texArray, textures, arrayResolution);
        }

        static List<StreakTier> DefaultTiers() => new() {
            new StreakTier { speed = 0f, textureScale = 10f, scrollSpeed = 93f },  // fine, gentle
            new StreakTier { speed = 1f, textureScale = 5f,  scrollSpeed = 186f }, // bold, fast
        };
    }
}
