using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(NoiseVectorFieldComponent)), CanEditMultipleObjects]
public class NoiseVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Noise", ViewKey("noise"));
		section.Add(new PropertyField(serializedObject.FindProperty("space"), "Space"));
		section.Add(new PropertyField(serializedObject.FindProperty("noiseSampler"), "Noise"));
		section.Add(new PropertyField(serializedObject.FindProperty("vortexAngle"), "Vortex Angle"));
		root.Add(section);
	}
}
