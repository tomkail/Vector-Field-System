using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// Inspector for ParticleSystemVectorField, in the plugin's card style (see VectorFieldInspectorUI): a Field section,
	// a Force section (the amplitude curve, drawn as a 0..1 ranged CurveField so this doesn't depend on UnityX's drawer),
	// and a Placement section. Contingent fields are hidden rather than shown inert — the force settings only exist once
	// a field is assigned, and Thickness only means anything while we're driving the transform.
	[CustomEditor(typeof(ParticleSystemVectorField)), CanEditMultipleObjects]
	public class ParticleSystemVectorFieldEditor : Editor {
		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement();
			VectorFieldInspectorUI.ApplyStyle(root);

			var fieldProp = serializedObject.FindProperty("_vectorFieldComponent");

			var fieldSection = VectorFieldInspectorUI.MakeSection("Field", ViewKey("field"));
			fieldSection.Add(VectorFieldInspectorUI.Field(fieldProp, "Vector Field",
				"The vector field the particles follow. Baked into the 3D texture the ParticleSystemForceField reads."));
			root.Add(fieldSection);

			// Everything below is inert without a field (Refresh clears the force field's texture and returns), so gate it
			// all on the reference. hasMultipleDifferentValues keeps it visible on a mixed multi-selection, where
			// objectReferenceValue reports null even though some targets do have a field.
			bool HasField() => fieldProp.hasMultipleDifferentValues || fieldProp.objectReferenceValue != null;

			var forceSection = VectorFieldInspectorUI.MakeSection("Force", ViewKey("force"));
			forceSection.Add(VectorFieldInspectorUI.RangedCurveField(serializedObject.FindProperty("amplitudeCurve"),
				"Amplitude", new Rect(0, 0, 1, 1),
				"Remaps the amplitude applied to particles across the normalized 0..1 range."));
			VectorFieldInspectorUI.ShowIf(forceSection, fieldProp, HasField);
			root.Add(forceSection);

			var placementSection = VectorFieldInspectorUI.MakeSection("Placement", ViewKey("placement"));
			var matchProp = serializedObject.FindProperty("matchFieldTransform");
			placementSection.Add(VectorFieldInspectorUI.Field(matchProp, "Match Field Transform",
				"Drive this object's transform to match the field's, so the force-field box overlays the field volume. Turn " +
				"off to position or animate the force field independently of the field."));

			// Thickness is only read by MatchTransform, so it does nothing while matching is off — hide it (with its help
			// line) rather than leave a control that silently has no effect.
			var thickness = new VisualElement();
			thickness.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("thickness"), "Thickness",
				"Depth of the force volume along the field plane's normal, in world units."));
			thickness.Add(VectorFieldInspectorUI.Help(
				"The field is 2D, so its flow is extruded uniformly through this depth: particles feel the same in-plane " +
				"force however far off the plane they sit, and nothing at all once they leave the box."));
			VectorFieldInspectorUI.ShowIf(thickness, matchProp, () => matchProp.boolValue);
			placementSection.Add(thickness);

			VectorFieldInspectorUI.ShowIf(placementSection, fieldProp, HasField);
			root.Add(placementSection);
			return root;
		}

		string ViewKey(string suffix) => $"VF.{nameof(ParticleSystemVectorField)}.{suffix}";
	}
}
