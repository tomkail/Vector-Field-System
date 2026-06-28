using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;

public class VectorFieldDrawingToolSettings : SerializedScriptableSingleton<VectorFieldDrawingToolSettings> {
    // The emitter (directional/spot) that the brush stamps. Shape comes from the cookie below.
    public VectorFieldBrushSettings brushSettings = new VectorFieldBrushSettings();

    // The brush's shape/softness. Defaults to a soft radial Falloff so brushes are round out of the box — the
    // stamp shader has no inherent falloff (strength == magnitude * cookie.r), so None gives a hard square.
    public VectorFieldCookieSource brushCookie = new VectorFieldCookieSource { mode = VectorFieldCookieSource.Mode.Falloff };

    public float gridSpaceBrushSize = 5;
    public float pressure = 1;
}


[EditorTool("Vector Field Tool", typeof(DrawableVectorFieldComponent))]
public class VectorFieldDrawingTool : EditorTool, IDrawSelectedHandles {
    
    private VectorFieldDrawingToolSettingsOverlay m_Overlay;
    
    VectorFieldDrawingToolSettings settings => VectorFieldDrawingToolSettings.Instance;
    
    private double lastTime;
    DrawableVectorFieldComponent vectorFieldManager => target as DrawableVectorFieldComponent;

    Vector2 lastGridPosition;

    Vector2Map brushMap;
    float gridDistance = 0;
    float stepDistance = 1f;

    // Persisted in the settings singleton so they survive tool re-activation and domain reloads.
    public float pressure {
        get => settings.pressure;
        set => settings.pressure = value;
    }
    public float gridSpaceBrushSize {
        get => settings.gridSpaceBrushSize;
        set => settings.gridSpaceBrushSize = value;
    }

    // Resolution of the generated brush map (the cookie-shaped stamp readback). Independent of the field grid.
    const int brushResolution = 32;

    public VectorFieldBrushTextureCreator brushCreator;


    // The second "context" argument accepts an EditorWindow type.
    [Shortcut("Activate DrawableVectorFieldComponent Tool", typeof(SceneView), KeyCode.P)]
    static void DrawableVectorFieldComponentToolShortcut()
    {
        if (Selection.GetFiltered<DrawableVectorFieldComponent>(SelectionMode.TopLevel).Length > 0)
            ToolManager.SetActiveTool<VectorFieldDrawingTool>();
        else if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Select a Drawable Vector Field to paint"));
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
        brushCreator = new VectorFieldBrushTextureCreator(new Vector2Int(brushResolution, brushResolution), settings.brushSettings);
        OnBrushSettingsChange();

        m_Overlay = new VectorFieldDrawingToolSettingsOverlay();
        m_Overlay.Init(this);
        SceneView.AddOverlayToActiveView(m_Overlay);
    }

