using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// UI Toolkit drawer for the output mask: a mode dropdown plus only the one field that mode uses (falloff softness,
// curve, or texture) — the others stay hidden until their mode is picked. Used by the vector field inspectors
// (which are UITK) via PropertyField.
[CustomPropertyDrawer(typeof(VectorFieldCookieSource))]
public class VectorFieldCookieSourceDrawer : PropertyDrawer {

	public override VisualElement CreatePropertyGUI(SerializedProperty property) {
		var root = new VisualElement();

		var modeProp = property.FindPropertyRelative("mode");
		root.Add(Tip(new PropertyField(modeProp, "Mask"),
			"How the output is masked: None (full strength), a soft radial Falloff, an authored radial Curve, or a Texture."));

		var softness = Indented(Tip(new PropertyField(property.FindPropertyRelative("falloffSoftness"), "Softness"),
			"0 = hard-edged circle, higher = softer edge."));
		// A CurveField (not a PropertyField) so we can constrain editing to the unit rect: both distance-from-centre (x)
		// and strength (y) are normalized 0..1, so the curve editor is clamped to [0,1]×[0,1]. Preset shapes (incl. a
		// donut) are added via the native curve editor's own preset bar (the ⚙ gear at its bottom-left).
		var curveField = new CurveField("Profile") { ranges = new Rect(0f, 0f, 1f, 1f) };
		curveField.bindingPath = property.FindPropertyRelative("curve").propertyPath;
		var curve = Indented(Tip(curveField,
			"Strength as a function of normalized distance from the centre (0 at centre, 1 at the edge)."));
		var texture = Indented(Tip(new PropertyField(property.FindPropertyRelative("texture"), "Texture"),
			"Explicit mask texture; samples the red channel as strength."));
		var invert = Indented(Tip(new PropertyField(property.FindPropertyRelative("invert"), "Invert"),
			"Flip the mask (1-x): full strength where it was empty and vice versa — rings, edge-weighted masks."));
		root.Add(softness);
		root.Add(curve);
		root.Add(texture);
		root.Add(invert);

		bool Is(VectorFieldCookieSource.Mode m) => (VectorFieldCookieSource.Mode)modeProp.enumValueIndex == m;
		VectorFieldInspectorUI.ShowIf(softness, modeProp, () => Is(VectorFieldCookieSource.Mode.Falloff));
		VectorFieldInspectorUI.ShowIf(curve, modeProp, () => Is(VectorFieldCookieSource.Mode.Curve));
		VectorFieldInspectorUI.ShowIf(texture, modeProp, () => Is(VectorFieldCookieSource.Mode.Texture));
		VectorFieldInspectorUI.ShowIf(invert, modeProp, () => !Is(VectorFieldCookieSource.Mode.None));

		return root;
	}

	static VisualElement Indented(VisualElement element) {
		element.style.marginLeft = 6;
		return element;
	}

	static T Tip<T>(T field, string tooltip) where T : VisualElement {
		field.tooltip = tooltip;
		return field;
	}
}
