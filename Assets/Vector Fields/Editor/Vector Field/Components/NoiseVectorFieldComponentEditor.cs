using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
	[CustomEditor(typeof(NoiseVectorFieldComponent)), CanEditMultipleObjects]
	public class NoiseVectorFieldComponentEditor : VectorFieldComponentEditor {
		protected override void BuildBody(VisualElement root) {
			AddNormalizeToggle(root);

			var section = VectorFieldInspectorUI.MakeSection("Noise", ViewKey("noise"));
			section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("space"), "Space",
				"Sample the noise in Local space (moves with the grid) or World space (the field flows past a moving grid)."));
			section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseSampler"), "Noise",
				"Noise sampler settings — offset position, frequency, octaves, lacunarity and persistence."));
			section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("vortexAngle"), "Vortex Angle",
				"Rotates every sampled vector around the plane normal. 0° points along the gradient; 90° swirls."));
			root.Add(section);
		}

		// Slots a Normalize toggle directly under the base Field section's Magnitude row. While it's on, the component
		// auto-computes magnitude (peak output = 1), so the Magnitude field stays visible — showing the live computed
		// value via its binding — but disabled.
		void AddNormalizeToggle(VisualElement root) {
			var magnitudeField = root.Q<PropertyField>("vf-magnitude");
			var normalizeProp = serializedObject.FindProperty("normalizeMagnitude");
			if (magnitudeField == null || normalizeProp == null) return;

			var toggle = VectorFieldInspectorUI.Field(normalizeProp, "Normalize",
				"Auto-set Magnitude so the field's strongest vector has length 1 (recomputed on the GPU whenever the noise changes). " +
				"Magnitude shows the computed value and can't be edited while this is on.");
			var container = magnitudeField.parent;
			container.Insert(container.IndexOf(magnitudeField) + 1, toggle);

			void Sync() => magnitudeField.SetEnabled(!normalizeProp.boolValue);
			Sync();
			toggle.TrackPropertyValue(normalizeProp, _ => Sync());
		}
	}
}
