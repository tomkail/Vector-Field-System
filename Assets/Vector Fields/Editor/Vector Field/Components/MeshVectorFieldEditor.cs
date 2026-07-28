using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
	[CustomEditor(typeof(MeshVectorField)), CanEditMultipleObjects]
	public class MeshVectorFieldEditor : VectorFieldComponentEditor {
		protected override void BuildBody(VisualElement root) {
			var sources = VectorFieldInspectorUI.MakeSection("Sources", ViewKey("sources"));
			sources.Add(VectorFieldInspectorUI.Help("3D meshes contribute their cross-section where they cut the grid plane."));
			sources.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("crossSectionMeshes"), "Cross-Section Meshes",
				"3D meshes sliced where they intersect the grid plane; the intersection contour becomes a boundary."));
			sources.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("crossSectionSkinnedMeshes"), "Skinned Meshes",
				"Skinned/animated meshes, sliced the same way. Enable Continuous Update to re-slice as they animate."));
			sources.Add(VectorFieldInspectorUI.Help("2D sprites / colliders contribute their silhouette outline."));
			sources.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("silhouetteColliders"), "Silhouette Colliders",
				"2D colliders whose outline is used directly as a boundary."));
			sources.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("silhouetteSprites"), "Silhouette Sprites",
				"Sprites whose silhouette outline is used as a boundary."));
			root.Add(sources);

			root.Add(VectorFieldShapeInspector.Build(serializedObject, ViewKey("shape")));

			var update = VectorFieldInspectorUI.MakeSection("Update", ViewKey("update"));
			update.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("continuousUpdate"), "Continuous Update",
				"Re-slice every frame. Enable for animated/skinned meshes or moving colliders whose motion the change hash can't see."));
			root.Add(update);
		}
	}
}
