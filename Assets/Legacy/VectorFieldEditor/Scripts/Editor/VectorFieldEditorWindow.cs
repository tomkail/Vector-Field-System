using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorWindow : EditorWindow {

	public static VectorFieldEditorWindow Instance;

	public RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGBFloat;
	public TextureFormat textureFormat = TextureFormat.RGBAFloat;

	public Point maxTextureRenderSize = new Point(2048, 2048);
	public float textureScaleFactor = 1;

	public static Vector2 worldScale = new Vector2(1000,1000);
	public float minAllowedMagnitude = 0.1f;
	public float maxAllowedMagnitude = 100;
	public float maxAllowedMagnitudeReciprocal;

	// This only needs to be as large as the window, but we don't want to have to recreate it if the window changes size, so we just make a large one.
	private Point renderTextureSize = new Point(1024, 1024);

	public Material _material;
	public Material material {
		get {
			if(_material == null) {
				Shader shader = Resources.Load<Shader> ("VectorFieldFlowVisualizationEditor");
				Texture2D _texture = Resources.Load<Texture2D> ("DefaultVectorFieldTexture");
				_material = new Material(shader);
				_material.SetTexture("_Tex", _texture);
				_material.SetFloat("_Brightness", 50);
				_material.SetTexture("_BackgroundTex", background);
			}
			return _material;
		} set {
			_material = value;
		}
	}

	private VectorFieldScriptableObject _vectorFieldScriptableObject;
	public VectorFieldScriptableObject vectorFieldScriptableObject {
		get {
			return _vectorFieldScriptableObject;
		} set {
			_vectorFieldScriptableObject = value;
			if(_vectorFieldScriptableObject != null) {
				LoadVectorField();
			} else {
				vectorField = null;
			}
		}
	}

	public Vector2Map vectorField {get; private set;}
	public History<Vector2Map> undoManager;

	public VectorFieldPainterToolManager toolManager;

	public RenderTexture renderTexture;

	public bool showTurbulence = false;

	private bool hasDragged = false;
	public bool mouseHoldStartedOverCanvas = false;
	public bool canUseTools {
		get {
			return !holdingSpace && mouseHoldStartedOverCanvas;
		}
	}
	public bool leftMouseIsPressed = false;
	public bool leftMouseWasPressed = false;
	public bool leftMouseWasReleased = false;
	public bool rightMouseIsPressed = false;
	public bool rightMouseWasPressed = false;
	public bool rightMouseWasReleased = false;

	public bool holdingSpace = false;
	public Vector2 inputScrollVector = Vector2.zero;
	public bool holdingShift = false;
	public bool holdingAlt = false;
	public bool holdingCommand = false;
	private bool hasEditedThisDrag = false;
	public bool saveFileUpToDate = false;

	public bool hoveredOverWindow;

	public float scrollSpeed = 0.75f;
	public float shiftScrollSpeed = 1.75f;

	public float zoomSpeed = 0.5f;
	private float _zoom = 1;
	public float zoom {
		get {
			return _zoom;
		} set {
			float lastZoom = _zoom;
			_zoom = Mathf.Clamp (value, minZoom, maxZoom);
			float deltaZoom = (1f/lastZoom) - (1f/zoom);
			if(deltaZoom != 0) {
				Vector2 position = normalizedShaderRect.position + Vector2.Scale(canvasWindow.normalizedMousePosition, aspectRatioVector) * deltaZoom;
				normalizedShaderRect = new Rect(position, normalizedShaderRect.size);
			}
		}
	}
	public float minZoom = 0.5f;
	public float maxZoom {
		get {
			if(texture == null) return 1;
			return Mathf.Max(texture.width, texture.height) * 0.1f;
		}
	}

	public Vector2 aspectRatioVector {
		get {
			Vector2 aspectRatio = new Vector2(1, 1);
			if(canvasWindow.textureContainerRect.width > canvasWindow.textureContainerRect.height) {
				aspectRatio.x = canvasWindow.textureContainerRect.width / canvasWindow.textureContainerRect.height;
			} else {
				aspectRatio.y = canvasWindow.textureContainerRect.height / canvasWindow.textureContainerRect.width;
			}
			return aspectRatio;
		}
	}

	private Rect rectClamp = new Rect(0,0,1,1);

	public Rect normalizedShaderRect {
		get {
			return new Rect(_normalizedRect.x, (1f-_normalizedRect.height)-_normalizedRect.y, _normalizedRect.width, _normalizedRect.height);
		} set {
			_normalizedRect = new Rect(value.x, (1f-value.height)-value.y, value.width, value.height);
		}
	}
	private Rect _normalizedRect = new Rect(0,0,1,1);
	public Rect normalizedRect {
		get {
			return _normalizedRect;
		} set {
			_normalizedRect = value;
		}
	}

	public Texture2D texture;
	public int maxNumPixelsPerFrame = 12000;
	private int textureRefreshPixelIndex;

	private float _maxComponent;
	public float maxComponent {
		get {
			return _maxComponent;
		} set {
			if(_maxComponent == value) return;
			_maxComponent = value;
			maxComponentReciprocal = 1f/_maxComponent;
		}
	}
	float maxComponentThisScan = 0;
	public float maxComponentReciprocal;

	public float maxMagnitude;
	float maxMagnitudeThisScan = 0;

	public FloatTween brushExampleTween;

	public Vector2 mousePosition;

	public List<VectorFieldEditorSubWindow> subWindows;
	public VectorFieldEditorFileSubWindow fileWindow;
	public VectorFieldEditorToolbarSubWindow toolbarWindow;
	public VectorFieldEditorCanvasSubWindow canvasWindow;
	public VectorFieldEditorMinimapSubWindow minimapWindow;
	public VectorFieldEditorInfoSubWindow infoWindow;
	public VectorFieldEditorDebugSubWindow debugWindow;
	private int resizingWindowID = -1;

	Vector2 deltaMousePosition;

	public Timer applyTextureTimer;

	public Texture2D background;

	[MenuItem("El and Six/Sailing/Vector Field Editor")]
	public static VectorFieldEditorWindow CreateWindow () {
		Instance = EditorWindow.GetWindow <VectorFieldEditorWindow>(false, "Vector Field", true);
		return Instance;
	}


	void OnEnable () {
		titleContent = new GUIContent("Vector Field", Resources.Load<Texture2D>("VectorFieldEditor/Icons/WindowIcon"));

		toolManager = new VectorFieldPainterToolManager(this);
		toolManager.OnEditVectorField += OnEditVectorField;
		undoManager = new History<Vector2Map>();
		undoManager.OnChangeHistoryIndex += OnChangeHistoryIndex;

		brushExampleTween = new FloatTween(0);
		CreateSubWindows();
	}

	void CreateSubWindows () {
		fileWindow = new VectorFieldEditorFileSubWindow(this);
		toolbarWindow = new VectorFieldEditorToolbarSubWindow(this);
		canvasWindow = new VectorFieldEditorCanvasSubWindow(this);
		minimapWindow = new VectorFieldEditorMinimapSubWindow(this);
		infoWindow = new VectorFieldEditorInfoSubWindow(this);
		debugWindow = new VectorFieldEditorDebugSubWindow(this);

		subWindows = new List<VectorFieldEditorSubWindow>();
		subWindows.Add(minimapWindow);
		subWindows.Add(infoWindow);
		subWindows.Add(debugWindow);
	}

	void OnChangeHistoryIndex (Vector2Map historyItem) {
		System.Array.Copy(historyItem.values, vectorField.values, historyItem.values.Length);
		RefreshTextureImmediate();
	}
	
	void OnDisable () {
		
	}
	
	void OnFocus () {
		applyTextureTimer = null;
		VectorFieldEditorWindow.Instance = this;

		EditorApplication.update -= Update;
		EditorApplication.update += Update;

		maxAllowedMagnitudeReciprocal = 1f/maxAllowedMagnitude;

		if(toolManager == null) {
			toolManager = new VectorFieldPainterToolManager(this);
			toolManager.OnEditVectorField += OnEditVectorField;
		}
		
		if(vectorField == null || vectorField.values.IsNullOrEmpty()) LoadVectorField();
		InitVectorField();

	}

	public void LoadVectorField (VectorFieldScriptableObject vectorFieldScriptableObject) {
		this.vectorFieldScriptableObject = vectorFieldScriptableObject;
		LoadVectorField();
	}

	public void LoadVectorField () {
		if(vectorFieldScriptableObject == null) return;
		ResetViewProperties();
		vectorField = vectorFieldScriptableObject.CreateMap();

		undoManager.Clear();
		undoManager.AddToHistory(new Vector2Map(vectorField));

		toolManager.currentTool.brush.size = Mathf.Min(Mathf.Max(Mathf.RoundToInt(vectorField.size.magnitude * 0.025f), 1), 50);
	}

	void InitVectorField () {
		if(vectorFieldScriptableObject == null) return;
		if(vectorField.size.area != vectorField.values.Length) {
			vectorField.Clear();
			SaveVectorField(true, false, false);

			undoManager.Clear();
			undoManager.AddToHistory(new Vector2Map(vectorField));
			Debug.LogWarning("Cleared VectorField because area was not equal to number of values.");
		}
		maxComponent = maxComponentThisScan = vectorFieldScriptableObject.maxComponent; //VectorFieldScriptableObject.MaxAbsComponent(vectorField.values);
		maxMagnitude = maxMagnitudeThisScan = VectorFieldScriptableObject.LargestMagnitude(vectorField.values);

		int startIndex = vectorField.NormalizedPositionToArrayIndex(new Vector2(normalizedRect.x, 1f-(normalizedRect.y+normalizedRect.height)));
//		startIndex = Mathf.Clamp(startIndex, 0,vectorField.values.Length);
		textureRefreshPixelIndex = startIndex;
		RebuildTexture();

		RebuildRenderTexture();
//		RefreshTextureImmediate();
		material.SetFloat("_GridCellCount", vectorField.size.x);
	}

	void ResetViewProperties () {
		zoom = 1;
		normalizedRect = new Rect(0,0,1,1);
	}


	public void RebuildRenderTexture () {
		if(renderTexture != null) {
			renderTexture.Release();
		}
		renderTexture = new RenderTexture(renderTextureSize.x, renderTextureSize.y, 16, renderTextureFormat);
		renderTexture.wrapMode = TextureWrapMode.Clamp;

		RefreshRenderTexture();
	}

	public void RebuildTexture () {
		if(texture != null)
			DestroyImmediate(texture);

		Point clampedSize = new Point(Mathf.Clamp(vectorField.size.x, 0, maxTextureRenderSize.x), Mathf.Clamp(vectorField.size.y, 0, maxTextureRenderSize.y));
		textureScaleFactor = (float)clampedSize.x/vectorField.size.x;
		texture = new Texture2D(clampedSize.x, clampedSize.y, textureFormat, false);
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = FilterMode.Point;
	}

	void OnLostFocus() {
		inputScrollVector = Vector2.zero;
		holdingShift = false;
		holdingAlt = false;
		holdingCommand = false;
	}
	
	void OnDestroy() {
		EditorApplication.update -= Update;
	}

	public Texture2D CreateRoundBrushTexture(int radius) {
		TypeMap<Color> colorMap = new TypeMap<Color>(new Point(radius*2+1, radius*2+1), new Color(1,1,1,0));
		int x0 = radius;
		int y0 = radius;
		int x = radius;
		int y = 0;
		Color color = Color.white;
		int decisionOver2 = 1 - x;   // Decision criterion divided by 2 evaluated at x=r, y=0
		while( y <= x ) {
			colorMap.SetValueAtGridPoint(new Point( x + x0,  y + y0), color); // Octant 1
			colorMap.SetValueAtGridPoint(new Point( y + x0,  x + y0), color); // Octant 2
			colorMap.SetValueAtGridPoint(new Point(-x + x0,  y + y0), color); // Octant 4
			colorMap.SetValueAtGridPoint(new Point(-y + x0,  x + y0), color); // Octant 3
			colorMap.SetValueAtGridPoint(new Point(-x + x0, -y + y0), color); // Octant 5
			colorMap.SetValueAtGridPoint(new Point(-y + x0, -x + y0), color); // Octant 6
			colorMap.SetValueAtGridPoint(new Point( x + x0, -y + y0), color); // Octant 7
			colorMap.SetValueAtGridPoint(new Point( y + x0, -x + y0), color); // Octant 8
			y++;
			if (decisionOver2 <=0 ) {
				decisionOver2 += 2 * y + 1;   // Change in decision criterion for y -> y+1
			} else {
				x--;
				decisionOver2 += 2 * (y - x) + 1;   // Change for y -> y+1, x -> x-1
			}
		}
		Texture2D texture = TextureX.Create(colorMap.size, colorMap.values);
   		texture.Apply();
		return texture;
	}

	private void OnGUI () {
		UpdateEvents();
		DrawCanvas();
		hoveredOverWindow = false;
		DrawFile();
		DrawToolbar();
		DrawWindows();
		UpdateSize();
		Repaint();
	}

	private void DrawFile () {
		hoveredOverWindow = hoveredOverWindow || fileWindow.rect.Contains(mousePosition);
		fileWindow.DrawWindow();
	}

	private void DrawToolbar () {
		hoveredOverWindow = hoveredOverWindow || toolbarWindow.rect.Contains(mousePosition);
		toolbarWindow.DrawWindow();
	}

	private void DrawCanvas() {
		canvasWindow.DrawWindow();
	}

	private void UpdateSize () {
		float aspectRatio = Mathf.Max(canvasWindow.textureContainerRect.width, canvasWindow.textureContainerRect.height)/Mathf.Min(canvasWindow.textureContainerRect.width, canvasWindow.textureContainerRect.height);
		// This can happen after reloading scripts
		if(float.IsNaN(aspectRatio)) aspectRatio = 1;
		Vector2 size = new Vector2(aspectRatio/zoom, aspectRatio/zoom);
		size.x = Mathf.Clamp(size.x, 0, 1);
		size.y = Mathf.Clamp(size.y, 0, 1);
		if(zoom > 1) {
			if(canvasWindow.textureContainerRect.width == canvasWindow.textureRect.width && canvasWindow.textureContainerRect.height == canvasWindow.textureRect.height) {
				if(canvasWindow.textureContainerRect.width > canvasWindow.textureContainerRect.height) {
					size.y *= 1f/Mathf.Min(zoom,aspectRatio);
				} else {
					size.x *= 1f/Mathf.Min(zoom,aspectRatio);
				}
			} else if(canvasWindow.textureContainerRect.width == canvasWindow.textureRect.width) {
				size.x *= 1f/Mathf.Min(zoom,aspectRatio);
			} else if(canvasWindow.textureContainerRect.height == canvasWindow.textureRect.height) {
				size.y *= 1f/Mathf.Min(zoom,aspectRatio);
			}
		}
		normalizedShaderRect = new Rect(normalizedShaderRect.position, size);
	}

	private void UpdateEvents () {
		leftMouseWasPressed = false;
		leftMouseWasReleased = false;
		rightMouseWasPressed = false;
		rightMouseWasReleased = false;

		Vector2 lastMousePosition = mousePosition;
		mousePosition = Event.current.mousePosition;
		deltaMousePosition = mousePosition - lastMousePosition;

		if(Event.current.type == EventType.MouseDown) {
			if(Event.current.button == 0) {
				leftMouseIsPressed = true;
				leftMouseWasPressed = true;
			} else if(Event.current.button == 1) {
				rightMouseIsPressed = true;
				rightMouseWasPressed = true;
			}
			mouseHoldStartedOverCanvas = !hoveredOverWindow;
			hasDragged = false;
		}
		if(Event.current.type == EventType.MouseDrag) {
			if(!hasDragged)
				hasDragged = true;
		}

		if(Event.current.type == EventType.MouseUp) {
			if(Event.current.button == 0) {
				leftMouseIsPressed = false;
				leftMouseWasReleased = true;
			} else if(Event.current.button == 1) {
				rightMouseIsPressed = false;
				rightMouseWasReleased = true;
			}
			if(hasDragged && hasEditedThisDrag) {
				undoManager.AddToHistory(new Vector2Map(vectorField));
				hasDragged = false;
				hasEditedThisDrag = false;
				saveFileUpToDate = false;
			}
			if(minimapWindow.draggingMinimap) {
				minimapWindow.draggingMinimap = false;
			}
		}

		if(Event.current.type == EventType.Layout) {
			holdingSpace = holdingShift = Event.current.shift;
			holdingAlt = Event.current.alt;
			holdingCommand = Event.current.command;
		}

		if(Event.current.type == EventType.KeyDown && !leftMouseIsPressed && !rightMouseIsPressed) {
			if(Event.current.keyCode == KeyCode.Space) {
				holdingSpace = true;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.F) {
				zoom = 1;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.UpArrow) {
				inputScrollVector.y = 1;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.DownArrow) {
				inputScrollVector.y = -1;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.LeftArrow) {
				inputScrollVector.x = -1;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.RightArrow) {
				inputScrollVector.x = 1;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.Q) {
				if(undoManager.canStepBack) {
					undoManager.StepBack();
					Event.current.Use();
				}
			} else if(Event.current.keyCode == KeyCode.W) {
				if(undoManager.canStepForward) {
					undoManager.StepForward();
					Event.current.Use();
				}
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.S) {
				SaveVectorField(true, false, false);
				Event.current.Use();
			}
		}
		if(Event.current.type == EventType.KeyUp) {
			if(Event.current.keyCode == KeyCode.Space) {
				holdingSpace = false;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.UpArrow && inputScrollVector.y == 1) {
				inputScrollVector.y = 0;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.DownArrow && inputScrollVector.y == -1) {
				inputScrollVector.y = 0;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.LeftArrow && inputScrollVector.x == -1) {
				inputScrollVector.x = 0;
				Event.current.Use();
			} else if(Event.current.keyCode == KeyCode.RightArrow && inputScrollVector.x == 1) {
				inputScrollVector.x = 0;
				Event.current.Use();
			}
		}
		if(Event.current.type == EventType.ScrollWheel && !leftMouseIsPressed && !hoveredOverWindow) {
			zoom -= Event.current.delta.y * zoomSpeed * zoom * EditorTime.deltaTime;
		}
		if(Event.current.type == EventType.Layout) {
			if(inputScrollVector != Vector2.zero) {
				Vector2 newPosition = normalizedShaderRect.position + inputScrollVector.normalized * ((Event.current.shift ? shiftScrollSpeed : scrollSpeed) * (1f/zoom) * EditorTime.deltaTime);
				normalizedShaderRect = new Rect(newPosition, normalizedShaderRect.size);
			} else if (leftMouseIsPressed && holdingSpace && mouseHoldStartedOverCanvas) {
				Vector2 newPosition = normalizedRect.position -(Vector2X.Divide(deltaMousePosition, Vector2X.Divide(canvasWindow.textureRect.size, aspectRatioVector) * zoom));
				normalizedRect = new Rect(newPosition, normalizedRect.size);
			}
		}

		normalizedShaderRect = RectX.ClampInsideKeepSize(normalizedShaderRect, rectClamp);
	}

	private void DrawWindows () {
		Rect windowRect = new Rect(Vector2.zero, position.size);
		GUI.Box(windowRect.Expanded(Vector2.one * 0), "");

		BeginWindows();

		for(int i = 0; i < subWindows.Count; i++) {
			Rect rect = subWindows[i].rect;
			if(subWindows[i].expanded) {
				rect = GUI.Window(i+1, rect, subWindows[i].DrawWindow, subWindows[i].name);
				rect = RectX.ClampInsideKeepSize(rect, windowRect);
				rect.size = new Vector2(Mathf.Clamp(rect.size.x, subWindows[i].minSize.x, subWindows[i].maxSize.x), Mathf.Clamp(rect.size.y, subWindows[i].minSize.y, subWindows[i].maxSize.y));
				Resizer(i+1, ref rect, ref resizingWindowID);
				if(subWindows[i].maintainAspectRatio) {
					rect.size = Vector2.one * ((rect.size.x + rect.size.y) / 2);
				}
			} else {
				rect = GUI.Window(i+1, rect, subWindows[i].DrawWindowCompressed, subWindows[i].name);
				rect = RectX.ClampInsideKeepSize(rect, windowRect);
				rect.size = new Vector2(Mathf.Clamp(rect.size.x, 20, subWindows[i].maxSize.x), Mathf.Clamp(rect.size.y, 20, subWindows[i].maxSize.y));
			}
			subWindows[i].rect = rect;
		}

		for(int i = 0; i < subWindows.Count; i++) {
			if (subWindows[i].rect.Contains(mousePosition)) {
				hoveredOverWindow = true;
			}
		}

        EndWindows();
	}

	void Resizer(int windowID, ref Rect r, ref int resizingWindowID, float detectionRange = 15f) {
		Rect resizer = new Rect(r.max-new Vector2(detectionRange, detectionRange), new Vector2(detectionRange, detectionRange));

		Event current = Event.current;
		EditorGUIUtility.AddCursorRect(resizer, MouseCursor.ResizeUpLeft);
	 
		if (resizer.Contains(current.mousePosition) && current.type == EventType.MouseDown && current.button == 0) {
			resizingWindowID = windowID;
		}
		if (current.type == EventType.MouseUp) {
			resizingWindowID = -1;
		}
		if (resizingWindowID >= 0 && resizingWindowID == windowID && current.type == EventType.Layout) {
			r.size += current.delta;
		}
	}

	public void Update() {
		if(focusedWindow != this)
			return;

		if(applyTextureTimer == null) {
			applyTextureTimer = new Timer(0.1f, true);
			applyTextureTimer.OnRepeat += Repeat;
			applyTextureTimer.Start();
		} else {
			applyTextureTimer.Update(EditorTime.deltaTime);
		}

		if(vectorField != null) {
			canvasWindow.lastGridPosition = canvasWindow.gridPosition;
			Vector2 vectorFieldRelativeMousePosition = Vector2.Scale(canvasWindow.normalizedMousePosition, vectorField.size);
			canvasWindow.gridPosition = Vector2.Scale(vectorField.size, normalizedShaderRect.position) + Vector2.Scale(vectorFieldRelativeMousePosition, normalizedShaderRect.size);
			canvasWindow.gridPoint = new Vector2(Mathf.FloorToInt(canvasWindow.gridPosition.x), Mathf.FloorToInt(canvasWindow.gridPosition.y));
			canvasWindow.deltaGridPosition = canvasWindow.gridPosition-canvasWindow.lastGridPosition;
		}

		brushExampleTween.Update(EditorTime.deltaTime);
//		if(holdingMouse && !holdingSpace && mouseHoldStartedOverCanvas)
//		else
			toolManager.Update();
			RefreshTexture();
	}

	void Repeat () {
		if(texture)
			texture.Apply(false, false);
	}

	public void RefreshRenderTexture () {
		if(texture == null || renderTexture == null) return;
		Vector4 rectVector = new Vector4(normalizedShaderRect.xMin, normalizedShaderRect.yMin, normalizedShaderRect.xMax, normalizedShaderRect.yMax);
		Vector2 offset = -new Vector2(0.5f/vectorField.size.x, 0.5f/vectorField.size.y);
		rectVector += new Vector4(offset.x, offset.y, offset.x, offset.y);
		material.SetVector("_Rect", rectVector);
		RenderTexture lastActiveRT = RenderTexture.active;
		Graphics.Blit(texture, renderTexture, material);
		RenderTexture.active = lastActiveRT;
	}

	public void RefreshTextureImmediate () {
		if(vectorField == null || texture == null) return;

		Color[] pixels = new Color[texture.width * texture.height];
		if(textureScaleFactor == 1) {
			for(int i = 0; i < pixels.Length; i++) {
				Vector2 vector = vectorField.values[i];
				if(showTurbulence) {
					// vector += SpaceGameWorld.Instance.vectorFieldManager.turbulence.GetValueAtIndex(SpaceGameWorld.Instance.vectorFieldManager.vectorField, i);
				}
				pixels[i] = VectorFieldUtils.VectorToColor(vector, maxAllowedMagnitudeReciprocal);
			}
		} else {
			// If we're downscaling the texture, then we need to scale the vector field as we set it to a texture too. This becomes a bit more expensive, but its still way better than having to call apply on a massive texture.
			Point gridPoint = Point.zero;
			float textureScaleFactorReciprocal = 1f / textureScaleFactor;
			for(int i = 0; i < pixels.Length; i++) {
				gridPoint = Grid.ArrayIndexToGridPoint(i, texture.width);
				Vector2 vector = vectorField.GetValueAtGridPosition(gridPoint * textureScaleFactorReciprocal);
				if(showTurbulence) {
					// vector += SpaceGameWorld.Instance.vectorFieldManager.turbulence.GetValueAtGridPosition(SpaceGameWorld.Instance.vectorFieldManager.vectorField, gridPoint * textureScaleFactorReciprocal);
				}
				pixels[i] = VectorFieldUtils.VectorToColor(vector, maxAllowedMagnitudeReciprocal);
			}
		}


		maxComponent = maxComponentThisScan = Mathf.Max(0.1f, VectorFieldScriptableObject.GetMaxAbsComponent(vectorField.values));

		texture.SetPixels(pixels);
		texture.Apply();
		textureRefreshPixelIndex = 0;
	}

	public void HighPassFilter (float min) {
		for(int i = 0; i < vectorField.values.Length; i++) {
			if(vectorField.values[i].magnitude < min) {
				vectorField.values[i] = Vector2.zero;
			}
		}
		undoManager.AddToHistory(new Vector2Map(vectorField));
		saveFileUpToDate = false;
	}

	public void LowPassFilter (float max) {
		for(int i = 0; i < vectorField.values.Length; i++) {
			if(vectorField.values[i].magnitude > max) {
				vectorField.values[i] = vectorField.values[i].normalized * max;
			}
		}
		undoManager.AddToHistory(new Vector2Map(vectorField));
		saveFileUpToDate = false;
	}
	 
	private void RefreshTexture () {
		if(vectorField == null || vectorField.values.IsNullOrEmpty() || texture == null) return;
		int remaining = Mathf.Clamp(maxNumPixelsPerFrame, 0, vectorField.size.area);
		if(remaining == 0) return;

		int vfArea = vectorField.size.area;

		while(remaining >= 0) {
			if(textureRefreshPixelIndex >= vfArea) {
				textureRefreshPixelIndex = 0;
				maxComponent = maxComponentThisScan;
				maxMagnitude = maxMagnitudeThisScan;
				maxComponentThisScan = 0.1f;
				maxMagnitudeThisScan = 0;
			}
			SetTexturePixel(textureRefreshPixelIndex);
			textureRefreshPixelIndex++;
			remaining--;
		}
//		texture.Apply(false, false);
	}

	void OnEditVectorField (List<Point> editedPoints) {
		if(!hasEditedThisDrag && editedPoints.Count > 0) hasEditedThisDrag = true;
		for(int i = 0; i < editedPoints.Count; i++) {
			SetTexturePixel(editedPoints[i]);
		}
//		texture.Apply(false, false);
	}

	void SetTexturePixel (int index) {
		Point point = vectorField.ArrayIndexToGridPoint(index);
		SetTexturePixel(index, point);
	}

	void SetTexturePixel (Point point) {
		int index = vectorField.GridPointToArrayIndex(point);
		SetTexturePixel(index, point);
	}

	void SetTexturePixel (int index, Point point) {
		Vector2 vector = vectorField.GetValueAtGridPoint(point);
		if(showTurbulence) {
			// vector += SpaceGameWorld.Instance.vectorFieldManager.turbulence.GetValueAtGridPoint(SpaceGameWorld.Instance.vectorFieldManager.vectorField, point);
		}
		Color color = VectorFieldUtils.VectorToColor(vector, maxAllowedMagnitudeReciprocal);
		if(textureScaleFactor == 1)
			texture.SetPixel(point.x, point.y, color);
		else
			texture.SetPixel(Mathf.RoundToInt(point.x * textureScaleFactor), Mathf.RoundToInt(point.y * textureScaleFactor), color);
		maxComponentThisScan = Mathf.Max(maxComponentThisScan, vectorField.values[index].x.Abs(), vectorField.values[index].y.Abs());
		maxComponent = Mathf.Max(maxComponentThisScan, maxComponent);
		maxMagnitudeThisScan = Mathf.Max(maxMagnitudeThisScan, vectorField.values[index].magnitude);
		maxMagnitude = Mathf.Max(maxMagnitudeThisScan, maxMagnitude);
	}

	public void SaveVectorField (bool saveRaw, bool saveTurbulence, bool saveRawTurbulence) {
		EditorUtility.DisplayProgressBar("Saving...", "Saving", 0);
		if(saveRaw)
			vectorFieldScriptableObject.Save(vectorField);
		EditorUtility.DisplayProgressBar("Saving...", "Saving", 0.3f);
		saveFileUpToDate = true;
		EditorUtility.ClearProgressBar();
	}

	public NewMapProperties newMapProperties;
	public class NewMapProperties {
		public Texture2D importTexture;
		public Vector2Map vectorField;
		public string name = "New Vector Field";
		public int size = 64;

		public bool isValid {
			get {
				if(importTexture != null) {
					return true;
				} else if(vectorField != null) {
					return vectorField.values.Length == vectorField.size.area;
				} else {
					return size > 0;
				}
			}
		}
	}

	public void CreateNewMap (NewMapProperties mapProperties) {
		if(!mapProperties.isValid) {
			Debug.LogError("Map properties is not valid.");
			return;
		}
		VectorFieldScriptableObject vectorField = ScriptableObjectX.CreateAssetWithSavePrompt<VectorFieldScriptableObject>(mapProperties.name);
		if(vectorField == null) return;

		vectorField.size = new Point(mapProperties.size, mapProperties.size);
		if(mapProperties.importTexture != null) {
			vectorField.maxComponent = maxAllowedMagnitude * 0.5f;
			vectorField.texture = mapProperties.importTexture;
		} else if(mapProperties.vectorField != null) {
			vectorField.Save(mapProperties.vectorField);
		} else {
			vectorField.maxComponent = 0.1f;
			vectorField.Save(new Vector2Map(vectorField.size));
		}
		vectorFieldScriptableObject = vectorField;
    }

    public void Clear () {
		vectorField.Clear();
		maxComponent = maxComponentThisScan = 0.1f;
		maxMagnitude = maxMagnitudeThisScan = 0;
		textureRefreshPixelIndex = 0;
		undoManager.AddToHistory(new Vector2Map(vectorField));
    }

	public Vector2 WorldToScreenScale (Vector2 worldScale) {
		Vector2 normalizedScale = Vector2X.Divide(worldScale, VectorFieldEditorWindow.worldScale);
		return Vector2.Scale(normalizedScale, canvasWindow.textureRect.size * zoom);
    }

	public Vector2 ScreenToWorldScale (Vector2 screenScale) {
		Vector2 normalizedScale = Vector2X.Divide(screenScale, (canvasWindow.textureRect.size * zoom));
		return Vector2.Scale(normalizedScale, VectorFieldEditorWindow.worldScale);
    }
}