using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;

// VectorFieldDirectionMode (FollowStroke / FixedAngle) now lives with the runtime brush in Brush/VectorFieldBrush.cs,
// so the tool and the runtime painting API share it.

public class VectorFieldDrawingToolSettings : SerializedScriptableSingleton<VectorFieldDrawingToolSettings> {
    // The emitter (directional/spot) that the brush stamps. Shape comes from the cookie below.
    public VectorFieldBrushSettings brushSettings = new VectorFieldBrushSettings();

    public VectorFieldDirectionMode directionMode = VectorFieldDirectionMode.FollowStroke;

    // The active brush op, by IVectorFieldBrushOp.Id. Selected persistently in the overlay; a held modifier can
    // temporarily override it (see OnToolGUI). Stored by id (not enum) so the op set can grow without breaking
    // serialization; unknown ids resolve to the default op.
    public string brushOpId = "draw";

    // The brush's shape/softness. Defaults to a soft radial Falloff so brushes are round out of the box — the
    // stamp shader has no inherent falloff (strength == magnitude * cookie.r), so None gives a hard square.
    public VectorFieldCookieSource brushCookie = new VectorFieldCookieSource { mode = VectorFieldCookieSource.Mode.Falloff };

    public float gridSpaceBrushSize = 5;
    public float pressure = 1;
}


[EditorTool("Vector Field Tool", typeof(DrawableVectorFieldComponent))]
public class VectorFieldDrawingTool : EditorTool, IDrawSelectedHandles {
    
    private VectorFieldDrawingToolSettingsOverlay m_Overlay;

    // The currently-activated instance, so static [Shortcut] handlers can drive the live tool.
    static VectorFieldDrawingTool s_Active;

    VectorFieldDrawingToolSettings settings => VectorFieldDrawingToolSettings.Instance;
    
    private double lastTime;
    DrawableVectorFieldComponent vectorFieldManager => target as DrawableVectorFieldComponent;

    // The cookie-shaped emitter map (built in OnBrushSettingsChange), wrapped as a VectorFieldBrushShape.FromMap so the
    // tool paints through the same runtime API as gameplay.
    Vector2Map brushMap;

    // The in-progress paint stroke (a drag). Null between strokes.
    VectorFieldStroke activeStroke;

    // Persisted in the settings singleton so they survive tool re-activation and domain reloads.
    public float pressure {
        get => settings.pressure;
        set => settings.pressure = value;
    }
    public float gridSpaceBrushSize {
        get => settings.gridSpaceBrushSize;
        set => settings.gridSpaceBrushSize = value;
    }
    public IVectorFieldBrushOp brushOp {
        get => VectorFieldBrushOpRegistry.ById(settings.brushOpId);
        set {
            settings.brushOpId = value.Id;
            VectorFieldDrawingToolSettings.Save();
        }
    }
    public VectorFieldDirectionMode directionMode {
        get => settings.directionMode;
        set {
            settings.directionMode = value;
            VectorFieldDrawingToolSettings.Save();
        }
    }

    // Resolution of the generated brush map (the cookie-shaped stamp readback). Independent of the field grid.
    const int brushResolution = 32;

    // The GPU texture the cookie-shaped stamp is rendered into (then read back into brushMap for painting). Owned by
    // this tool; created on demand and destroyed on deactivation.
    public RenderTexture brushTexture;

    // The cookie's grayscale mask (the brush shape), for the overlay preview. The falloff shader writes
    // float4(f,f,f,1), so it displays black-and-white. White (full strength) for cookie mode None.
    public Texture cookiePreview {
        get {
            var mask = settings.brushCookie != null ? settings.brushCookie.Resolve(new Vector2Int(brushResolution, brushResolution)) : null;
            return mask != null ? mask : Texture2D.whiteTexture;
        }
    }


