using UnityEngine;
using UnityEditor;
using System.Collections;
using UnityX.Geometry;

[CustomEditor(typeof(VectorFieldScriptableObject))]
public class VectorFieldScriptableObjectEditor : BaseEditor<VectorFieldScriptableObject> {

	public override void OnInspectorGUI () {
		base.OnInspectorGUI ();
		if (GUILayout.Button("Edit")) {
			VectorFieldEditorWindow window = VectorFieldEditorWindow.CreateWindow();
			window.LoadVectorField(data);
		}
	}
}