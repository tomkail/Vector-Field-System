using UnityEngine;

// Colour smoke that RIDES a vector field. The vector field (e.g. the fluid sim, or any VectorFieldComponent) provides
// the air currents; this is a separate simulation whose density is advected BY that field — a passive scalar. One-way
// coupling: the flow pushes the smoke, the smoke doesn't affect the flow.
//
// The emission source is painted through the SAME generic brush/stroke core the vector field uses: this component is an
// IPaintTarget<Color>, so ColorPainting.BeginStroke paints a ColorMap of "smoke to release". Each fixed step the sim
// injects that source into the GPU density, advects the density along the velocity field, and dissipates it. The
// painted source fades over time so emission is transient (a trail), not permanent.
//
// Renders the density on a world plane aligned to the grid.
[ExecuteAlways, RequireComponent(typeof(GridRenderer))]
public class SmokeSimulationComponent : MonoBehaviour, IPaintTarget<Color> {

    static ComputeShader smokeComputeShader;
    static ComputeShader SmokeComputeShader => smokeComputeShader ? smokeComputeShader : (smokeComputeShader = Resources.Load<ComputeShader>("SmokeSimulation"));

    [Header("Velocity source")]
    [Tooltip("The vector field whose currents carry the smoke (e.g. the fluid sim). Null = smoke just sits and fades.")]
    public VectorFieldComponent velocitySource;
    [Tooltip("How fast the smoke rides the flow, independent of grid resolution (calibrated in cells/second at a 64-cell " +
        "reference). Tune with the velocity source's scale so smoke moves with the flow.")]
    public float velocityScale = 8f;

    [Header("Simulation")]
    [Tooltip("Fixed solver rate. The sim steps in increments of 1/this regardless of frame rate.")]
    public float simulationFps = 60f;
    [Tooltip("Cap on solver steps per frame, so a hitch can't spiral into a catch-up death loop.")]
    public int maxSubstepsPerFrame = 4;
    [Tooltip("Simulated seconds per real second — how fast the smoke evolves, independent of step rate.")]
    public float timeScale = 1f;
    [Range(0f, 1f), Tooltip("Density retained per second (1 = never fades, lower = smoke thins out).")]
    public float dissipationPerSecond = 0.6f;

    [Header("Emission (painting)")]
    [Tooltip("How much of the painted source is released into the density per second.")]
    public float injectRate = 4f;
    [Range(0f, 1f), Tooltip("Painted-source retained per second — how quickly a painted trail stops emitting once you " +
        "move on (lower = shorter puffs).")]
    public float sourceRetainPerSecond = 0.1f;

    [Header("Rendering")]
    public Shader renderShader;
    [ColorUsage(true, true)] public Color tint = Color.white;
    [Tooltip("Multiplies density into opacity when rendering.")]
    public float opacity = 1f;

    GridRenderer _gridRenderer;
    public GridRenderer gridRenderer => _gridRenderer != null ? _gridRenderer : (_gridRenderer = GetComponent<GridRenderer>());

    // GPU density ping-pong (RGBA, raw). Advected on the GPU.
    RenderTexture densityA, densityB;
    Point allocatedSize = new Point(-1, -1);

    // Emission source, painted by strokes (IPaintTarget<Color>.PaintField). The brush writes the CPU ColorMap (the
    // last-painted value per cell); the GPU RenderTexture is the live source the sim reads — it fades on the GPU and
    // only the freshly-painted brush region is copied CPU->GPU each frame (see Update). Keeping it GPU-side is what
    // makes painting on a large grid cheap: no full-grid CPU fade or upload, ever.
    ColorMap injectionMap;
    RenderTexture injectionRT;
    RectInt? pendingDirty;      // brush region painted since the last upload (grid coords); null = nothing to upload
    bool everPainted;           // once anything's been painted, keep fading/injecting the source
    Texture2D sourcePatchTex;   // brush footprint staged CPU->GPU, then composited into injectionRT
    Color[] regionColors;       // reused scratch for the region upload, grown to the largest region seen

    float accumulator;
    int kInject, kAdvect, kFadeSource, kCompositeSource;
    bool kernelsResolved;

    Material renderMaterial;
    Mesh quad;

    const int ThreadsX = 8, ThreadsY = 8;