    // The second "context" argument accepts an EditorWindow type.
    [Shortcut("Activate DrawableVectorFieldComponent Tool", typeof(SceneView), KeyCode.P)]
    static void DrawableVectorFieldComponentToolShortcut()
    {
        if (Selection.GetFiltered<DrawableVectorFieldComponent>(SelectionMode.TopLevel).Length > 0)
            ToolManager.SetActiveTool<VectorFieldDrawingTool>();
        else if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Select a Drawable Vector Field to paint"));
    }
    
    // Cycle through the registered brush ops while the tool is active.
    [Shortcut("Vector Field Tool/Cycle Paint Mode", typeof(SceneView), KeyCode.M)]
    static void CyclePaintModeShortcut() {
        if (s_Active == null) return;
        var ops = VectorFieldBrushOpRegistry.Ops;
        int next = (VectorFieldBrushOpRegistry.IndexOf(s_Active.brushOp.Id) + 1) % ops.Count;
        s_Active.brushOp = ops[next];
        s_Active.m_Overlay?.SyncFromTool();
        if (SceneView.lastActiveSceneView != null) {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Paint mode: " + s_Active.brushOp.DisplayName));
            SceneView.lastActiveSceneView.Repaint();
        }
    }

    // Global tools (tools that do not specify a target type in the attribute) are lazy initialized and persisted by
    // a ToolManager. Component tools (like this example) are instantiated and destroyed with the current selection.
    void OnEnable()
    {
    }

    void OnDisable() {
    }

    // Called when the active tool is set to this tool instance. Global tools are persisted by the ToolManager,
    // so usually you would use OnEnable and OnDisable to manage native resources, and OnActivated/OnWillBeDeactivated
    // to set up state. See also `EditorTools.{ activeToolChanged, activeToolChanged }` events.
    public override void OnActivated() {
        s_Active = this;
        OnBrushSettingsChange();

        m_Overlay = new VectorFieldDrawingToolSettingsOverlay();
        m_Overlay.Init(this);
        SceneView.AddOverlayToActiveView(m_Overlay);
    }

    // Called before the active tool is changed, or destroyed. The exception to this rule is if you have manually
    // destroyed this tool (ex, calling `Destroy(this)` will skip the OnWillBeDeactivated invocation).
    public override void OnWillBeDeactivated() {
        if (s_Active == this) s_Active = null;
        VectorFieldRenderTextureUtils.Destroy(ref brushTexture);

        // Release the cookie's generated mask texture (rebuilt on demand the next time the tool activates).
        settings.brushCookie?.Dispose();

        SceneView.RemoveOverlayFromActiveView(m_Overlay);
    }

    
    public void OnBrushSettingsChange() {
        // Build the brush: a directional/spot emitter (brushSettings) shaped by the cookie's mask. Resolve() returns
        // null for cookie mode None, in which case Dispatch falls back to a solid white mask (a hard square stamp).
        // magnitude is 1 here — the brush map is shape-only; the actual pressure is applied per-paint.
        var size = new Vector2Int(brushResolution, brushResolution);
        var mask = settings.brushCookie != null ? settings.brushCookie.Resolve(size) : null;
        VectorFieldRenderTextureUtils.EnsureValid(ref brushTexture, size);
        VectorFieldBrushTextureCreator.Dispatch(brushTexture, size, 1f, settings.brushSettings, mask);

        // Read the generated brush back to the CPU so painting can sample it (BuildBrush wraps brushMap via FromMap).
        var readbackRequest = AsyncGPUReadback.Request(brushTexture, 0, Callback);
        readbackRequest.WaitForCompletion();
        void Callback(AsyncGPUReadbackRequest request) {
            if (request.hasError) {
                Debug.LogError("AsyncGPUReadback encountered an error.");
                return;
            }
            var rawData = request.GetData<Color>();
            Vector2[] vectors = VectorFieldUtils.ColorsToVectors(rawData, 1);
            brushMap = new Vector2Map(new Point(request.width, request.height), vectors);
        }
        
        VectorFieldDrawingToolSettings.Save();
    }

