using UnityEngine;

// Single-texture Flow Map renderer — the minimal case, built straight on VectorFieldTextureRenderer + the shared
// VectorFieldFlowStyle, driving the "Vector Fields/Flow Map/Flow Map" shader. Sits alongside the multi-tier
// TieredFlowMapRenderer; between them they exercise the shared base + core at both ends of the complexity range. No
// tiers, no Texture2DArray — just one water texture and its flow params, pushed via the property block.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow Map")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class FlowMapRenderer : VectorFieldTextureRenderer {
    [SerializeField] VectorFieldFlowStyle style = MakeUntintedStyle();

    [Tooltip("The water/caustics image scrolled along the flow. Leave empty to keep the material's assigned texture.")]
    [SerializeField] Texture waterTexture;
    [Tooltip("Tiling of the water texture across the quad.")]
    [SerializeField] float tiling = 4f;
    [Tooltip("How far UVs push per cycle — apparent turbulence/distortion.")]
    [Range(0f, 2f)] [SerializeField] float flowStrength = 0.3f;
    [Tooltip("How fast the ping-pong flow cycle runs.")]
    [Range(0f, 4f)] [SerializeField] float flowSpeed = 1f;
    [Tooltip("Add a second layer at a different scale/speed to hide the single-tiling repetition.")]
    [SerializeField] bool dualScale = true;
    [SerializeField] float detailTiling = 2.17f;
    [SerializeField] float detailSpeed = 1.7f;
    [Tooltip("Flat tint multiplied into the water colour.")]
    [SerializeField] Color tint = Color.white;

    static readonly int WaterTex = Shader.PropertyToID("_WaterTex");
    static readonly int Tiling = Shader.PropertyToID("_Tiling");
    static readonly int FlowStrength = Shader.PropertyToID("_FlowStrength");
    static readonly int FlowSpeed = Shader.PropertyToID("_FlowSpeed");
    static readonly int DualScale = Shader.PropertyToID("_DualScale");
    static readonly int DetailTiling = Shader.PropertyToID("_DetailTiling");
    static readonly int DetailSpeed = Shader.PropertyToID("_DetailSpeed");
    static readonly int Color_ = Shader.PropertyToID("_Color");

    protected override void OnEnable() {
        style.Bake();
        base.OnEnable();
    }

    protected override void ConfigurePropertyBlock(MaterialPropertyBlock block) {
        style.Apply(block);
        if (waterTexture != null) block.SetTexture(WaterTex, waterTexture); // empty = keep the material's texture
        block.SetFloat(Tiling, tiling);
        block.SetFloat(FlowStrength, flowStrength);
        block.SetFloat(FlowSpeed, flowSpeed);
        block.SetFloat(DualScale, dualScale ? 1f : 0f);
        block.SetFloat(DetailTiling, detailTiling);
        block.SetFloat(DetailSpeed, detailSpeed);
        block.SetColor(Color_, tint);
    }

#if UNITY_EDITOR
    protected override void OnValidate() {
        style.Bake();
        base.OnValidate();
    }
#endif

    void OnDestroy() {
        style?.Dispose();
    }

    // White gradient by default so the water shows untinted out of the box (the shared style default is black→white).
    static VectorFieldFlowStyle MakeUntintedStyle() {
        var s = new VectorFieldFlowStyle();
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        s.amplitudeColor = g;
        return s;
    }
}