    // Cached shader property IDs — SetInt/SetFloat/SetTexture with a string re-hashes it via Shader.PropertyToID every
    // call (visible in the profiler across the per-frame dispatches). Resolve once.
    static readonly int ID_width = Shader.PropertyToID("width");
    static readonly int ID_height = Shader.PropertyToID("height");
    static readonly int ID_dt = Shader.PropertyToID("dt");
    static readonly int ID_dissipation = Shader.PropertyToID("dissipation");
    static readonly int ID_injectRate = Shader.PropertyToID("injectRate");
    static readonly int ID_velocityScale = Shader.PropertyToID("velocityScale");
    static readonly int ID_sourceRetain = Shader.PropertyToID("sourceRetain");
    static readonly int ID_Density = Shader.PropertyToID("Density");
    static readonly int ID_Injection = Shader.PropertyToID("Injection");
    static readonly int ID_Velocity = Shader.PropertyToID("Velocity");
    static readonly int ID_DensityIn = Shader.PropertyToID("DensityIn");
    static readonly int ID_DensityOut = Shader.PropertyToID("DensityOut");
    static readonly int ID_Source = Shader.PropertyToID("Source");
    static readonly int ID_SourceRW = Shader.PropertyToID("SourceRW");
    static readonly int ID_SourcePatch = Shader.PropertyToID("SourcePatch");
    static readonly int ID_patchX = Shader.PropertyToID("patchX");
    static readonly int ID_patchY = Shader.PropertyToID("patchY");
    static readonly int ID_patchW = Shader.PropertyToID("patchW");
    static readonly int ID_patchH = Shader.PropertyToID("patchH");

    // velocityScale is expressed in cells/second at this reference grid resolution, then normalised before it reaches
    // the shader. That keeps the knob's value meaningful and, crucially, makes the smoke's motion independent of the
    // actual grid resolution — a bigger grid is just sharper, not slower.
    const float ReferenceResolution = 64f;

    // --- IPaintTarget<Color> ---------------------------------------------------------------------------------------
    public TypeMap<Color> PaintField {
        get { EnsureInjectionMap(); return injectionMap; }
    }
    public TypeMap<Color> CreateMap(Point size) => new ColorMap(size);

    // The stroke reports the grid rect it just painted; accumulate the union so Update uploads exactly that (and no
    // more) to the GPU source. Only the freshly-painted footprint crosses CPU->GPU — never the whole field.
    public void MarkRegionDirty(RectInt region) {
        pendingDirty = pendingDirty.HasValue ? RectIntUnion(pendingDirty.Value, region) : region;
        everPainted = true;
    }

