using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

// Viewport-only display preferences for the vector field debug arrows. Stored in EditorPrefs (per editor,
// not per scene/asset) because how densely arrows are drawn is a viewing choice, not field data.
public static class VectorFieldDebugSettings {
    const string kVariableResolution = "VectorField.Debug.VariableResolution";
    const string kTargetSpacingPixels = "VectorField.Debug.TargetSpacingPixels";
    const string kMaxArrows = "VectorField.Debug.MaxArrows";

    public static bool VariableResolution {
        get => EditorPrefs.GetBool(kVariableResolution, true);
        set => EditorPrefs.SetBool(kVariableResolution, value);
    }

    // Desired screen-space gap between arrows, in pixels.
    public static float TargetSpacingPixels {
        get => EditorPrefs.GetFloat(kTargetSpacingPixels, 36f);
        set => EditorPrefs.SetFloat(kTargetSpacingPixels, value);
    }

    // Upper bound on arrows along the field's long axis (limits density when zoomed in).
    public static int MaxArrows {
        get => EditorPrefs.GetInt(kMaxArrows, 64);
        set => EditorPrefs.SetInt(kMaxArrows, value);
    }
}

[Overlay(typeof(SceneView), "vector-field-debug", "Vector Field Debug")]
public class VectorFieldDebugOverlay : Overlay {
    public override void OnCreated() {
        Selection.selectionChanged += UpdateVisibility;
        UpdateVisibility();
    }

    public override void OnWillBeDestroyed() {
        Selection.selectionChanged -= UpdateVisibility;
    }

    // Only surface the panel while a vector field is selected — mirroring when the arrows actually draw.
    void UpdateVisibility() {
        displayed = HasVectorFieldSelected();
    }

    static bool HasVectorFieldSelected() {
        foreach (var obj in Selection.gameObjects) {
            if (obj.GetComponent<VectorFieldComponent>() != null) return true;
        }
        return false;
    }

    public override VisualElement CreatePanelContent() {
        var root = new VisualElement { style = { minWidth = 220 } };

        var variable = new Toggle("Variable resolution") { value = VectorFieldDebugSettings.VariableResolution };
        var spacing = new Slider("Spacing (px)", 8f, 128f) {
            value = VectorFieldDebugSettings.TargetSpacingPixels,
            showInputField = true
        };
        var maxArrows = new SliderInt("Max arrows", 8, 256) {
            value = VectorFieldDebugSettings.MaxArrows,
            showInputField = true
        };

        void RefreshEnabled() {
            spacing.SetEnabled(variable.value);
            maxArrows.SetEnabled(variable.value);
        }

        variable.RegisterValueChangedCallback(e => {
            VectorFieldDebugSettings.VariableResolution = e.newValue;
            RefreshEnabled();
            SceneView.RepaintAll();
        });
        spacing.RegisterValueChangedCallback(e => {
            VectorFieldDebugSettings.TargetSpacingPixels = e.newValue;
            SceneView.RepaintAll();
        });
        maxArrows.RegisterValueChangedCallback(e => {
            VectorFieldDebugSettings.MaxArrows = e.newValue;
            SceneView.RepaintAll();
        });

        root.Add(variable);
        root.Add(spacing);
        root.Add(maxArrows);
        RefreshEnabled();
        return root;
    }
}
