using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(WaveVectorFieldComponent)), CanEditMultipleObjects]
public class WaveVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Wave", ViewKey("wave"));

		var sourceProp = serializedObject.FindProperty("sourceField");
		section.Add(VectorFieldInspectorUI.Field(sourceProp, "Source Field",
			"The (usually static) field to animate. Its pattern is sampled across this field's extent."));

		var noSource = VectorFieldInspectorUI.Help("Assign a source field to animate — its flow is passed through, gusting over time.");
		section.Add(noSource);
		VectorFieldInspectorUI.ShowIf(noSource, sourceProp, () => sourceProp.objectReferenceValue == null);

		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("waveScale"), "Scale",
			"Gust wave frequency: waves per world unit along the flow direction."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("waveSpeed"), "Speed",
			"How fast gusts travel along the flow."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("waveAmount"), "Amount",
			"0 = steady pass-through of the source; 1 = fully gusting waves."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("animateInEditMode"), "Animate In Edit Mode",
			"Advance the wave in edit mode too. Otherwise it only animates in Play mode."));

		root.Add(section);
	}
}
