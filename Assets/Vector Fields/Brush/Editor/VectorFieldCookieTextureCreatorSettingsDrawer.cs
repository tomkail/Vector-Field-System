using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(VectorFieldCookieTextureCreatorSettings))]
public class VectorFieldCookieTextureCreatorSettingsDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        // Create the root container
        VisualElement root = new VisualElement();

        // Create fields for each property
        SerializedProperty gridSizeProp = property.FindPropertyRelative("gridSize");
        SerializedProperty generationModeProp = property.FindPropertyRelative("generationMode");
        SerializedProperty falloffSoftnessProp = property.FindPropertyRelative("falloffSoftness");
        SerializedProperty animationCurveProp = property.FindPropertyRelative("animationCurve");

        // Add the gridSize property
        PropertyField gridSizeField = new PropertyField(gridSizeProp, "Grid Size");
        root.Add(gridSizeField);

        // Add the generationMode property
        PropertyField generationModeField = new PropertyField(generationModeProp, "Generation Mode");
        root.Add(generationModeField);

        // Create fields for falloffSoftness and animationCurve
        PropertyField falloffSoftnessField = new PropertyField(falloffSoftnessProp, "Falloff Softness");
        PropertyField animationCurveField = new PropertyField(animationCurveProp, "Animation Curve");

        // Add a callback to change visibility based on generationMode
        generationModeField.RegisterCallback<ChangeEvent<int>>(evt =>
        {
            UpdateFieldVisibility((VectorFieldCookieTextureCreatorSettings.GenerationMode)evt.newValue);
        });

        // Function to update the visibility of the fields
        void UpdateFieldVisibility(VectorFieldCookieTextureCreatorSettings.GenerationMode mode)
        {
            // Clear existing falloffSoftness and animationCurve fields
            root.Remove(falloffSoftnessField);
            root.Remove(animationCurveField);

            // Add the appropriate fields based on the selected mode
            if (mode == VectorFieldCookieTextureCreatorSettings.GenerationMode.Exponent)
            {
                root.Add(falloffSoftnessField);
            }
            else if (mode == VectorFieldCookieTextureCreatorSettings.GenerationMode.AnimationCurve)
            {
                root.Add(animationCurveField);
            }
        }

        // Initially set field visibility based on the current generation mode
        UpdateFieldVisibility((VectorFieldCookieTextureCreatorSettings.GenerationMode)generationModeProp.enumValueIndex);

        // Return the constructed VisualElement
        return root;
    }
}
