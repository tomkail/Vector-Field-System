using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorDebugSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "Debug";}}
	public override Vector2 minSize {get {return new Vector2(140,100);}}

	private Vector2 scrollPosition;

	public VectorFieldEditorDebugSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {
		rect = new Rect(0, 680, 260, 320);
		expanded = false;
	}

	public override void DrawWindow() {
		if(editorWindow.vectorField == null) return;
		scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(rect.height-(EditorGUIX.windowHeaderHeight + 15 + 10)));
		RenderTextureFormat newRenderTextureFormat = (RenderTextureFormat)EditorGUILayout.EnumPopup("Render Texture Format", editorWindow.renderTextureFormat);
		if(newRenderTextureFormat != editorWindow.renderTextureFormat) {
			editorWindow.renderTextureFormat = newRenderTextureFormat;
			editorWindow.RebuildRenderTexture();
		}
		TextureFormat newTextureFormat = (TextureFormat)EditorGUILayout.EnumPopup("Texture Format", editorWindow.textureFormat);

		if(SystemInfo.SupportsTextureFormat(newTextureFormat) && newTextureFormat != editorWindow.textureFormat) {
			editorWindow.textureFormat = newTextureFormat;
			editorWindow.RebuildTexture();
		}

		EditorGUILayout.HelpBox(
		"Texture max render size - "+editorWindow.maxTextureRenderSize+"\n"+
		"Downscale factor - "+editorWindow.textureScaleFactor,
		MessageType.None);

		EditorGUILayout.LabelField("Current max component: "+editorWindow.maxComponent);

		EditorGUILayout.HelpBox(
		"Normalized View Rect - "+editorWindow.normalizedRect,
		MessageType.None);

		EditorGUILayout.HelpBox(
		"Grid position - "+editorWindow.canvasWindow.gridPosition+"\n"+
		"Normalized Grid position - "+Vector2X.Divide(editorWindow.canvasWindow.gridPosition, editorWindow.vectorField.size)+"\n"+
		"Vector Field Value - "+(editorWindow.vectorField.IsOnGrid(editorWindow.canvasWindow.gridPoint) ? (editorWindow.vectorField.GetValueAtGridPoint(editorWindow.canvasWindow.gridPoint).ToString(2) + " " + editorWindow.vectorField.GetValueAtGridPoint(editorWindow.canvasWindow.gridPoint).magnitude.RoundTo(2)) : "Off grid")+"\n"+
		"Interpolated Vector Field Value - "+(editorWindow.vectorField.IsOnGrid(editorWindow.canvasWindow.gridPosition) ? editorWindow.vectorField.GetValueAtGridPosition(editorWindow.canvasWindow.gridPosition).ToString(2) + " " + editorWindow.vectorField.GetValueAtGridPosition(editorWindow.canvasWindow.gridPosition).magnitude.RoundTo(2) : "Off grid"),
		MessageType.None);

		EditorGUILayout.HelpBox(
		"Mouse Position"+"\n"+
		"Canvas mouse position - "+editorWindow.canvasWindow.canvasRelativeMousePosition+"\n"+
		"Normalized canvas mouse position - "+editorWindow.canvasWindow.normalizedMousePosition,
		MessageType.None);

		if(editorWindow.vectorField != null) {
			EditorGUILayout.HelpBox("Info"+"\n"+
			"Zoom - "+ editorWindow.zoom.RoundToSig(3).ToString()+"\n"+
			"Undo Index - "+(editorWindow.undoManager.historyIndex+1)+"/"+editorWindow.undoManager.history.Count+"\n"+
			"Editor Time - "+ EditorTime.time.ToString()+"\n"+
			"Max Vector Magnitude - "+ editorWindow.maxComponent, 
			MessageType.None);

			editorWindow.maxNumPixelsPerFrame = EditorGUILayout.IntField("Pixels per frame", editorWindow.maxNumPixelsPerFrame);
		} else {

		}
		GUILayout.EndScrollView();
    }
}