using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(PolygonVectorField)), CanEditMultipleObjects]
public class PolygonVectorFieldEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var source = VectorFieldInspectorUI.MakeSection("Source", ViewKey("source"));
		source.Add(new PropertyField(serializedObject.FindProperty("polygonRenderer"), "Polygon Renderer"));
		root.Add(source);

		root.Add(VectorFieldShapeInspector.Build(serializedObject, ViewKey("shape")));
	}
}