    // Called before the active tool is changed, or destroyed. The exception to this rule is if you have manually
    // destroyed this tool (ex, calling `Destroy(this)` will skip the OnWillBeDeactivated invocation).
    public override void OnWillBeDeactivated() {
        brushCreator.DisposeAndDestroyRenderTexture();
        brushCreator = null;

        // Release the cookie's generated mask texture (rebuilt on demand the next time the tool activates).
        settings.brushCookie?.Dispose();

        SceneView.RemoveOverlayFromActiveView(m_Overlay);
    }

    
    public void OnBrushSettingsChange() {
        // Build the brush: a directional/spot emitter (brushSettings) shaped by the cookie's mask. Resolve() returns
        // null for cookie mode None, in which case Dispatch falls back to a solid white mask (a hard square stamp).
        // magnitude is 1 here — the brush map is shape-only; the actual pressure is applied per-paint.
        var size = brushCreator.GridSize;
        var mask = settings.brushCookie != null ? settings.brushCookie.Resolve(size) : null;
        brushCreator.EnsureHasValidRenderTexture();
        VectorFieldBrushTextureCreator.Dispatch(brushCreator.RenderTexture, size, 1f, settings.brushSettings, mask);

        // Read the generated brush back to the CPU so painting can sample it (GetBrushPaint reads brushMap).
        var readbackRequest = AsyncGPUReadback.Request(brushCreator.RenderTexture, 0, Callback);
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
        var controlHeld = e.modifiers.HasFlag(EventModifiers.Control);
        var altHeld = e.modifiers.HasFlag(EventModifiers.Alt);
        var commandHeld = e.modifiers.HasFlag(EventModifiers.Command);

        // Cmd+Scroll resizes the brush.
        if (commandHeld && e.type == EventType.ScrollWheel) {
            e.Use();
            gridSpaceBrushSize = Mathf.Max(0.01f, gridSpaceBrushSize + e.delta.y * gridSpaceBrushSize * 3.5f * deltaTime);
            m_Overlay?.SyncFromTool();
            sceneView.Repaint();
        }

        if (hasHit && e.type == EventType.MouseDown && e.button == 0 && !altHeld) {
            // One undo entry per stroke: snapshot the component (incl. the painted field) before the first edit.
            Undo.RegisterCompleteObjectUndo(vectorFieldManager, "Paint Vector Field");

            var gridPosition = lastGridPosition = vectorFieldManager.gridRenderer.cellCenter.WorldToGridPosition(hit.point);
            gridDistance = 0;

            if (shiftHeld)
                EditVectorField(new List<Point>(Stamp(gridPosition, 1, brushMap, gridSpaceBrushSize)));

            GUIUtility.hotControl = controlId;
            e.Use();
        }

        if (hasHit && e.type == EventType.MouseDrag && e.button == 0 && !altHeld) {
            var gridPosition = vectorFieldManager.gridRenderer.cellCenter.WorldToGridPosition(hit.point);

            Move((gridPosition - lastGridPosition).magnitude);

            if (commandHeld)
                UpdateEraser(gridPosition, lastGridPosition);
            else if (controlHeld)
                UpdateAdditiveDrawing(gridPosition, lastGridPosition);
            else
                UpdateDrawing(gridPosition, lastGridPosition);

            lastGridPosition = gridPosition;
            e.Use();
        }

        if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == controlId) {
            GUIUtility.hotControl = 0;
            e.Use();
        }

        if (hasHit && (e.type == EventType.Repaint || e.type == EventType.Layout))
            DrawBrushGizmo(hit, commandHeld, controlHeld);

