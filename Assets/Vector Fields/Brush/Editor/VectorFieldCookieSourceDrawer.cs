using UnityEditor;
using UnityEngine;

// IMGUI drawer: the vector field component inspectors draw via DrawDefaultInspector (IMGUI), so a
// UIElements-only drawer would render as "No GUI Implemented". Shows the mode plus only the field that mode uses.
[CustomPropertyDrawer(typeof(VectorFieldCookieSource))]
public class VectorFieldCookieSourceDrawer : PropertyDrawer {

	// The one field the current mode uses, or null for None (which needs no extra field).
	static SerializedProperty RelevantField(SerializedProperty property) {
		var mode = (VectorFieldCookieSource.Mode)property.FindPropertyRelative("mode").enumValueIndex;
		switch (mode) {
			case VectorFieldCookieSource.Mode.Falloff: return property.FindPropertyRelative("falloffSoftness");
			case VectorFieldCookieSource.Mode.Curve:   return property.FindPropertyRelative("curve");
			case VectorFieldCookieSource.Mode.Texture: return property.FindPropertyRelative("texture");
			default:                                    return null; // None
		}
	}

	public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
		float line = EditorGUIUtility.singleLineHeight;
		if (!property.isExpanded) return line;
		float spacing = EditorGUIUtility.standardVerticalSpacing;
		var modeProp = property.FindPropertyRelative("mode");
		float height = line + spacing + EditorGUI.GetPropertyHeight(modeProp); // foldout + mode
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
			var modeProp = property.FindPropertyRelative("mode");
			var modeRect = new Rect(position.x, foldoutRect.yMax + spacing, position.width, EditorGUI.GetPropertyHeight(modeProp));
			EditorGUI.PropertyField(modeRect, modeProp);

			var field = RelevantField(property);
			if (field != null) {
				var fieldRect = new Rect(position.x, modeRect.yMax + spacing, position.width, EditorGUI.GetPropertyHeight(field));
				EditorGUI.PropertyField(fieldRect, field, true);
			}
		}
	}
}
