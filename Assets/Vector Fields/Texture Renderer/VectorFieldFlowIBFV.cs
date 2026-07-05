using UnityEngine;

// PROTOTYPE — drives the Image-Based Flow Visualization (IBFV) look (van Wijk 2002) over a VectorFieldComponent.
//
// IBFV is a SEPARATE aesthetic from the Flow-Aligned Texture shader: seam-free, flowing, but blurry/directional rather
// than sandy. It works by a feedback loop — each frame it advects the previous accumulation buffer along the flow and
// blends in a little fresh noise (see VectorFieldFlowIBFV.shader). That loop needs ping-pong render textures, which is
// what this component manages. Put it on a quad (MeshRenderer + MeshFilter), point it at a field, hit play.
//
// Status: prototype / exploration, not a finished feature. Compare it against the Flow-Aligned Texture Mode 1 before deciding
// whether it's worth productionising. See FLOW_ALIGNED_NOTES.md.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow (IBFV)")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class VectorFieldFlowIBFV : MonoBehaviour {
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int FieldTex = Shader.PropertyToID("_FieldTex");
    static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int NoisePhase = Shader.PropertyToID("_NoisePhase");
    static readonly int FlowStep = Shader.PropertyToID("_FlowStep");
    static readonly int NoiseAmount = Shader.PropertyToID("_NoiseAmount");
    static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
    static readonly int NoiseRate = Shader.PropertyToID("_NoiseRate");

    [SerializeField] VectorFieldComponent vectorFieldComponent;

    // Material using "Vector Fields/Vector Field Flow IBFV". Auto-created if left empty.
    [SerializeField] Material ibfvMaterial;
    // Noise injected each frame. Auto-generated white noise if left empty.
    [SerializeField] Texture2D noiseTexture;

    [SerializeField] Vector2Int resolution = new Vector2Int(512, 512);

    [Header("Look (pushed to the material each frame)")]
    [Tooltip("How far the feedback buffer is advected along the flow each frame, in UV units. Bigger = faster/longer streaks.")]
    [SerializeField] float flowStep = 0.02f;
    [Tooltip("Fraction of fresh noise injected each frame. Lower = longer-lived streaks; too low blurs to grey.")]
    [Range(0f, 1f)][SerializeField] float noiseAmount = 0.08f;
    [Tooltip("Tiling of the injection noise across the quad. Higher = finer streaks.")]
    [SerializeField] float noiseScale = 6f;
    [Tooltip("Twinkle speed (cycles/sec). Each noise texel pulses on its own phase so spots persist a few frames — the " +
        "coherence that lets advection draw them into streaks. Too slow = streaks wrap; too fast = static noise.")]
    [SerializeField] float noiseRate = 1.5f;

    // Shifts the display quad along the field plane normal (draw-order control), like VectorFieldTextureRenderer.
    [SerializeField] float depthOffset;

    RenderTexture bufferA, bufferB;
    bool readFromA = true;
    float elapsed;   // drives the noise twinkle (passed to the shader as _NoisePhase.x)
    float lastTime;

    MeshRenderer meshRenderer => GetComponent<MeshRenderer>();
    MaterialPropertyBlock propertyBlock;

    void OnEnable() {
        lastTime = Now();
        EnsureResources();
    }

    void OnDisable() {
        ReleaseBuffer(ref bufferA);
        ReleaseBuffer(ref bufferB);
    }

    void OnDestroy() {
        if (ibfvMaterial != null && ibfvMaterial.hideFlags == HideFlags.HideAndDontSave) VectorFieldObjectUtils.DestroyAutomatic(ibfvMaterial);
        if (noiseTexture != null && noiseTexture.hideFlags == HideFlags.HideAndDontSave) VectorFieldObjectUtils.DestroyAutomatic(noiseTexture);
    }

    void EnsureResources() {
        VectorFieldRendererUtils.GetOrCreateMaterial(ref ibfvMaterial, "Vector Fields/Vector Field Flow IBFV", hideAndDontSave: true);
        if (noiseTexture == null) noiseTexture = CreateWhiteNoise(256);
        EnsureBuffer(ref bufferA);
        EnsureBuffer(ref bufferB);
    }

    void EnsureBuffer(ref RenderTexture rt) {
        if (rt != null && (rt.width != resolution.x || rt.height != resolution.y)) ReleaseBuffer(ref rt);
        if (rt == null) {
            rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();
        }
    }

    void ReleaseBuffer(ref RenderTexture rt) {
        if (rt == null) return;
        rt.Release();
        VectorFieldObjectUtils.DestroyAutomatic(rt);
        rt = null;
    }

    void LateUpdate() {
        if (vectorFieldComponent == null || ibfvMaterial == null) return;
        var field = vectorFieldComponent.renderTexture;
        if (field == null) return; // nothing rendered yet

        EnsureResources();
        MatchFieldBounds();

        float now = Now();
        float dt = Mathf.Clamp(now - lastTime, 0f, 0.1f);
        lastTime = now;
        elapsed += dt;

        var src = readFromA ? bufferA : bufferB;
        var dst = readFromA ? bufferB : bufferA;

        ibfvMaterial.SetTexture(FieldTex, field);
        ibfvMaterial.SetTexture(NoiseTex, noiseTexture);
        ibfvMaterial.SetVector(NoisePhase, new Vector4(elapsed, 0, 0, 0));
        ibfvMaterial.SetFloat(FlowStep, flowStep);
        ibfvMaterial.SetFloat(NoiseAmount, noiseAmount);
        ibfvMaterial.SetFloat(NoiseScale, noiseScale);
        ibfvMaterial.SetFloat(NoiseRate, noiseRate);
        Graphics.Blit(src, dst, ibfvMaterial);          // _MainTex = src (previous accumulation)
        readFromA = !readFromA;

        // Show the new accumulation on the mesh. The renderer's material should be an unlit textured material; we only
        // override its _MainTex per-instance via the property block.
        VectorFieldRendererUtils.SetRendererTexture(meshRenderer, ref propertyBlock, MainTex, dst);
    }

    // Lay the quad over the field's world rect — shared with the other field renderers.
    void MatchFieldBounds() {
        VectorFieldRendererUtils.MatchFieldRect(transform, vectorFieldComponent, depthOffset);
    }

    static Texture2D CreateWhiteNoise(int size) {
        // RGBA32, not R8: the shader reads noise as .rgb, and we write a grey (v,v,v) below. An R8 texture would drop
        // G/B, so the injected noise would be pure red and the accumulation would converge to red instead of grey.
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var px = new Color32[size * size];
        // Deterministic hash so it doesn't rely on Random state (and is stable across edit-mode repaints).
        // R = per-texel noise value; G = per-texel temporal phase for the twinkle (independent bits of the same hash).
        for (int i = 0; i < px.Length; i++) {
            uint h = (uint)(i * 2654435761u);
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            byte value = (byte)(h & 0xFF);
            byte phase = (byte)((h >> 8) & 0xFF);
            px[i] = new Color32(value, phase, 0, 255);
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // Time source that also advances in edit mode (Time.time doesn't tick reliably outside play).
    static float Now() => Application.isPlaying ? Time.time : (float)UnityEditor_TimeShim();

    static double UnityEditor_TimeShim() {
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.realtimeSinceStartupAsDouble;
#endif
    }
}
