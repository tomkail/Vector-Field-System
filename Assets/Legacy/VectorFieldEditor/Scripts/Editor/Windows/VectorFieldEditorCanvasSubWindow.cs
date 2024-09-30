using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorCanvasSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "Canvas";}}
	public override Vector2 minSize {get {return new Vector2(200,200);}}

	public Texture2D brushTexture;

	public Rect textureContainerRect;
	public Rect textureRect;

	public Vector2 canvasRelativeMousePosition;
	public Vector2 normalizedMousePosition;
	public Vector2 gridPosition;
	public Point gridPoint;
	public Vector2 lastGridPosition;
	public Vector2 deltaGridPosition;

	public bool mouseOverCanvas = false;

	public VectorFieldEditorCanvasSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {}

	public override void DrawWindow() {
		Vector2 offset = new Vector2(260,80);
		Vector2 margin = new Vector2(4,4);

		rect = new Rect(offset, editorWindow.position.size - offset);
		GUI.BeginGroup(new Rect(0, 0, rect.width+offset.x, rect.height+offset.y));
		GUI.Box(rect, "");

		if(editorWindow.texture != null && editorWindow.vectorField != null && editorWindow.vectorFieldScriptableObject != null) {
			if(Event.current.type == EventType.Repaint) {
				textureContainerRect = new Rect(margin.x + rect.x, margin.y + rect.y, rect.width - margin.x * 2, rect.height - margin.y * 2);
				GUI.Box(textureContainerRect, "");
				Vector2 size = new Vector2(Mathf.Min(textureContainerRect.width, textureContainerRect.height), Mathf.Min(textureContainerRect.width, textureContainerRect.height));

				textureRect = new Rect(textureContainerRect.x,textureContainerRect.y,size.x,size.y);
				if(editorWindow.zoom <= Mathf.Max(textureContainerRect.width, textureContainerRect.height)/Mathf.Min(textureContainerRect.width, textureContainerRect.height)) {
					textureRect.size *= editorWindow.zoom;
				} else {
					textureRect.size = textureContainerRect.size;
				}

				canvasRelativeMousePosition = editorWindow.mousePosition;
				canvasRelativeMousePosition -= margin;
				canvasRelativeMousePosition -= offset;

				if(textureRect.size.x < textureContainerRect.size.x) {
					float xOffset = (textureContainerRect.size.x - textureRect.size.x) * 0.5f;
					textureRect.position += new Vector2(xOffset, 0);
					canvasRelativeMousePosition.x -= xOffset;
				}
				if(textureRect.size.y < textureContainerRect.size.y) {
					float yOffset = (textureContainerRect.size.y - textureRect.size.y) * 0.5f;
					textureRect.position += new Vector2(0, yOffset);
					canvasRelativeMousePosition.y -= yOffset;
				}

				textureRect = RectX.ClampInsideWithFlexibleSize(textureRect, textureContainerRect);

				normalizedMousePosition = Vector2X.Divide(canvasRelativeMousePosition, textureRect.size);
				normalizedMousePosition.y = 1f-normalizedMousePosition.y;

				if(editorWindow.renderTexture != null) {
					editorWindow.RefreshRenderTexture();
//					EditorGUI.DrawPreviewTexture(textureRect, editorWindow.background, null, ScaleMode.StretchToFill);
					EditorGUI.DrawPreviewTexture(textureRect, editorWindow.renderTexture, null, ScaleMode.StretchToFill);
				}
				mouseOverCanvas = rect.Contains(editorWindow.mousePosition) && !editorWindow.hoveredOverWindow;
				if(mouseOverCanvas) {
					DrawCursors();
				}
				DrawExampleBrush();

				Rect scaleRect = new Rect(rect.position.x + 6, rect.position.y + rect.height - (60 + 6), 120, 60);
				DrawScale(scaleRect);
			}
		}

		GUI.EndGroup();
	}

	void DrawExampleBrush () {
		if(brushTexture != null) {
			Point brushSize = Vector2X.Divide(Vector2.one * editorWindow.zoom * Mathf.Min(textureContainerRect.width, textureContainerRect.height), editorWindow.vectorField.size) * editorWindow.toolManager.currentTool.brush.size;
			Color savedColor = GUI.color;
			GUI.color = new Color(1, 1, 1, editorWindow.brushExampleTween.currentValue);
			GUI.DrawTexture(RectX.CreateFromCenter(textureContainerRect.position + textureContainerRect.size * 0.5f, brushSize.ToVector2()), brushTexture, ScaleMode.StretchToFill, true);
			GUI.color = savedColor;
		}
	}

	void DrawCursors () {
		Rect labelRect = new Rect(textureRect.position + canvasRelativeMousePosition, new Vector2(34,16));
		if(editorWindow.mousePosition.x-rect.position.x > rect.size.x * 0.5f) {
			labelRect.x -= labelRect.size.x;
			labelRect.x -= 5;
		} else if(editorWindow.mousePosition.x-rect.position.x < rect.size.x * 0.5f) {
			labelRect.x += 8;
		}
		if(editorWindow.mousePosition.y-rect.position.y > rect.size.y * 0.5f) {
			labelRect.y -= labelRect.size.y;
			labelRect.y += 0;
		} else if(editorWindow.mousePosition.y-rect.position.y < rect.size.y * 0.5f) {
			labelRect.y += 1;
		}

		GUI.Box(labelRect, gridPoint.x+","+gridPoint.y, EditorStyles.helpBox);

		Point brushSize = Vector2X.Divide(Vector2.one * editorWindow.zoom * Mathf.Min(textureContainerRect.width, textureContainerRect.height), editorWindow.vectorField.size) * editorWindow.toolManager.currentTool.brush.size;
		int targetBrushSize = Mathf.RoundToInt(Mathf.Clamp(brushSize.x, 0, 128));
		if((brushTexture == null || (brushTexture.width-1) != targetBrushSize)) {
			if(brushTexture != null)
				MonoBehaviour.DestroyImmediate(brushTexture);
			brushTexture = editorWindow.CreateRoundBrushTexture(targetBrushSize);
		}
		if(brushTexture != null) {
			GUI.DrawTexture(RectX.CreateFromCenter(textureRect.position + canvasRelativeMousePosition, brushSize.ToVector2()), brushTexture, ScaleMode.StretchToFill, true);
		}

		Texture2D vectorFieldDropperCursor = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VectorFieldEditor/Cursors/DropperCursor.psd");
		if(editorWindow.holdingAlt && vectorFieldDropperCursor != null) {
			GUI.DrawTexture(new Rect(textureRect.position + canvasRelativeMousePosition + new Vector2(16 * 0f,-15), new Vector2(16,16)), vectorFieldDropperCursor, ScaleMode.StretchToFill, true);
		}
		Texture2D vectorFieldLockCursor = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/VectorFieldEditor/Cursors/LockCursor.psd");
		if(editorWindow.holdingCommand && vectorFieldLockCursor != null) {
			GUI.DrawTexture(new Rect(textureRect.position + canvasRelativeMousePosition + new Vector2(16 * 0.5f,-16), new Vector2(16,16)), vectorFieldLockCursor, ScaleMode.StretchToFill, true);
		}

		if(editorWindow.holdingSpace) {
			EditorGUIUtility.AddCursorRect(rect, MouseCursor.Pan);
		} else {
			EditorGUIUtility.AddCursorRect(textureRect, MouseCursor.Link);
		}
	}

	void DrawScale (Rect area) {
		GUI.Box(area, "",EditorStyles.helpBox);
		GUI.Box(area, "",EditorStyles.helpBox);
		area.height -= 14;

		OnGUIX.DrawLine(new Vector2(area.x, area.y), new Vector2(area.x, area.y + area.height), Color.white, 1);
		OnGUIX.DrawLine(new Vector2(area.x, area.y + area.height), new Vector2(area.x + area.width, area.y + area.height), Color.white, 1);

		DrawInScale(area, 1);
		DrawInScale(area, 2.5f);
		DrawInScale(area, 5);
		DrawInScale(area, 10);
		DrawInScale(area, 25);
		DrawInScale(area, 50);
		DrawInScale(area, 100);
		DrawInScale(area, 250);
		DrawInScale(area, 500);
		DrawInScale(area, 1000);
		DrawInScale(area, 2500);
		DrawInScale(area, 5000);
	}

	void DrawInScale (Rect area, float numMeters) {
		AnimationCurve strengthIntervalCurve = new AnimationCurve(new Keyframe(3,0), new Keyframe(5,1), new Keyframe(area.width * 0.5f, 1), new Keyframe(area.width * 0.8f, 0));
		AnimationCurve intervalHeightMultiplierCurve = new AnimationCurve(new Keyframe(2,0.1f), new Keyframe(area.width * 0.8f, 1));
		AnimationCurve textOpacityCurve = new AnimationCurve(new Keyframe(area.width * 0.25f, 0), new Keyframe(area.width * 0.35f, 1), new Keyframe(area.width, 1));

		float screenSpaceInterval = editorWindow.WorldToScreenScale(new Vector2(numMeters,numMeters)).x;
		float numThatFit = area.width/screenSpaceInterval;

		float alpha = strengthIntervalCurve.Evaluate(screenSpaceInterval);
		if(alpha == 0) return;
		Color color = Color.white.WithAlpha(alpha);
		int numThatFitInt = Mathf.CeilToInt(numThatFit);

		float height = intervalHeightMultiplierCurve.Evaluate(screenSpaceInterval) * area.height * 0.8f;
		if(height == 0) return;
		float textOpacity = textOpacityCurve.Evaluate(screenSpaceInterval);
		GUIStyle textStyle =  new GUIStyle(GUI.skin.label);
		textStyle.alignment = TextAnchor.UpperCenter;
		for(int i = 0; i < numThatFitInt; i++) {
			float offset = screenSpaceInterval * i;
			OnGUIX.DrawLine(new Vector2(area.x+offset, area.y + area.height), new Vector2(area.x+offset, area.y + area.height - height), color, 1);

			if(textOpacity > 0) {
				Color savedColor = GUI.color;
				GUI.color = Color.white.WithAlpha(textOpacity);
				GUI.Label(new Rect(area.x+offset - (30 * 0.5f), area.y + area.height, 30, 14), editorWindow.ScreenToWorldScale(new Vector2(offset, offset)).x.ToString(), textStyle);
				GUI.color = savedColor;
			}
		}

	}
}