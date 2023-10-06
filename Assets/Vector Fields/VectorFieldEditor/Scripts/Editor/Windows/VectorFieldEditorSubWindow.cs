using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public abstract class VectorFieldEditorSubWindow {
	protected VectorFieldEditorWindow editorWindow;
	public abstract string name {get;}
	public virtual Rect rect {get; set;}
	public virtual Vector2 savedSize {get; set;}
	public virtual Vector2 compressedSize {get {return new Vector2(100, EditorGUIX.windowHeaderHeight);}}
	public virtual Vector2 minSize {get {return Vector2.zero;}}
	public virtual Vector2 maxSize {get {return Vector2.one * Mathf.Infinity;}}
	public virtual bool maintainAspectRatio {get {return false;}}
	private bool _expanded = true;
	public bool expanded {
		get {
			return _expanded;
		} set {
			_expanded = value;
			if(value) {
				rect = new Rect(rect.x, rect.y, savedSize.x, savedSize.y);
			} else {
				savedSize = rect.size;
				rect = new Rect(rect.x, rect.y, compressedSize.x, compressedSize.y);
			}
		}
	}

	public VectorFieldEditorSubWindow (VectorFieldEditorWindow editorWindow) {
		this.editorWindow = editorWindow;
	}

	public virtual void DrawWindowCompressed(int windowID) {
		Color savedColor = GUI.color;
		GUI.color = Color.green;
		if(GUI.Button(new Rect(0, 0, EditorGUIX.windowHeaderHeight + 20,EditorGUIX.windowHeaderHeight), "")) {
			expanded = true;
		}
		GUI.color = savedColor;
		GUI.Label(new Rect(0,0,rect.width, rect.height), name, EditorStyles.centeredGreyMiniLabel);
		GUI.DragWindow(new Rect(0,0,rect.width,rect.height));
	}

	public virtual void DrawWindow(int windowID) {
		Color savedColor = GUI.color;
		GUI.color = Color.yellow;
		if(GUI.Button(new Rect(0, 0, EditorGUIX.windowHeaderHeight + 20,EditorGUIX.windowHeaderHeight), "")) {
			expanded = false;

		}
		GUI.color = savedColor;
		DrawWindow();
		GUI.DragWindow(new Rect(0,0,rect.width,rect.height-15));
	}

	public abstract void DrawWindow();
}