        // Repaint only while the cursor is moving over the scene so the brush gizmo tracks it, instead of forcing a
        // full repaint of every scene view on every event.
        if (hasHit && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag))
            sceneView.Repaint();

        HandleUtility.AddDefaultControl(controlId);
    }

    // Brush cursor: an outer disc at the brush radius plus a faded inner disc hinting at the falloff core, and (for a
    // directional emitter) a short arrow showing the stamp direction. Colour-coded by the active paint mode.
    void DrawBrushGizmo(RaycastHit hit, bool commandHeld, bool controlHeld) {
        var cellCenter = vectorFieldManager.gridRenderer.cellCenter;
        Color color = commandHeld ? new Color(1f, 0.4f, 0.3f)      // erase
                    : controlHeld ? new Color(0.4f, 0.7f, 1f)      // additive
                    : Color.green;                                  // draw

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

        // Direction arrow for a directional emitter.
        if (settings.brushSettings.forceType == VectorFieldBrushSettings.ForceEmitterType.Directional) {
            float angle = settings.brushSettings.directionalAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            Vector3 worldDir = cellCenter.GridToWorldVector(dir).normalized;
            float length = cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one * 0.5f).magnitude;
            if (worldDir.sqrMagnitude > 0f)
                Handles.ArrowHandleCap(0, hit.point, Quaternion.LookRotation(worldDir, hit.normal), length, EventType.Repaint);
        }
    }

    void UpdateEraser(Vector2 gridPosition, Vector2 lastGridPosition) {
        var stepsToMove = GetDrawingSteps(gridPosition, lastGridPosition, gridSpaceBrushSize, ref gridDistance, stepDistance);
        List<Point> editedPoints = new List<Point>();
        foreach(var step in stepsToMove) 
            editedPoints.AddRange(Erase(step, pressure, brushMap, gridSpaceBrushSize));
        EditVectorField(editedPoints);
    }

    void UpdateDrawing(Vector2 gridPosition, Vector2 lastGridPosition) {
        var stepsToMove = GetDrawingSteps(gridPosition, lastGridPosition, gridSpaceBrushSize, ref gridDistance, stepDistance);
        List<Point> editedPoints = new List<Point>();
        foreach(var step in stepsToMove) 
            editedPoints.AddRange(Draw(step, pressure, brushMap, gridSpaceBrushSize));
        EditVectorField(editedPoints);
    }
    
    void UpdateAdditiveDrawing(Vector2 gridPosition, Vector2 lastGridPosition) {
        var stepsToMove = GetDrawingSteps(gridPosition, lastGridPosition, gridSpaceBrushSize, ref gridDistance, stepDistance);
        List<Point> editedPoints = new List<Point>();
        foreach(var step in stepsToMove) 
            editedPoints.AddRange(DrawAdditive(step, pressure, brushMap, gridSpaceBrushSize));
        EditVectorField(editedPoints);
    }
    
    public struct DrawingStepParams {
        public Vector2 gridPosition;
        public Vector2 drawForce;
    }
    static List<DrawingStepParams> GetDrawingSteps(Vector2 gridPosition, Vector2 lastGridPosition, float gridSpaceBrushSize, ref float gridDistance, float stepDistance = 1) {
        var deltaGridPosition = gridPosition - lastGridPosition;
        var gridDistanceMovedThisFrame = deltaGridPosition.magnitude;
        
        List<DrawingStepParams> steps = new List<DrawingStepParams>();
        // I don't really get this fudge factor. I guessed it such that the pressure is vaguely right after drawing. Maybe it relates to the brush falloff?
        float sizePressureModifier = (1f / gridSpaceBrushSize) * 1.1225f;

        // This approach has an exact step distance, but will not reach the target position exactly.
        gridDistance += gridDistanceMovedThisFrame;
        var numStepsToTake = Mathf.FloorToInt(gridDistance / stepDistance);
        var distanceToMove = numStepsToTake * stepDistance;
        float interval = stepDistance / (gridDistance - stepDistance);
        int i = 0;
        while (gridDistance >= stepDistance) {
            var distanceTravelled = Mathf.Lerp(0, distanceToMove, interval * i);
            Vector2 stepGridPosition = lastGridPosition + (gridPosition - lastGridPosition).normalized * distanceTravelled;
            Vector2 drawForce = deltaGridPosition.normalized * stepDistance * sizePressureModifier;
            var step = new DrawingStepParams() {
                gridPosition = stepGridPosition,
                drawForce = drawForce,
            };
            steps.Add(step);
            gridDistance -= stepDistance;
            i++;
        }

        return steps;
    }

    void Move(float gridDistanceMoved) { }


    struct CellBrushAffectorParams {
        public Point gridPoint;
        public Vector2 brushForce;
        public Vector2 finalForce;
    }

    private IEnumerable<CellBrushAffectorParams> GetBrushPaint(Vector2 gridPosition, float magnitude, Vector2Map brushMap, float gridSpaceBrushSize) {
        var worldBounds = new Bounds(vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(gridPosition), vectorFieldManager.gridRenderer.cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one));
        var pointsOnGrid = vectorFieldManager.gridRenderer.GetPointsInWorldBounds(worldBounds);
        var gridBrushSize = vectorFieldManager.gridRenderer.cellCenter.WorldToGridVector(Vector2.one * gridSpaceBrushSize);
        var brushRect = RectX.CreateFromCenter(gridPosition, gridBrushSize);
        
        foreach(var point in pointsOnGrid) {
            var normalizedBrushPos = Rect.PointToNormalized(brushRect, point);
            var brushForce = brushMap.GetValueAtNormalizedPosition(normalizedBrushPos);
            Vector2 finalForce = brushForce * magnitude;
            yield return new CellBrushAffectorParams() {
                gridPoint = point,
                brushForce = brushForce,
                finalForce = finalForce,
            };
        }
    }

    private IEnumerable<CellBrushAffectorParams> GetBrushPaint(Vector2 gridPosition, Vector2 vector, Vector2Map brushMap, float gridSpaceBrushSize) {
        var worldBounds = new Bounds(vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(gridPosition), vectorFieldManager.gridRenderer.cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one));
        var pointsOnGrid = vectorFieldManager.gridRenderer.GetPointsInWorldBounds(worldBounds);
        var gridBrushSize = vectorFieldManager.gridRenderer.cellCenter.WorldToGridVector(Vector3.one * gridSpaceBrushSize);
        var brushRect = RectX.CreateFromCenter(gridPosition, gridBrushSize);
        
        foreach(var point in pointsOnGrid) {
            var normalizedBrushPos = Rect.PointToNormalized(brushRect, point);
            var brushForce = brushMap.GetValueAtNormalizedPosition(normalizedBrushPos);
            Vector2 finalForce = brushForce * vector.magnitude;
            var degrees = Vector2X.Degrees(vector);
            finalForce = Vector2X.Rotate(finalForce, degrees);
            
            yield return new CellBrushAffectorParams() {
                gridPoint = point,
                brushForce = brushForce,
                finalForce = finalForce,
            };
        }
    }


    IEnumerable<Point> Stamp(Vector2 gridPosition, float magnitude, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(gridPosition, magnitude, brushMap, gridSpaceBrushSize)) {
            vectorFieldManager.PaintField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, cellBrushAffectorParams.finalForce);
            editedPoints.Add(cellBrushAffectorParams.gridPoint);
        }
        return editedPoints;
    }

    List<Point> Draw (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            var oldValue = vectorFieldManager.PaintField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint);
            var newValue = drawingStepParams.drawForce * pressure;
            newValue = Vector2.ClampMagnitude(newValue, Mathf.Lerp(oldValue.magnitude, pressure, cellBrushAffectorParams.brushForce.magnitude));
            vectorFieldManager.PaintField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, newValue);
            editedPoints.Add(cellBrushAffectorParams.gridPoint);
        }
        return editedPoints;
    }
    
    List<Point> DrawAdditive (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            vectorFieldManager.PaintField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.PaintField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) + cellBrushAffectorParams.finalForce * pressure);
            editedPoints.Add(cellBrushAffectorParams.gridPoint);
        }
        return editedPoints;
    }
    
    // List<Point> Smudge (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
    //     List<Point> editedPoints = new List<Point>();
    //
    //     foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
    //         vectorFieldManager.PaintField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.PaintField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) + cellBrushAffectorParams.finalForce * pressure);
    //     }
    //     return editedPoints;
    // }
    
    List<Point> Erase (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            vectorFieldManager.PaintField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.PaintField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) * cellBrushAffectorParams.finalForce.magnitude * pressure);
            editedPoints.Add(cellBrushAffectorParams.gridPoint);
        }
        return editedPoints;
    }

    void EditVectorField(List<Point> editedPoints) {
        if (editedPoints.Count == 0) {
            // No region info — fall back to a full re-upload.
            vectorFieldManager.SetDirty();
            return;
        }

        // Report the bounding rect of the touched cells so only that region is uploaded to the GPU texture.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in editedPoints) {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        // RectInt size is exclusive of the max, so +1 to include the max cell.
        vectorFieldManager.MarkRegionDirty(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
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