using UnityEngine;
using UnityEditor;

namespace VectorFields {
	// Adds a "Clear" button to the SmokeSimulationComponent inspector, so the plugin doesn't depend on the
	// EasyButtons package.
	[CustomEditor(typeof(SmokeSimulationComponent)), CanEditMultipleObjects]
	public class SmokeSimulationComponentEditor : Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();
			if (GUILayout.Button("Clear"))
				foreach (var t in targets)
					(t as SmokeSimulationComponent)?.Clear();
		}
	}
}
