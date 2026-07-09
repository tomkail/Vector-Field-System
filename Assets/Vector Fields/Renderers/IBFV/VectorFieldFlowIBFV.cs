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
// Quad-follows-field alignment (matchFieldBounds / depthOffset) is inherited from VectorFieldQuad. This does NOT extend
// VectorFieldTextureRenderer: that binds the field texture to the quad, whereas IBFV shows its own accumulation buffer
// and feeds the field into the blit material instead — so it shares only the alignment base, not the texture binding.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow (IBFV)")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class VectorFieldFlowIBFV : VectorFieldQuad {
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int FieldTex = Shader.PropertyToID("_FieldTex");
    static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int NoisePhase = Shader.PropertyToID("_NoisePhase");
    static readonly int FlowStep = Shader.PropertyToID("_FlowStep");
    static readonly int NoiseAmount = Shader.PropertyToID("_NoiseAmount");
    static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
    static readonly int NoiseRate = Shader.PropertyToID("_NoiseRate");

    [SerializeField] VectorFieldComponent vectorFieldComponent;
    protected override VectorFieldComponent Field => vectorFieldComponent;

    // Material using "Vector Fields/Vector Field Flow IBFV". Auto-created if left empty.
    [SerializeField] Material ibfvMaterial;
    // Present material using "Vector Fields/Vector Field Flow IBFV Present" — colours the grey buffer at display time.
    // Auto-created; assigned to the MeshRenderer each frame (overrides whatever material was on it).
    [SerializeField] Material presentMaterial;
    // Noise injected each frame. Auto-generated white noise if left empty.
    [SerializeField] Texture2D noiseTexture;

    [SerializeField] Vector2Int resolution = new Vector2Int(512, 512);

    [SerializeField] VectorFieldFlowStyle style = new VectorFieldFlowStyle();

    [Tooltip("How far the feedback buffer is advected along the flow each frame, in UV units. Bigger = faster/longer streaks.")]
    [SerializeField] float flowStep = 0.02f;
    [Tooltip("Fraction of fresh noise injected each frame. Lower = longer-lived streaks; too low blurs to grey.")]
    [Range(0f, 1f)][SerializeField] float noiseAmount = 0.08f;
    [Tooltip("Tiling of the injection noise across the quad. Higher = finer streaks.")]
    [SerializeField] float noiseScale = 6f;
    [Tooltip("Twinkle speed (cycles/sec). Each noise texel pulses on its own phase so spots persist a few frames — the " +
        "coherence that lets advection draw them into streaks. Too slow = streaks wrap; too fast = static noise.")]
    [SerializeField] float noiseRate = 1.5f;

    RenderTexture bufferA, bufferB;
    bool readFromA = true;
    float elapsed;   // drives the noise twinkle (passed to the shader as _NoisePhase.x)
    float lastTime;

    void OnEnable() {
        lastTime = Now();
        EnsureResources();
        style.Bake();
    }

#if UNITY_EDITOR
    void OnValidate() {
        style.Bake();
    }
#endif

    void OnDisable() {
        ReleaseBuffer(ref bufferA);
        ReleaseBuffer(ref bufferB);
    }

    void OnDestroy() {
        if (ibfvMaterial != null && ibfvMaterial.hideFlags == HideFlags.HideAndDontSave) VectorFieldObjectUtils.DestroyAutomatic(ibfvMaterial);
        if (presentMaterial != null && presentMaterial.hideFlags == HideFlags.HideAndDontSave) VectorFieldObjectUtils.DestroyAutomatic(presentMaterial);
        if (noiseTexture != null && noiseTexture.hideFlags == HideFlags.HideAndDontSave) VectorFieldObjectUtils.DestroyAutomatic(noiseTexture);
        style.Dispose();
    }

    void EnsureResources() {
        VectorFieldRendererUtils.GetOrCreateMaterial(ref ibfvMaterial, "Vector Fields/Vector Field Flow IBFV", hideAndDontSave: true);
        VectorFieldRendererUtils.GetOrCreateMaterial(ref presentMaterial, "Vector Fields/Vector Field Flow IBFV Present", hideAndDontSave: true);
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

    protected override void LateUpdate() {
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

        // Colour + present the new accumulation. The buffer stays grey (so the feedback loop is stable); the present
        // material applies the shared styling (contrast/colour/background) at display time. We drive the present
        // material directly (it's our own hidden instance) and force it onto the MeshRenderer, overriding whatever
        // material was assigned there.
        if (meshRenderer.sharedMaterial != presentMaterial) meshRenderer.sharedMaterial = presentMaterial;
        presentMaterial.SetTexture(MainTex, dst);
        presentMaterial.SetTexture(FieldTex, field);
        style.Apply(presentMaterial);
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
