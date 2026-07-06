using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// UI Toolkit drawer for the stamp brush: the force type plus only the angle that type uses — directionalAngle for
// Directional, vortexAngle for Spot. Used by the vector field inspectors (which are UITK) via PropertyField.
[CustomPropertyDrawer(typeof(VectorFieldBrushSettings))]
public class VectorFieldBrushSettingsDrawer : PropertyDrawer {

	public override VisualElement CreatePropertyGUI(SerializedProperty property) {
		var root = new VisualElement();

		var typeProp = property.FindPropertyRelative("forceType");
		root.Add(Tip(new PropertyField(typeProp, "Force Type"),
			"Directional pushes every cell the same way; Spot emits radially / as a vortex from the centre."));

		var directional = Indented(Tip(new PropertyField(property.FindPropertyRelative("directionalAngle"), "Angle"),
			"Direction of the push, in degrees."));
		var vortex = Indented(Tip(new PropertyField(property.FindPropertyRelative("vortexAngle"), "Vortex Angle"),
			"Swirl angle around the centre. 0° = straight out (source), 90° = pure vortex."));
		root.Add(directional);
		root.Add(vortex);

		bool Is(VectorFieldBrushSettings.ForceEmitterType t) => (VectorFieldBrushSettings.ForceEmitterType)typeProp.enumValueIndex == t;
		VectorFieldInspectorUI.ShowIf(directional, typeProp, () => Is(VectorFieldBrushSettings.ForceEmitterType.Directional));
		VectorFieldInspectorUI.ShowIf(vortex, typeProp, () => Is(VectorFieldBrushSettings.ForceEmitterType.Spot));

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
