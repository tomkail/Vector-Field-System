using UnityEngine;
using UnityEditor;

// Adds the "Clear" button that used to come from [EasyButtons.Button] on SmokeSimulationComponent, so the plugin
// doesn't depend on the EasyButtons package.
[CustomEditor(typeof(SmokeSimulationComponent)), CanEditMultipleObjects]
public class SmokeSimulationComponentEditor : Editor {
	public override void OnInspectorGUI() {
		DrawDefaultInspector();
		if (GUILayout.Button("Clear"))
			foreach (var t in targets)
				(t as SmokeSimulationComponent)?.Clear();
	}
}
