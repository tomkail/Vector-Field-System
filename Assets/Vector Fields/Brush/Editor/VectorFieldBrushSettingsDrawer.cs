using UnityEditor;
using UnityEngine;

// IMGUI drawer: the vector field component inspectors draw via DrawDefaultInspector (IMGUI), so a
// UIElements-only drawer would render as "No GUI Implemented". Shows the force type plus only the angle that type uses
// — directionalAngle for Directional, vortexAngle for Spot.
[CustomPropertyDrawer(typeof(VectorFieldBrushSettings))]
public class VectorFieldBrushSettingsDrawer : PropertyDrawer {

	// The one angle field the current force type uses, or null if it uses none.
	static SerializedProperty RelevantField(SerializedProperty property) {
		var forceType = (VectorFieldBrushSettings.ForceEmitterType)property.FindPropertyRelative("forceType").enumValueIndex;
		switch (forceType) {
			case VectorFieldBrushSettings.ForceEmitterType.Directional: return property.FindPropertyRelative("directionalAngle");
			case VectorFieldBrushSettings.ForceEmitterType.Spot:        return property.FindPropertyRelative("vortexAngle");
			default:                                                     return null;
		}
	}

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		float line = EditorGUIUtility.singleLineHeight;
		if (!property.isExpanded) return line;
		float spacing = EditorGUIUtility.standardVerticalSpacing;
		var forceTypeProp = property.FindPropertyRelative("forceType");
		float height = line + spacing + EditorGUI.GetPropertyHeight(forceTypeProp); // foldout + force type
		var field = RelevantField(property);
		if (field != null) height += spacing + EditorGUI.GetPropertyHeight(field);
		return height;
	}

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		float line = EditorGUIUtility.singleLineHeight;
		float spacing = EditorGUIUtility.standardVerticalSpacing;

		var foldoutRect = new Rect(position.x, position.y, position.width, line);
		property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
		if (!property.isExpanded) return;

		using (new EditorGUI.IndentLevelScope()) {
			var forceTypeProp = property.FindPropertyRelative("forceType");
			var forceTypeRect = new Rect(position.x, foldoutRect.yMax + spacing, position.width, EditorGUI.GetPropertyHeight(forceTypeProp));
			EditorGUI.PropertyField(forceTypeRect, forceTypeProp);

			var field = RelevantField(property);
			if (field != null) {
				var fieldRect = new Rect(position.x, forceTypeRect.yMax + spacing, position.width, EditorGUI.GetPropertyHeight(field));
				EditorGUI.PropertyField(fieldRect, field, true);
			}
		}
	}
}
