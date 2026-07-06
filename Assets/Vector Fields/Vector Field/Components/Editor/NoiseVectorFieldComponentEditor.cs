using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(NoiseVectorFieldComponent)), CanEditMultipleObjects]
public class NoiseVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Noise", ViewKey("noise"));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("space"), "Space",
			"Sample the noise in Local space (moves with the grid) or World space (the field flows past a moving grid)."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseSampler"), "Noise",
			"Noise sampler settings — offset position, frequency, octaves, lacunarity and persistence."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("vortexAngle"), "Vortex Angle",
			"Rotates every sampled vector around the plane normal. 0° points along the gradient; 90° swirls."));
		root.Add(section);
	}
}