    // Equivalent to Editor.OnSceneGUI.
    public override void OnToolGUI(EditorWindow window) {
        // Calculate deltaTime
        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)(currentTime - lastTime);
        lastTime = currentTime;
        
        if (window is not SceneView sceneView)
            return;

        Event e = Event.current;

        // Own the default control so clicks paint instead of deselecting / starting a rubber-band selection.
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        bool hasHit = GetHitPoint(e.mousePosition, out RaycastHit hit);

        var shiftHeld = e.modifiers.HasFlag(EventModifiers.Shift);
        var altHeld = e.modifiers.HasFlag(EventModifiers.Alt);
        // EditorGUI.actionKey is the platform action modifier: Ctrl on Windows/Linux, Cmd on macOS. We must NOT use
        // Event.current.control on macOS because the OS turns Ctrl+click into a right-click before the tool sees a
        // button-0 mouse-down, so any Ctrl gesture is dead on Mac. The action key is held as a temporary "erase"
        // override (Polybrush-style invert); the persistent mode comes from the overlay.
        var actionKeyHeld = EditorGUI.actionKey;

        // ActionKey+Scroll resizes the brush.
        if (actionKeyHeld && e.type == EventType.ScrollWheel) {
            e.Use();
            gridSpaceBrushSize = Mathf.Max(0.01f, gridSpaceBrushSize + e.delta.y * gridSpaceBrushSize * 3.5f * deltaTime);
            m_Overlay?.SyncFromTool();
            sceneView.Repaint();
        }

        // The action key temporarily forces Erase; otherwise use the op picked in the overlay.
        var activeOp = actionKeyHeld ? VectorFieldBrushOpRegistry.Erase : brushOp;

        if (hasHit && e.type == EventType.MouseDown && e.button == 0 && !altHeld) {
            // One undo entry per stroke: snapshot the component (incl. the painted field) before the first edit.
            Undo.RegisterCompleteObjectUndo(vectorFieldManager, "Paint Vector Field");

            var brush = BuildBrush(activeOp);
            if (shiftHeld) {
                // Shift = a single stamp (the map's baked emitter direction, no stroke). Radial ops radiate from here.
                vectorFieldManager.Stamp(brush, hit.point);
            } else {
                // Begin a continuous stroke; drag feeds it more points. Leading tip so paint tracks the cursor.
                activeStroke = vectorFieldManager.BeginStroke(brush);
                activeStroke.To(hit.point);
            }

            GUIUtility.hotControl = controlId;
            e.Use();
        }

        if (hasHit && e.type == EventType.MouseDrag && e.button == 0 && !altHeld) {
            activeStroke?.To(hit.point);
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == controlId) {
            activeStroke?.End();
            activeStroke = null;
            GUIUtility.hotControl = 0;
            e.Use();
        }

        if (hasHit && (e.type == EventType.Repaint || e.type == EventType.Layout))
            DrawBrushGizmo(hit, activeOp);

