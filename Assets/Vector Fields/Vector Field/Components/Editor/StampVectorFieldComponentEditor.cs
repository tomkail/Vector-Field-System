using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(StampVectorFieldComponent)), CanEditMultipleObjects]
public class StampVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Brush", ViewKey("brush"));
		section.Add(new PropertyField(serializedObject.FindProperty("brushSettingsParams"), "Brush"));
		root.Add(section);
	}
}
