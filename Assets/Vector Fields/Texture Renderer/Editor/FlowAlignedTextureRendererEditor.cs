using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

// FlowAlignedTextureRenderer: the Texture Renderer chrome (Field / Material / Placement) plus the Flow-Aligned shader's
// amplitude/colour ramps — the amplitude curve drawn as a 0..1 ranged CurveField (native, no UnityX [CurveRange] dep).
[CustomEditor(typeof(FlowAlignedTextureRenderer)), CanEditMultipleObjects]
public class FlowAlignedTextureRendererEditor : VectorFieldTextureRendererEditor {
	protected override void BuildBody(VisualElement root) {
		base.BuildBody(root); // Material section

		var section = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
		section.Add(VectorFieldInspectorUI.RangedCurveField(serializedObject.FindProperty("amplitudeAlphaCurve"),
			"Amplitude Alpha", new Rect(0, 0, 1, 1),
			"Remaps alpha against the field's amplitude across the normalized 0..1 range."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("colorGradient"), "Colour Gradient",
			"Recolours the streaks when the material's \"Use Texture Colour\" is off, sampled by flow magnitude or streak luminance."));
		root.Add(section);
	}
}
