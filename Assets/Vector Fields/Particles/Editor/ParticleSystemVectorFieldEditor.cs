using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

// Default inspector, but the amplitude curve is drawn as a 0..1 ranged CurveField (natively, replacing the old
// [CurveRange] attribute so this component no longer depends on UnityX's drawer).
[CustomEditor(typeof(ParticleSystemVectorField)), CanEditMultipleObjects]
public class ParticleSystemVectorFieldEditor : Editor {
	public override VisualElement CreateInspectorGUI() =>
		VectorFieldInspectorUI.DefaultInspectorWithRangedCurve(serializedObject, "amplitudeCurve", new Rect(0, 0, 1, 1),
			"Remaps the amplitude applied to particles across the normalized 0..1 range.");
}
