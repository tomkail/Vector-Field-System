using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;

public class VectorFieldDrawingToolSettings : SerializedScriptableSingleton<VectorFieldDrawingToolSettings> {
    public Texture2D customBrushTexture;
    public BrushSource brushType;
    public enum BrushSource {
        Directional,
        Radial,
        Noise,
        Custom,
    }
    public BrushMode brushMode;
    public enum BrushMode {
    }
    public VectorFieldBrushSettings brushSettings = new VectorFieldBrushSettings();
    
    public float directionalBrushModeAngle;
    public float directionalBrushModeVortexAngle;
    
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

    // VectorFieldBrush brush = new VectorFieldBrush();
    Vector2Map brushMap;
    Texture brushTexture;
    float gridDistance = 0;
    float stepDistance = 1f;
    public float pressure = 1f;
    public float gridSpaceBrushSize = 5;

    public VectorFieldBrushTextureCreator brushCreator;


    
    VectorFieldCookieTextureCreator cookieTextureCreator;
    VectorFieldCookieTextureCreatorSettings cookieTextureCreatorSettings;
    

    // The second "context" argument accepts an EditorWindow type.
    [Shortcut("Activate DrawableVectorFieldComponent Tool", typeof(SceneView), KeyCode.P)]
    static void DrawableVectorFieldComponentToolShortcut()
    {
        if (Selection.GetFiltered<DrawableVectorFieldComponent>(SelectionMode.TopLevel).Length > 0)
            ToolManager.SetActiveTool<VectorFieldDrawingTool>();
        else
            Debug.Log("No platforms selected!");
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

        // SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Entering DrawableVectorFieldComponent Tool"), .1f);
        
        cookieTextureCreator = new VectorFieldCookieTextureCreator();
        cookieTextureCreatorSettings = new VectorFieldCookieTextureCreatorSettings() {
            gridSize = new Vector2Int(32,32),
            generationMode = VectorFieldCookieTextureCreatorSettings.GenerationMode.Exponent,
            falloffSoftness = 1,
        };
        brushCreator = new VectorFieldBrushTextureCreator(new Vector2Int(32,32), new VectorFieldBrushSettings());
        // Allocate unmanaged resources or perform one-time set up functions here
        OnBrushSettingsChange();
        
        
        m_Overlay = new VectorFieldDrawingToolSettingsOverlay();
        m_Overlay.Init(this);
        SceneView.AddOverlayToActiveView(m_Overlay);
    }