    static RectInt RectIntUnion(RectInt a, RectInt b) {
        int xMin = Mathf.Min(a.xMin, b.xMin), yMin = Mathf.Min(a.yMin, b.yMin);
        int xMax = Mathf.Max(a.xMax, b.xMax), yMax = Mathf.Max(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    void EnsureInjectionMap() {
        var size = gridRenderer.gridSize;
        if (injectionMap == null || injectionMap.size != size)
            injectionMap = new ColorMap(size);
    }

    // --- lifecycle -------------------------------------------------------------------------------------------------
    void OnEnable() {
        ConfigureGrid();
        ResolveKernels();
        EnsureTextures();
    }

    // Unlike VectorFieldComponent (which sets its GridRenderer up in OnValidate), this component isn't a
    // VectorFieldComponent, so nothing configures its required GridRenderer. Do it here — otherwise the mode module is
    // null (cellCenter.gridToWorldMatrix NREs) and gridSize is (0,0) so the density textures allocate at 0×0. Matches
    // the vector-field setup so smoke and fluid share the same grid convention. Runs at runtime too, so a built player
    // (no OnValidate) is covered.
    void ConfigureGrid() {
        var gr = gridRenderer;
        if (gr.modeModule is not GridRendererManhattanModeModule)
            gr.modeModule = ScriptableObject.CreateInstance<GridRendererManhattanModeModule>();
        gr.scaleWithGridSize = false;
        if (gr.gridSize == Point.zero) gr.gridSize = new Point(64, 64);
    }

    void OnDisable() {
        ReleaseTextures();
        if (sourcePatchTex != null) { ObjectX.DestroyAutomatic(sourcePatchTex); sourcePatchTex = null; }
        if (renderMaterial != null) { ObjectX.DestroyAutomatic(renderMaterial); renderMaterial = null; }
        if (quad != null) { ObjectX.DestroyAutomatic(quad); quad = null; }
    }

    void Update() {
        EnsureTextures();
        if (!Application.isPlaying) { Render(); return; }   // sim runs in play mode; still show the last state in edit

        // Fade the GPU source (frame-rate independent), then patch in this frame's freshly-painted brush region over
        // the top so under-brush cells stay full while the trail behind decays. Both are cheap: the fade is a GPU
        // dispatch, the upload transfers only the brush footprint.
        FadeSource(Time.deltaTime);
        UploadDirtyRegion();

        float fixedDt = 1f / Mathf.Max(1f, simulationFps);
        float simDt = fixedDt * timeScale;
        int steps = 0;
        accumulator += Time.deltaTime;
        while (accumulator >= fixedDt && steps < maxSubstepsPerFrame) {
            Step(simDt);
            accumulator -= fixedDt;
            steps++;
        }
        if (steps == maxSubstepsPerFrame) accumulator = 0f;

        Render();
    }

    // --- simulation ------------------------------------------------------------------------------------------------
    void Step(float dt) {
        var cs = SmokeComputeShader;
        int w = gridRenderer.gridSize.x, h = gridRenderer.gridSize.y;
        cs.SetInt(ID_width, w);
        cs.SetInt(ID_height, h);
        cs.SetFloat(ID_dt, dt);
        cs.SetFloat(ID_dissipation, Mathf.Pow(Mathf.Clamp01(dissipationPerSecond), dt));

        // 1) Inject the painted source into the density (in place on densityA).
        if (everPainted && injectionRT != null) {
            cs.SetFloat(ID_injectRate, injectRate);
            cs.SetTexture(kInject, ID_Density, densityA);
            cs.SetTexture(kInject, ID_Injection, injectionRT);
            Dispatch(kInject, w, h);
        }

        // 2) Advect the density along the velocity field. densityA -> densityB, then swap.
        // The shader back-traces in normalised space, so hand it a normalised velocity scale: velocityScale is
        // calibrated in cells/second at ReferenceResolution, divided out here so the *same* velocityScale produces the
        // same motion at any grid resolution (resolution stays a purely visual knob).
        bool hasVel = velocitySource != null && velocitySource.renderTexture != null;
        cs.SetFloat(ID_velocityScale, hasVel ? velocityScale / ReferenceResolution : 0f);   // 0 => stays put (no motion)
        cs.SetTexture(kAdvect, ID_Velocity, hasVel ? velocitySource.renderTexture : (Texture)Texture2D.blackTexture);
        cs.SetTexture(kAdvect, ID_DensityIn, densityA);
        cs.SetTexture(kAdvect, ID_DensityOut, densityB);
        Dispatch(kAdvect, w, h);
        (densityA, densityB) = (densityB, densityA);
    }

    // Decay the whole GPU source by a per-second retain (frame-rate independent). The trail keeps fading each frame; the
    // brush region is re-patched to full right after (see UploadDirtyRegion), so only cells the brush has moved off of
    // actually fade. A single cheap dispatch — no per-cell CPU work regardless of grid size.
    void FadeSource(float deltaTime) {
        if (!everPainted || injectionRT == null) return;
        var cs = SmokeComputeShader;
        int w = gridRenderer.gridSize.x, h = gridRenderer.gridSize.y;
        cs.SetInt(ID_width, w);
        cs.SetInt(ID_height, h);
        cs.SetFloat(ID_sourceRetain, Mathf.Pow(Mathf.Clamp01(sourceRetainPerSecond), deltaTime));
        cs.SetTexture(kFadeSource, ID_Source, injectionRT);
        Dispatch(kFadeSource, w, h);
    }

    // Stamp just the painted brush footprint into the GPU source, then clear it from the CPU map. Region-only transfer,
    // so cost scales with the brush, not the grid. The composite writes only cells the brush actually painted (see the
    // CompositeSource kernel) — so overlapping the rect with existing smoke or the box corners never re-stamps a square.
    // Clearing the CPU region afterwards keeps the map holding only *this* frame's paint, so a later, overlapping upload
    // can't re-assert stale (un-faded) values over the GPU's fading source.
    void UploadDirtyRegion() {
        if (!pendingDirty.HasValue || injectionMap == null || injectionRT == null) return;
        var size = gridRenderer.gridSize;
        int x0 = Mathf.Clamp(pendingDirty.Value.xMin, 0, size.x);
        int y0 = Mathf.Clamp(pendingDirty.Value.yMin, 0, size.y);
        int x1 = Mathf.Clamp(pendingDirty.Value.xMax, 0, size.x);
        int y1 = Mathf.Clamp(pendingDirty.Value.yMax, 0, size.y);
        pendingDirty = null;
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return;

        EnsurePatchTex(w, h);
        int count = w * h;
        if (regionColors == null || regionColors.Length != count) regionColors = new Color[count];   // SetPixels needs an exact-sized block
        for (int ry = 0; ry < h; ry++)
            for (int rx = 0; rx < w; rx++) {
                int i = ry * w + rx;
                regionColors[i] = injectionMap.GetValueAtGridPoint(x0 + rx, y0 + ry);
                injectionMap.SetValueAtGridPoint(x0 + rx, y0 + ry, Color.clear);   // consumed — don't re-upload later
            }
        // Stage into the patch texture (only the region travels CPU->GPU), then composite it into the source on the GPU.
        sourcePatchTex.SetPixels(0, 0, w, h, regionColors);
        sourcePatchTex.Apply(false);

        var cs = SmokeComputeShader;
        cs.SetTexture(kCompositeSource, ID_SourceRW, injectionRT);
        cs.SetTexture(kCompositeSource, ID_SourcePatch, sourcePatchTex);
        cs.SetInt(ID_patchX, x0);
        cs.SetInt(ID_patchY, y0);
        cs.SetInt(ID_patchW, w);
        cs.SetInt(ID_patchH, h);
        Dispatch(kCompositeSource, w, h);
    }

    // Patch staging texture, sized to the largest region seen and reused (grown monotonically) so painting doesn't churn
    // textures. RGBAFloat so it round-trips the painted colour without precision loss.
    void EnsurePatchTex(int w, int h) {
        if (sourcePatchTex != null && sourcePatchTex.width >= w && sourcePatchTex.height >= h) return;
        int sw = sourcePatchTex != null ? Mathf.Max(w, sourcePatchTex.width) : w;
        int sh = sourcePatchTex != null ? Mathf.Max(h, sourcePatchTex.height) : h;
        if (sourcePatchTex != null) ObjectX.DestroyAutomatic(sourcePatchTex);
        sourcePatchTex = new Texture2D(sw, sh, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
    }

    // --- rendering (world plane) -----------------------------------------------------------------------------------
    void Render() {
        if (densityA == null) return;
        EnsureRenderResources();
        renderMaterial.mainTexture = densityA;
        renderMaterial.SetColor("_Tint", tint);
        renderMaterial.SetFloat("_Opacity", opacity);

        // Unit quad (0..1) scaled across the grid and mapped to world by the grid transform.
        int w = gridRenderer.gridSize.x, h = gridRenderer.gridSize.y;
        Matrix4x4 m = gridRenderer.cellCenter.gridToWorldMatrix * Matrix4x4.Scale(new Vector3(w, h, 1f));
        Graphics.DrawMesh(quad, m, renderMaterial, gameObject.layer);
    }

    void EnsureRenderResources() {
        if (renderShader == null) renderShader = Shader.Find("VectorField/SmokeRender");
        if (renderMaterial == null && renderShader != null) renderMaterial = new Material(renderShader);
        if (quad == null) quad = BuildUnitQuad();
    }

    static Mesh BuildUnitQuad() {
        var m = new Mesh { name = "SmokeQuad" };
        m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    // --- texture lifecycle -----------------------------------------------------------------------------------------
    void EnsureTextures() {
        var size = gridRenderer.gridSize;
        if (allocatedSize == size && densityA != null) return;
        ReleaseTextures();
        densityA = NewDensityTexture(size);
        densityB = NewDensityTexture(size);
        // Source RT: RGBAFloat so a CopyTexture region from the (RGBAFloat) staging texture is a format-exact match, and
        // enableRandomWrite so the FadeSource kernel can decay it in place. Starts cleared; the region upload patches it.
        injectionRT = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        injectionRT.Create();
        pendingDirty = null;
        allocatedSize = size;
    }

    static RenderTexture NewDensityTexture(Point size) {
        var rt = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }

    void ReleaseTextures() {
        foreach (var rt in new[] { densityA, densityB, injectionRT }) {
            if (rt == null) continue;
            if (RenderTexture.active == rt) RenderTexture.active = null;
            rt.Release();
        }
        densityA = densityB = injectionRT = null;
        allocatedSize = new Point(-1, -1);
    }

    void ResolveKernels() {
        if (kernelsResolved) return;
        var cs = SmokeComputeShader;
        kInject = cs.FindKernel("Inject");
        kAdvect = cs.FindKernel("Advect");
        kFadeSource = cs.FindKernel("FadeSource");
        kCompositeSource = cs.FindKernel("CompositeSource");
        kernelsResolved = true;
    }

    void Dispatch(int kernel, int w, int h) {
        int gx = Mathf.CeilToInt((float)w / ThreadsX);
        int gy = Mathf.CeilToInt((float)h / ThreadsY);
        SmokeComputeShader.Dispatch(kernel, gx, gy, 1);
    }

    // Clear all smoke (density + painted source).
    [EasyButtons.Button]
    public void Clear() {
        if (densityA != null) { var prev = RenderTexture.active; RenderTexture.active = densityA; GL.Clear(false, true, Color.clear); RenderTexture.active = prev; }
        if (densityB != null) { var prev = RenderTexture.active; RenderTexture.active = densityB; GL.Clear(false, true, Color.clear); RenderTexture.active = prev; }
        if (injectionRT != null) { var prev = RenderTexture.active; RenderTexture.active = injectionRT; GL.Clear(false, true, Color.clear); RenderTexture.active = prev; }
        if (injectionMap != null) injectionMap.Fill(Color.clear);
        pendingDirty = null;
        everPainted = false;
    }
}
