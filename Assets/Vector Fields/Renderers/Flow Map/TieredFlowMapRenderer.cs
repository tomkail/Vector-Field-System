using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// VectorFieldTextureRenderer specialised for the "Vector Fields/Flow Map" shader. Drives every water-flow setting
// AND the shared flow styling from the component (like LIC / Flow-Aligned), and adds N SPEED TIERS: several water looks
// keyed to positions on the normalised speed axis. Per pixel the shader blends the two tiers straddling the local flow
// speed — e.g. calm water where the flow is slow, choppy where it's fast.
//
// Tier textures are packed into a Texture2DArray (one slice per tier, sorted by speed); the per-tier scalar params +
// speeds go into float[] uniforms. The water texture already carries colour, so the shared gradient acts as a SPEED
// colourmap tint (white = untinted); contrast/gamma/background/opacity apply on top.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow Map (Tiered)")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class TieredFlowMapRenderer : VectorFieldTextureRenderer {
    public const int MaxTiers = 8;   // keep in sync with VF_MAX_TIERS in VectorFieldSpeedTiers.cginc

    [System.Serializable]
    public struct WaterFlowTier {
        [Tooltip("Position on the normalised speed axis (0 = still, 1 = Max Speed) where this look sits.")]
        [Range(0f, 1f)] public float speed;
        [Tooltip("Water texture for this tier. Empty = white.")]
        public Texture texture;
        [Tooltip("Tiling of the water texture across the quad.")]
        public float tiling;
        [Tooltip("How far UVs push per cycle — apparent turbulence/distortion.")]
        [Range(0f, 2f)] public float flowStrength;
        [Tooltip("How fast the ping-pong flow cycle runs.")]
        [Range(0f, 4f)] public float flowSpeed;
    }

    [SerializeField] VectorFieldFlowStyle style = MakeUntintedStyle();

    [Tooltip("Water looks keyed to flow speed. Each pixel blends the two tiers straddling its local speed (calm ↔ choppy).")]
    [SerializeField] List<WaterFlowTier> tiers = DefaultTiers();

    // Global look, shared across tiers.
    [Tooltip("Add a second layer at a different scale/speed to hide the single-tiling repetition.")]
    [SerializeField] bool dualScale = true;
    [SerializeField] float detailTiling = 2.17f;
    [SerializeField] float detailSpeed = 1.7f;
    [Tooltip("Flat tint multiplied into the water colour.")]
    [SerializeField] Color tint = Color.white;
    [Tooltip("Resolution each tier texture is resampled to inside the packed array.")]
    [SerializeField] int arrayResolution = 256;

    RenderTexture waterArray;                 // Texture2DArray, one slice per tier (sorted by speed)
    readonly List<WaterFlowTier> sorted = new();

    static readonly int WaterArray = Shader.PropertyToID("_WaterArray");
    static readonly int DualScale = Shader.PropertyToID("_DualScale");
    static readonly int DetailTiling = Shader.PropertyToID("_DetailTiling");
    static readonly int DetailSpeed = Shader.PropertyToID("_DetailSpeed");
    static readonly int Color_ = Shader.PropertyToID("_Color");
    static readonly int TierSpeed = Shader.PropertyToID("_TierSpeed");
    static readonly int TierTiling = Shader.PropertyToID("_TierTiling");
    static readonly int TierStrength = Shader.PropertyToID("_TierStrength");
    static readonly int TierFlowSpeed = Shader.PropertyToID("_TierFlowSpeed");
    static readonly int TierCount = Shader.PropertyToID("_TierCount");

    protected override void OnEnable() {
        style.Bake();
        BakeArray();
        base.OnEnable(); // subscribes + binds; the bind pushes everything via ConfigurePropertyBlock
    }

    protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
        style.Apply(block);
        if (waterArray != null) block.SetTexture(WaterArray, waterArray);
        block.SetFloat(DualScale, dualScale ? 1f : 0f);
        block.SetFloat(DetailTiling, detailTiling);
        block.SetFloat(DetailSpeed, detailSpeed);
        block.SetColor(Color_, tint);

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
        style.Bake();
        BakeArray();
        base.OnValidate();
    }
#endif

    void OnDestroy() {
        style?.Dispose();
        ReleaseArray();
    }

    // Sort the tiers by speed (into `sorted`, capped at MaxTiers) and pack their textures into the Texture2DArray.
    // Slice order matches the sorted order, so it lines up with the float[] uniforms pushed in ConfigurePropertyBlock.
    void BakeArray() {
        sorted.Clear();
        if (tiers != null) sorted.AddRange(tiers);
        if (sorted.Count == 0) sorted.Add(new WaterFlowTier { speed = 0f, tiling = 4f, flowStrength = 0.3f, flowSpeed = 1f });
        sorted.Sort((a, b) => a.speed.CompareTo(b.speed));
        if (sorted.Count > MaxTiers) sorted.RemoveRange(MaxTiers, sorted.Count - MaxTiers);

        int count = sorted.Count;
        int size = Mathf.Clamp(arrayResolution, 8, 2048);
        if (waterArray == null || waterArray.volumeDepth != count || waterArray.width != size) {
            ReleaseArray();
            waterArray = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32) {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = count,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            waterArray.Create();
        }
        for (int i = 0; i < count; i++) {
            var tex = sorted[i].texture != null ? sorted[i].texture : Texture2D.whiteTexture;
            Graphics.Blit(tex, waterArray, 0, i);   // (source, dest, sourceDepthSlice, destDepthSlice)
        }
    }

    void ReleaseArray() {
        if (waterArray == null) return;
        waterArray.Release();
        VectorFieldObjectUtils.DestroyAutomatic(waterArray);
        waterArray = null;
    }

    static List<WaterFlowTier> DefaultTiers() => new() {
        new WaterFlowTier { speed = 0f, tiling = 4f, flowStrength = 0.3f, flowSpeed = 1f },   // calm
        new WaterFlowTier { speed = 1f, tiling = 4f, flowStrength = 0.5f, flowSpeed = 1.5f }, // choppy
    };

    static VectorFieldFlowStyle MakeUntintedStyle() {
        var s = new VectorFieldFlowStyle();
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        s.amplitudeColor = g;
        return s;
    }
}
