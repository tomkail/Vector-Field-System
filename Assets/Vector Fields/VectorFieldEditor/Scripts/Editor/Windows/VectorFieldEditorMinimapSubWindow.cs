using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorMinimapSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "Minimap";}}
	public override Vector2 minSize {get {return new Vector2(100,100);}}
	public override bool maintainAspectRatio {get {return true;}}

	private Rect minimapRect;
	private Rect minimapViewportRect;
	private Rect minimapBrushRect;
	private Rect minimapCursorRect;
	
	public bool draggingMinimap;
	private Vector2 minimapDragNormalizedStartPoint;

	public VectorFieldEditorMinimapSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {
		rect = new Rect(0, 410, 260, 260);
	}

	public override void DrawWindow() {

		GUILayout.Label("");
		if(editorWindow.vectorField == null || editorWindow.texture == null) return;

		UpdateMinimapRect();
		GUI.DrawTexture(minimapRect, editorWindow.texture);
		GUI.Box(minimapRect, "");
		GUI.Box(minimapRect, "");

		UpdateViewportRect();
		GUI.Box(minimapViewportRect, "");

		UpdateBrushRect();
		if(editorWindow.canvasWindow.mouseOverCanvas)
			GUI.Box(minimapBrushRect, "");

		UpdateCursorRect();
		if(editorWindow.canvasWindow.mouseOverCanvas)
			GUI.Box(minimapCursorRect, "");

		EditorGUIUtility.AddCursorRect(minimapRect, MouseCursor.MoveArrow);

		if(minimapRect.Contains(Event.current.mousePosition)) {
			
			if(minimapViewportRect.Contains(Event.current.mousePosition)) {
				GUI.Box(minimapViewportRect, "");

				if(Event.current.type == EventType.MouseDown) {
					minimapDragNormalizedStartPoint = Vector2X.Divide(Event.current.mousePosition-minimapViewportRect.position, minimapRect.size);
					draggingMinimap = true;
					Event.current.Use();
				}
			} else {
				if(Event.current.type == EventType.MouseDown) {
					Vector2 normalizedGridPosition = Vector2X.Divide(Event.current.mousePosition-minimapRect.position, minimapRect.size);
					Vector2 normalizedMinimapViewportSize = Vector2X.Divide(minimapViewportRect.size, minimapRect.size);
					editorWindow.normalizedRect = editorWindow.normalizedRect.WithPosition(normalizedGridPosition - normalizedMinimapViewportSize * 0.5f);
					UpdateViewportRect();

					minimapDragNormalizedStartPoint = Vector2X.Divide(Event.current.mousePosition-minimapViewportRect.position, minimapRect.size);

					draggingMinimap = true;
					Event.current.Use();
				}
			}
		}
		if(draggingMinimap) {
			if(Event.current.type == EventType.MouseDrag) {
				Vector2 normalizedGridPosition = Vector2X.Divide(Event.current.mousePosition-minimapRect.position, minimapRect.size);
				editorWindow.normalizedRect = editorWindow.normalizedRect.WithPosition(normalizedGridPosition - minimapDragNormalizedStartPoint);
				Event.current.Use();
			}
		}

//		if(Event.current.mousePosition+" "+minimapViewportRect);
//		if(minimapViewportRect.Contains(mousePosition))
	}



	void UpdateMinimapRect () {
		Vector2 offset = new Vector2(10,10);
		Rect totalMinimapContainerRect = new Rect(offset.x, EditorGUIX.windowHeaderHeight + offset.y, rect.width - offset.x * 2, rect.height - EditorGUIX.windowHeaderHeight - offset.y * 2);
		Rect minimapContainerRect = totalMinimapContainerRect;

		minimapRect = new Rect(minimapContainerRect.x,minimapContainerRect.y,Mathf.Min(minimapContainerRect.width, minimapContainerRect.height), Mathf.Min(minimapContainerRect.width, minimapContainerRect.height));

		if(minimapContainerRect.size.x > minimapRect.size.x) {
			float xOffset = (minimapContainerRect.size.x - minimapRect.size.x) * 0.5f;
			minimapRect.position += new Vector2(xOffset, 0);
		}
	}

	void UpdateCursorRect () {
		Vector2 singleCell = Vector2X.Divide(minimapRect.size, editorWindow.vectorField.size);
		Vector2 size = singleCell;
		Vector2 position = Vector2.Scale(singleCell, editorWindow.canvasWindow.gridPoint);
		position.y = (minimapRect.size.y - size.y) - position.y;
		position += minimapRect.position;
		minimapCursorRect = new Rect(position, size);
	}

	void UpdateBrushRect () {
		Vector2 singleCell = Vector2X.Divide(minimapRect.size, editorWindow.vectorField.size);
		Vector2 size = singleCell * editorWindow.toolManager.currentTool.brush.size;
		Vector2 position = Vector2.Scale(singleCell, editorWindow.canvasWindow.gridPoint);
		position -= (size - singleCell) * 0.5f;
		position.y = (minimapRect.size.y - size.y) - position.y;
		position += minimapRect.position;
		minimapBrushRect = new Rect(position, size);
	}

	void UpdateViewportRect () {
		Vector2 minimapViewportPosition = minimapRect.position + Vector2.Scale(minimapRect.size, editorWindow.normalizedRect.position);
		Vector2 minimapViewportSize = Vector2.Scale(minimapRect.size, editorWindow.normalizedRect.size);
		minimapViewportRect = new Rect(minimapViewportPosition, minimapViewportSize);
	}
}