    // Called before the active tool is changed, or destroyed. The exception to this rule is if you have manually
    // destroyed this tool (ex, calling `Destroy(this)` will skip the OnWillBeDeactivated invocation).
    public override void OnWillBeDeactivated() {
        // SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Exiting DrawableVectorFieldComponent Tool"), .1f);
        
        cookieTextureCreator.Dispose();
        
        brushCreator.Dispose();
        brushCreator = null;
        
        SceneView.RemoveOverlayFromActiveView(m_Overlay);
    }

    
    public void OnBrushSettingsChange() {
        // if(VectorFieldDrawingToolSettings.Instance.brushType == VectorFieldDrawingToolSettings.BrushSource.Custom)
        cookieTextureCreator.Render(cookieTextureCreatorSettings);
        brushCreator.Render();
        // brushCreator.ReadIntoCPUImmediate();
        
        // // Request GPU readback synchronously
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

        GetHitPoint(e.mousePosition, out RaycastHit hit);
        
        var shiftHeld = e.modifiers.HasFlag(EventModifiers.Shift);
        var controlHeld = e.modifiers.HasFlag(EventModifiers.Control);
        var altHeld = e.modifiers.HasFlag(EventModifiers.Alt);
        var commandHeld = e.modifiers.HasFlag(EventModifiers.Command);
        
        // Debug.Log(shiftHeld+" "+controlHeld+" "+altHeld+" "+commandHeld);
        if (commandHeld && e.type == EventType.ScrollWheel) {
            e.Use();
            gridSpaceBrushSize += e.delta.y * gridSpaceBrushSize * 3.5f * deltaTime;
        }

        if (e.type == EventType.MouseDown) {
            Undo.RegisterCompleteObjectUndo(vectorFieldManager, "Edited Vector Field");
            
            var gridPosition = lastGridPosition = vectorFieldManager.gridRenderer.cellCenter.WorldToGridPosition(hit.point);
            
            if (shiftHeld) {
                // This is to compensate for the effect of having larger brushes, or of using brushes with different hardness. It's far from perfect, but it's better than nothing. Far worse with very small brushes.
                float sizeHardnessFactor = 1;//Mathf.Clamp(Mathf.Clamp((1.75f-brush.brushHardness), 1, 1.75f)/brush.size, 0,1);
                    
                List<Point> editedPoints = new List<Point>();
                editedPoints.AddRange(Stamp(gridPosition, 1, brushMap, gridSpaceBrushSize));
                EditVectorField(editedPoints);
                e.Use();
            }
        }
        
        if(e.type == EventType.MouseDrag && e.button == 0 && !altHeld) {
            var gridPosition = vectorFieldManager.gridRenderer.cellCenter.WorldToGridPosition(hit.point);
            
            Move((gridPosition - lastGridPosition).magnitude);

            if (commandHeld) {
                UpdateEraser(gridPosition, lastGridPosition);
                e.Use();
            } else if (controlHeld) {
                UpdateAdditiveDrawing(gridPosition, lastGridPosition);
                e.Use();
            } else {
                UpdateDrawing(gridPosition, lastGridPosition);
                e.Use();
            }
            lastGridPosition = gridPosition;
        }
        
        Handles.color = Color.green;
        var lastMatrix = Handles.matrix; 
        Handles.matrix = Matrix4x4.TRS(hit.point, Quaternion.identity, vectorFieldManager.gridRenderer.cellCenter.GridToWorldVector(gridSpaceBrushSize * Vector3.one * 0.5f));
        Handles.DrawWireDisc(Vector3.zero, hit.normal, 1);
        Handles.matrix = lastMatrix;
        
        SceneView.RepaintAll();
        
        HandleUtility.AddDefaultControl(0);
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
            vectorFieldManager.vectorField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, cellBrushAffectorParams.finalForce);
        }
        return editedPoints;
    }

    List<Point> Draw (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            var oldValue = vectorFieldManager.vectorField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint);
            var newValue = drawingStepParams.drawForce * pressure;
            newValue = Vector2.ClampMagnitude(newValue, Mathf.Lerp(oldValue.magnitude, pressure, cellBrushAffectorParams.brushForce.magnitude));
            vectorFieldManager.vectorField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, newValue);
        }
        return editedPoints;
    }
    
    List<Point> DrawAdditive (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            vectorFieldManager.vectorField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.vectorField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) + cellBrushAffectorParams.finalForce * pressure);
        }
        return editedPoints;
    }
    
    // List<Point> Smudge (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
    //     List<Point> editedPoints = new List<Point>();
    //
    //     foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
    //         vectorFieldManager.vectorField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.vectorField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) + cellBrushAffectorParams.finalForce * pressure);
    //     }
    //     return editedPoints;
    // }
    
    List<Point> Erase (DrawingStepParams drawingStepParams, float pressure, Vector2Map brushMap, float gridSpaceBrushSize) {
        List<Point> editedPoints = new List<Point>();

        foreach(var cellBrushAffectorParams in GetBrushPaint(drawingStepParams.gridPosition, drawingStepParams.drawForce, brushMap, gridSpaceBrushSize)) {
            vectorFieldManager.vectorField.SetValueAtGridPoint(cellBrushAffectorParams.gridPoint, vectorFieldManager.vectorField.GetValueAtGridPoint(cellBrushAffectorParams.gridPoint) * cellBrushAffectorParams.finalForce.magnitude * pressure);
        }
        return editedPoints;
    }

    void EditVectorField(List<Point> editedPoints) {
        vectorFieldManager.SetDirty();
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