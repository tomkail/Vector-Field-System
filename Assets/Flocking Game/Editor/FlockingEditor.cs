using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Flocking))]
public class FlockingEditor : BaseEditor<Flocking> {
	public override void OnInspectorGUI() {
		Flocking flocking = (Flocking)target;

		// Draw the default inspector
		DrawDefaultInspector();

		EditorGUILayout.Space(10);

		// Create a horizontal group for Clear and Recreate buttons
		EditorGUILayout.BeginHorizontal();

		// Clear button
		if (GUILayout.Button("Clear Flock", GUILayout.Height(30))) {
			Undo.RecordObject(flocking.gameObject, "Clear Flock");

			flocking.ClearFlock();
			// if (Application.isPlaying) {
			// } else {
			// 	EditorUtility.SetDirty(flocking.gameObject);
			// }
		}

		// Recreate button
		if (GUILayout.Button("Recreate Flock", GUILayout.Height(30))) {
			Undo.RecordObject(flocking.gameObject, "Recreate Flock");

			flocking.RecreateFlockRuntime(true);
			// if (Application.isPlaying) {
			// } else {
			// 	EditorUtility.SetDirty(flocking.gameObject);
			// }
		}

		EditorGUILayout.EndHorizontal();

		// Add a button that combines recreate and pre-warm (only in play mode)
		if (Application.isPlaying) {
			EditorGUILayout.Space(5);
			if (GUILayout.Button("Recreate and Pre-warm", GUILayout.Height(30))) {
				flocking.RecreateFlockRuntime(true);
			}
		}
	}
}
