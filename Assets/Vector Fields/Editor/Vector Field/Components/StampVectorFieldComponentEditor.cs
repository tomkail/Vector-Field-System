using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(StampVectorFieldComponent)), CanEditMultipleObjects]
public class StampVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Brush", ViewKey("brush"));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("brushSettingsParams"), "Brush",
			"The force emitter stamped into the field — a directional push or a spot/vortex."));
		root.Add(section);
	}
}
