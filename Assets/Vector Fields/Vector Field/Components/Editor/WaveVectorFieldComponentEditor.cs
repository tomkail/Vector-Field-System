using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(WaveVectorFieldComponent)), CanEditMultipleObjects]
public class WaveVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Wave", ViewKey("wave"));

		var sourceProp = serializedObject.FindProperty("sourceField");
		section.Add(new PropertyField(sourceProp, "Source Field"));

		var noSource = VectorFieldInspectorUI.Help("Assign a source field to animate — its flow is passed through, gusting over time.");
		section.Add(noSource);
		VectorFieldInspectorUI.ShowIf(noSource, sourceProp, () => sourceProp.objectReferenceValue == null);

		section.Add(new PropertyField(serializedObject.FindProperty("waveScale"), "Scale"));
		section.Add(new PropertyField(serializedObject.FindProperty("waveSpeed"), "Speed"));
		section.Add(new PropertyField(serializedObject.FindProperty("waveAmount"), "Amount"));
		section.Add(new PropertyField(serializedObject.FindProperty("animateInEditMode"), "Animate In Edit Mode"));

		root.Add(section);
	}
}
