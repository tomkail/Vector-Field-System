using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

// Default inspector, but the amplitude-alpha curve is drawn as a 0..1 ranged CurveField (natively, replacing the
// old [CurveRange] attribute so this component no longer depends on UnityX's drawer).
[CustomEditor(typeof(VectorFieldTextureRenderer)), CanEditMultipleObjects]
public class VectorFieldTextureRendererEditor : Editor {
	public override VisualElement CreateInspectorGUI() =>
		VectorFieldInspectorUI.DefaultInspectorWithRangedCurve(serializedObject, "amplitudeAlphaCurve", new Rect(0, 0, 1, 1),
			"Remaps alpha against the field's amplitude across the normalized 0..1 range.");
}
