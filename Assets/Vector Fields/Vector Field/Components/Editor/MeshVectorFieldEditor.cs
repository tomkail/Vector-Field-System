using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(MeshVectorField)), CanEditMultipleObjects]
public class MeshVectorFieldEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var sources = VectorFieldInspectorUI.MakeSection("Sources", ViewKey("sources"));
		sources.Add(VectorFieldInspectorUI.Help("3D meshes contribute their cross-section where they cut the grid plane."));
		sources.Add(new PropertyField(serializedObject.FindProperty("crossSectionMeshes"), "Cross-Section Meshes"));
		sources.Add(new PropertyField(serializedObject.FindProperty("crossSectionSkinnedMeshes"), "Skinned Meshes"));
		sources.Add(VectorFieldInspectorUI.Help("2D sprites / colliders contribute their silhouette outline."));
		sources.Add(new PropertyField(serializedObject.FindProperty("silhouetteColliders"), "Silhouette Colliders"));
		sources.Add(new PropertyField(serializedObject.FindProperty("silhouetteSprites"), "Silhouette Sprites"));
		root.Add(sources);

		root.Add(VectorFieldShapeInspector.Build(serializedObject, ViewKey("shape")));

		var update = VectorFieldInspectorUI.MakeSection("Update", ViewKey("update"));
		update.Add(new PropertyField(serializedObject.FindProperty("continuousUpdate"), "Continuous Update"));
		root.Add(update);
	}
}
