using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorFileSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "File";}}

	private bool _createNewMapExpanded;
	public bool createNewMapExpanded {
		get {
			return _createNewMapExpanded;
		} set {
			_createNewMapExpanded = value;
		}
	}

	Texture2D saveIcon;
	Texture2D saveAsIcon;
	Texture2D undoIcon;
	Texture2D redoIcon;
	Texture2D clearIcon;
	Texture2D resetIcon;

	public VectorFieldEditorFileSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {
		saveIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/SaveIcon");
		saveAsIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/SaveAsIcon");
		undoIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/UndoIcon");
		redoIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/RedoIcon");
		clearIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/ClearIcon");
		resetIcon = Resources.Load<Texture2D>("VectorFieldEditor/Icons/ResetIcon");
	}

	public override void DrawWindow() {
		rect = new Rect(Vector2.zero, new Vector2(editorWindow.position.width, 40));
		if(editorWindow.vectorField == null) return;
		GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(rect.height), GUILayout.MaxWidth(rect.width));

		if(editorWindow.vectorField != null) {

			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			DrawSaveButton();
			DrawSaveAsButton();
			EditorGUILayout.EndHorizontal();



			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			DrawUndoButton();
			DrawRedoButton();
			EditorGUILayout.EndHorizontal();



			EditorGUILayout.BeginHorizontal(GUI.skin.box);
			DrawClearButton();
			DrawResetButton();
			EditorGUILayout.EndHorizontal();
		}
		EditorGUILayout.EndHorizontal();
    }

	private void DrawSaveButton () {
		if(editorWindow.saveFileUpToDate) GUI.enabled = false;
		if(GUILayout.Button(new GUIContent(saveIcon, "Save"), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			int saveTurbulence = EditorUtility.DisplayDialogComplex("Save", "Also save turbulence map?", "Yes", "No", "Cancel");
			if(saveTurbulence < 2) {
				editorWindow.SaveVectorField(true, saveTurbulence == 0, false);
			}
		}
		GUI.enabled = true;
    }

	private void DrawSaveAsButton () {
		if(GUILayout.Button(new GUIContent(saveAsIcon, "Save As..."), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			VectorFieldEditorWindow.NewMapProperties mapProperties = new VectorFieldEditorWindow.NewMapProperties();
			mapProperties.vectorField = editorWindow.vectorField;
			editorWindow.CreateNewMap(mapProperties);
		}
    }

	private void DrawUndoButton () {
		if(!editorWindow.undoManager.canStepBack) GUI.enabled = false;
		if(GUILayout.Button(new GUIContent(undoIcon, "Undo"), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			editorWindow.undoManager.StepBack();
			Event.current.Use();
		}
		GUI.enabled = true;
    }

	private void DrawRedoButton () {
		if(!editorWindow.undoManager.canStepForward) GUI.enabled = false;
		if(GUILayout.Button(new GUIContent(redoIcon, "Redo"), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			editorWindow.undoManager.StepForward();
			Event.current.Use();
		}
		GUI.enabled = true;
    }

	private void DrawClearButton () {
		if(GUILayout.Button(new GUIContent(clearIcon, "Clear"), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			editorWindow.Clear();
		}
    }

	private void DrawResetButton () {
		if(GUILayout.Button(new GUIContent(resetIcon, "Reset"), GUILayout.MaxWidth(32), GUILayout.Height(24))) {
			System.Array.Copy(editorWindow.vectorFieldScriptableObject.CreateMap().values, editorWindow.vectorField.values, editorWindow.vectorField.values.Length);
			editorWindow.saveFileUpToDate = true;
		}
    }
}