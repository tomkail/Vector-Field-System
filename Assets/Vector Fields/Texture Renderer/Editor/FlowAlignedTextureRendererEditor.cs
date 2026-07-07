using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

// Inherits VectorFieldQuadEditor (default inspector + depthOffset gated on matchFieldBounds) and only customises the
// amplitude-alpha curve, drawn as a 0..1 ranged CurveField (natively, replacing the old [CurveRange] attribute so this
// component no longer depends on UnityX's drawer).
[CustomEditor(typeof(FlowAlignedTextureRenderer)), CanEditMultipleObjects]
public class FlowAlignedTextureRendererEditor : VectorFieldQuadEditor {
	protected override VisualElement BuildField(SerializedProperty property) {
		if (property.name == "amplitudeAlphaCurve")
			return VectorFieldInspectorUI.RangedCurveField(property.Copy(), property.displayName, new Rect(0, 0, 1, 1),
				"Remaps alpha against the field's amplitude across the normalized 0..1 range.");
		return base.BuildField(property);
	}
}
