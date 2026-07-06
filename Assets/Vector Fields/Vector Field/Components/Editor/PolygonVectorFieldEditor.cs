using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(PolygonVectorField)), CanEditMultipleObjects]
public class PolygonVectorFieldEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var source = VectorFieldInspectorUI.MakeSection("Source", ViewKey("source"));
		source.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("polygonRenderer"), "Polygon Renderer",
			"The PolygonRenderer whose outline drives the field. Each cell points toward its nearest edge."));
		root.Add(source);

		root.Add(VectorFieldShapeInspector.Build(serializedObject, ViewKey("shape")));
	}
}
