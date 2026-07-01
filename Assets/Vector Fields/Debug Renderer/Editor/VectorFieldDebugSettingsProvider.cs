using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Registers the "Vector Field Debug" page under Edit > Project Settings. Edits the project-wide appearance defaults
// (VectorFieldDebugProjectSettings) used by the scene-view debug arrows.
static class VectorFieldDebugSettingsProvider {
    [SettingsProvider]
    public static SettingsProvider Create() {
        return new SettingsProvider("Project/Vector Fields", SettingsScope.Project) {
            label = "Vector Fields",
            keywords = new HashSet<string>(new[] {
                "vector", "field", "debug", "arrow", "colour", "color", "texture", "magnitude", "direction"
            }),
            guiHandler = _ => {
                var settings = VectorFieldDebugProjectSettings.instance;
                var so = new SerializedObject(settings);
                so.Update();

                var appearance = so.FindProperty("_appearance");
                var texture = appearance.FindPropertyRelative("arrowTexture");
                var colorMode = appearance.FindPropertyRelative("colorMode");
                var fixedColor = appearance.FindPropertyRelative("fixedColor");
                var lowColor = appearance.FindPropertyRelative("lowColor");
                var highColor = appearance.FindPropertyRelative("highColor");
                var maxMagnitude = appearance.FindPropertyRelative("maxMagnitude");
                var opacity = appearance.FindPropertyRelative("opacity");

                EditorGUI.BeginChangeCheck();

                EditorGUILayout.LabelField("Arrows", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(texture, new GUIContent("Texture", "Leave empty to use the built-in arrow."));
                EditorGUILayout.PropertyField(opacity);
                EditorGUILayout.PropertyField(maxMagnitude, new GUIContent("Max Magnitude"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Colour", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(colorMode, new GUIContent("Mode"));

                // Only the colour fields relevant to the chosen mode (Direction needs none — it's hue-from-angle).
                switch ((VectorFieldDebugColorMode)colorMode.enumValueIndex) {
                    case VectorFieldDebugColorMode.Fixed:
                        EditorGUILayout.PropertyField(fixedColor, new GUIContent("Colour"));
                        break;
                    case VectorFieldDebugColorMode.Magnitude:
                        EditorGUILayout.PropertyField(lowColor, new GUIContent("Low Magnitude"));
                        EditorGUILayout.PropertyField(highColor, new GUIContent("High Magnitude"));
                        break;
                }

                if (EditorGUI.EndChangeCheck()) {
                    so.ApplyModifiedProperties();
                    settings.SaveChanges();
                    SceneView.RepaintAll();
                }
            },
        };
    }
}