        // Repaint only while the cursor is moving over the scene so the brush gizmo tracks it, instead of forcing a
        // full repaint of every scene view on every event.
        if (hasHit && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag))
            sceneView.Repaint();

        HandleUtility.AddDefaultControl(controlId);
    }

    // Brush cursor: an outer disc at the brush radius plus a faded inner disc hinting at the falloff core, and (for a
    // directional emitter) a short arrow showing the stamp direction. Colour-coded by the active brush op.
    void DrawBrushGizmo(RaycastHit hit, IVectorFieldBrushOp op) {
        var cellCenter = vectorFieldManager.gridRenderer.cellCenter;
        Color color = op.GizmoColor;

        var lastMatrix = Handles.matrix;
        Handles.matrix = Matrix4x4.TRS(hit.point, Quaternion.identity, cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one * 0.5f));

        // Inner core radius shrinks as the Falloff softens (soft brush -> small solid core). Only meaningful for Falloff.
        var cookie = settings.brushCookie;
        if (cookie != null && cookie.mode == VectorFieldCookieSource.Mode.Falloff) {
            Handles.color = new Color(color.r, color.g, color.b, 0.25f);
            Handles.DrawWireDisc(Vector3.zero, hit.normal, Mathf.Clamp01(1f - cookie.falloffSoftness));
        }

        Handles.color = color;
        Handles.DrawWireDisc(Vector3.zero, hit.normal, 1f);
        Handles.matrix = lastMatrix;

        // Direction arrow — only when the active op paints the emitter direction, the emitter is directional (spot
        // emitters have no single arrow direction), and we're in Fixed-angle mode (in Follow mode the flow comes from
        // the stroke, so a fixed arrow would mislead).
        if (op.UsesBrushDirection && settings.directionMode == VectorFieldDirectionMode.FixedAngle
            && settings.brushSettings.forceType == VectorFieldBrushSettings.ForceEmitterType.Directional) {
            float angle = settings.brushSettings.directionalAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            Vector3 worldDir = cellCenter.GridToWorldVector(dir).normalized;
            float length = cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one * 0.5f).magnitude;
            if (worldDir.sqrMagnitude > 0f)
                Handles.ArrowHandleCap(0, hit.point, Quaternion.LookRotation(worldDir, hit.normal), length, EventType.Repaint);
        }
    }

    // Build the runtime brush for the current settings: the cookie-shaped emitter map as the shape, plus the active
    // op, size, pressure, and direction mode from the overlay. The runtime stroke + Stamp then do the cell-building
    // and the (frame-rate-independent, exact-overlap) application — the same code path gameplay uses.
    VectorFieldBrush BuildBrush(IVectorFieldBrushOp op) {
        var shape = VectorFieldBrushShape.FromMap(brushMap);
        // gridSpaceBrushSize is the brush's full grid-space extent; the runtime brush wants a WORLD-space radius, which
        // the stroke converts back to ~gridSpaceBrushSize/2 grid cells (exact for a uniform grid scale).
        float worldRadius = vectorFieldManager.gridRenderer.cellCenter
            .GridToWorldVector(new Vector3(gridSpaceBrushSize * 0.5f, 0f, 0f)).magnitude;
        // Leading tip so the paint tracks the cursor with no lag (reads as responsive when drawing by hand).
        return new VectorFieldBrush(shape, op, worldRadius, pressure, TipMode.Leading, settings.directionMode);
    }

    bool GetHitPoint(Vector2 mousePosition, out RaycastHit hit) {
        // Create a ray from the camera through the clicked screen point
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

        // Perform the raycast
        if (Physics.Raycast(ray, out hit)) {
            return true;
        } else {
            Vector3 point = Vector3.zero;
            if (vectorFieldManager.gridRenderer.floorPlane.TryGetHitPoint(ray, out point)) {
                hit = new RaycastHit() {
                    point = point,
                    normal = vectorFieldManager.gridRenderer.floorPlane.normal
                };
                return true;
            }
        }

        return false;
    }

    // IDrawSelectedHandles interface allows tools to draw gizmos when the target objects are selected, but the tool
    // has not yet been activated. This allows you to keep MonoBehaviour free of debug and gizmo code.
    public void OnDrawHandles() { }
}



// [EditorToolbarElement("VectorFieldToolbar", typeof(SceneView))]
// class VectorFieldToolbar : EditorToolbarButton
// {
//     public VectorFieldToolbar() : base("Vector Field")
//     {
//         icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/VectorFieldIcon.png");
//         tooltip = "Activate Vector Field Drawing Tool";
//         clicked += OnClicked;
//     }
//
//     private void OnClicked()
//     {
//         ToolManager.SetActiveTool<VectorFieldDrawingTool>();
//     }
// }