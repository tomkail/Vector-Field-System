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
		root.Add(new PropertyField(typeProp, "Force Type"));

		var directional = Indented(new PropertyField(property.FindPropertyRelative("directionalAngle"), "Angle"));
		var vortex = Indented(new PropertyField(property.FindPropertyRelative("vortexAngle"), "Vortex Angle"));
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
}
