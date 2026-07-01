using UnityEngine;

// PROTOTYPE — drives the Image-Based Flow Visualization (IBFV) look (van Wijk 2002) over a VectorFieldComponent.
//
// IBFV is a SEPARATE aesthetic from the sand-ripple flow shader: seam-free, flowing, but blurry/directional rather
// than sandy. It works by a feedback loop — each frame it advects the previous accumulation buffer along the flow and
// blends in a little fresh noise (see VectorFieldFlowIBFV.shader). That loop needs ping-pong render textures, which is
// what this component manages. Put it on a quad (MeshRenderer + MeshFilter), point it at a field, hit play.
//
// Status: prototype / exploration, not a finished feature. Compare it against the sand shader's Mode 1 before deciding
// whether it's worth productionising. See FLOW_VISUALIZATION_NOTES.md.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Renderers/Flow (IBFV)")]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class VectorFieldFlowIBFV : MonoBehaviour {
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int FieldTex = Shader.PropertyToID("_FieldTex");
    static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
    static readonly int NoisePhase = Shader.PropertyToID("_NoisePhase");

    [SerializeField] VectorFieldComponent vectorFieldComponent;

    // Material using "Vector Fields/Vector Field Flow IBFV". Auto-created if left empty.
    [SerializeField] Material ibfvMaterial;
    // Noise injected each frame. Auto-generated white noise if left empty.
    [SerializeField] Texture2D noiseTexture;

    [SerializeField] Vector2Int resolution = new Vector2Int(512, 512);
    [SerializeField] float noiseScrollSpeed = 0.25f;
    // Shifts the display quad along the field plane normal (draw-order control), like VectorFieldTextureRenderer.
    [SerializeField] float depthOffset;

    RenderTexture bufferA, bufferB;
    bool readFromA = true;
    Vector2 noisePhase;
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
        if (ibfvMaterial != null && ibfvMaterial.hideFlags == HideFlags.HideAndDontSave) ObjectX.DestroyAutomatic(ibfvMaterial);
        if (noiseTexture != null && noiseTexture.hideFlags == HideFlags.HideAndDontSave) ObjectX.DestroyAutomatic(noiseTexture);
    }

    void EnsureResources() {
        if (ibfvMaterial == null) {
            var shader = Shader.Find("Vector Fields/Vector Field Flow IBFV");
            if (shader != null) ibfvMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
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
        ObjectX.DestroyAutomatic(rt);
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
        noisePhase += new Vector2(1.0f, 0.61f) * (noiseScrollSpeed * dt);

        var src = readFromA ? bufferA : bufferB;
        var dst = readFromA ? bufferB : bufferA;

        ibfvMaterial.SetTexture(FieldTex, field);
        ibfvMaterial.SetTexture(NoiseTex, noiseTexture);
        ibfvMaterial.SetVector(NoisePhase, new Vector4(noisePhase.x, noisePhase.y, 0, 0));
        Graphics.Blit(src, dst, ibfvMaterial);          // _MainTex = src (previous accumulation)
        readFromA = !readFromA;

        // Show the new accumulation on the mesh. The renderer's material should be an unlit textured material; we only
        // override its _MainTex per-instance via the property block.
        propertyBlock ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture(MainTex, dst);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    // Lay the quad over the field's world rect (same approach as VectorFieldTextureRenderer).
    void MatchFieldBounds() {
        var bounds = vectorFieldComponent.GetBounds();
        transform.position = bounds.center + vectorFieldComponent.planeNormal * depthOffset;

        var worldSize = new Vector3(bounds.size.x, bounds.size.y, 1);
        var parent = transform.parent;
        if (parent == null) {
            transform.localScale = worldSize;
        } else {
            var s = parent.lossyScale;
            transform.localScale = new Vector3(
                s.x != 0 ? worldSize.x / s.x : worldSize.x,
                s.y != 0 ? worldSize.y / s.y : worldSize.y,
                s.z != 0 ? worldSize.z / s.z : worldSize.z);
        }
    }

    static Texture2D CreateWhiteNoise(int size) {
        var tex = new Texture2D(size, size, TextureFormat.R8, false, true) {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var px = new Color32[size * size];
        // Deterministic hash so it doesn't rely on Random state (and is stable across edit-mode repaints).
        for (int i = 0; i < px.Length; i++) {
            uint h = (uint)(i * 2654435761u);
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            byte v = (byte)(h & 0xFF);
            px[i] = new Color32(v, v, v, 255);
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
