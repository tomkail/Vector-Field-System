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
		var curve = Indented(Tip(new PropertyField(property.FindPropertyRelative("curve"), "Profile"),
			"Strength as a function of normalized distance from the centre (0 at centre, 1 at the edge)."));
		var texture = Indented(Tip(new PropertyField(property.FindPropertyRelative("texture"), "Texture"),
			"Explicit mask texture; samples the red channel as strength."));
		root.Add(softness);
		root.Add(curve);
		root.Add(texture);

		bool Is(VectorFieldCookieSource.Mode m) => (VectorFieldCookieSource.Mode)modeProp.enumValueIndex == m;
		VectorFieldInspectorUI.ShowIf(softness, modeProp, () => Is(VectorFieldCookieSource.Mode.Falloff));
		VectorFieldInspectorUI.ShowIf(curve, modeProp, () => Is(VectorFieldCookieSource.Mode.Curve));
		VectorFieldInspectorUI.ShowIf(texture, modeProp, () => Is(VectorFieldCookieSource.Mode.Texture));

		return root;
	}

	static VisualElement Indented(VisualElement element) {
		element.style.marginLeft = 6;
		return element;
	}

	static PropertyField Tip(PropertyField field, string tooltip) {
		field.tooltip = tooltip;
		return field;
	}
}
