using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace VectorFields {
    // Viewport-only display preferences for the vector field debug arrows. Stored in EditorPrefs (per editor,
    // not per scene/asset) because how densely arrows are drawn is a viewing choice, not field data.
    public static class VectorFieldDebugSettings {
        const string kVariableResolution = "VectorField.Debug.VariableResolution";
        const string kTargetSpacingPixels = "VectorField.Debug.TargetSpacingPixels";
        const string kMaxArrows = "VectorField.Debug.MaxArrows";
        const string kShowParentGroup = "VectorField.Debug.ShowParentGroup";

        public static bool VariableResolution {
            get => EditorPrefs.GetBool(kVariableResolution, true);
            set => EditorPrefs.SetBool(kVariableResolution, value);
        }

        // When a selected field lives under a group, also draw that group's combined output, so you can see the
        // field's contribution to the group alongside the field itself. Off by default.
        public static bool ShowParentGroup {
            get => EditorPrefs.GetBool(kShowParentGroup, false);
            set => EditorPrefs.SetBool(kShowParentGroup, value);
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

    [Overlay(typeof(SceneView), "vector-field-debug", "Vector Field Visualization")]
    public class VectorFieldDebugOverlay : Overlay {
        // Kept so selection changes can show/hide it without rebuilding the panel.
        Toggle showParentGroupToggle;

        public override void OnCreated() {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        public override void OnWillBeDestroyed() {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged() {
            // Only surface the panel while a vector field is selected — mirroring when the arrows actually draw.
            displayed = HasVectorFieldSelected();
            RefreshParentGroupToggle();
        }

        // "Show parent group" only makes sense when a selected field is actually inside a group, so hide the row
        // otherwise rather than offer a no-op toggle.
        void RefreshParentGroupToggle() {
            if (showParentGroupToggle == null) return;
            showParentGroupToggle.style.display = VectorFieldComponentDrawer.SelectionHasParentGroup()
                ? DisplayStyle.Flex : DisplayStyle.None;
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
            var showParentGroup = new Toggle("Show parent group") {
                value = VectorFieldDebugSettings.ShowParentGroup,
                tooltip = "When the selected field is inside a group, draw the group's combined output instead of the " +
                    "field itself, so you can read this field's effect on the group without the two overlapping."
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
            showParentGroup.RegisterValueChangedCallback(e => {
                VectorFieldDebugSettings.ShowParentGroup = e.newValue;
                SceneView.RepaintAll();
            });

            root.Add(variable);
            root.Add(spacing);
            root.Add(maxArrows);
            root.Add(showParentGroup);
            showParentGroupToggle = showParentGroup;
            RefreshEnabled();
            RefreshParentGroupToggle();
            return root;
        }
    }
}
