using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// Grouped, conditional inspector for the runtime arrow renderer, in the plugin's card style (see VectorFieldInspectorUI).
	// Appearance mirrors the Scene-view debug renderer — colour fields shown only for the mode that uses them — and Density
	// shows only the controls the chosen resolution mode reads (Fixed's count vs Adaptive's spacing/cap).
	[CustomEditor(typeof(VectorFieldArrowRenderer)), CanEditMultipleObjects]
	public class VectorFieldArrowRendererEditor : Editor {
		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement();
			VectorFieldInspectorUI.ApplyStyle(root);

			var field = VectorFieldInspectorUI.MakeSection("Field", ViewKey("field"));
			field.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("_vectorFieldComponent"), "Vector Field",
				"The vector field these arrows visualise."));
			root.Add(field);

			var placement = VectorFieldInspectorUI.MakeSection("Placement", ViewKey("placement"));
			placement.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("matchFieldTransform"), "Match Field Transform",
				"On: draw the arrows at the field's own placement. Off: draw them relative to this object's transform, so you " +
				"can offset / rotate / scale the overlay independently of the field."));
			root.Add(placement);

			root.Add(BuildAppearanceSection());
			root.Add(BuildDensitySection());
			return root;
		}

		VectorFieldInspectorUI.Section BuildAppearanceSection() {
			var appearance = serializedObject.FindProperty("appearance");
			var colorMode = appearance.FindPropertyRelative("colorMode");

			var section = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
			section.Add(VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("arrowTexture"), "Texture",
				"Glyph drawn for each arrow. At runtime the built-in glyph can't load (it lives under an Editor/ folder), so assign one here."));
			section.Add(VectorFieldInspectorUI.Field(colorMode, "Colour Mode",
				"Direction = hue from angle; Magnitude = low→high gradient; Fixed = a flat colour; Invert Background = invert what's behind."));

			// Only the colour fields the chosen mode actually uses (matches the Scene-view Project Settings page).
			var fixedColor = VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("fixedColor"), "Colour");
			VectorFieldInspectorUI.ShowIf(fixedColor, colorMode, () => colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.Fixed);
			section.Add(fixedColor);

			var lowColor = VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("lowColor"), "Low Magnitude");
			var highColor = VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("highColor"), "High Magnitude");
			VectorFieldInspectorUI.ShowIf(lowColor, colorMode, () => colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.Magnitude);
			VectorFieldInspectorUI.ShowIf(highColor, colorMode, () => colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.Magnitude);
			section.Add(lowColor);
			section.Add(highColor);

			var invertHelp = VectorFieldInspectorUI.Help("Arrows invert whatever's behind them, so they stand out against any background; the colour fields are ignored.");
			VectorFieldInspectorUI.ShowIf(invertHelp, colorMode, () => colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.InvertBackground);
			section.Add(invertHelp);

			// Magnitude scale — used by Direction (opacity) and Magnitude (gradient); irrelevant to Fixed / InvertBackground.
			var maxMag = VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("maxMagnitude"), "Max Magnitude",
				"Vector magnitude that maps to full intensity (the high colour / full direction opacity).");
			VectorFieldInspectorUI.ShowIf(maxMag, colorMode, () =>
				colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.Direction ||
				colorMode.enumValueIndex == (int)VectorFieldDebugColorMode.Magnitude);
			section.Add(maxMag);

			section.Add(VectorFieldInspectorUI.Field(appearance.FindPropertyRelative("opacity"), "Opacity",
				"Overall opacity multiplier for the arrows."));
			return section;
		}

		VectorFieldInspectorUI.Section BuildDensitySection() {
			var mode = serializedObject.FindProperty("resolutionMode");
			var section = VectorFieldInspectorUI.MakeSection("Density", ViewKey("density"));
			section.Add(VectorFieldInspectorUI.Field(mode, "Resolution Mode",
				"Native = one arrow per cell; Fixed = a set count regardless of camera; Adaptive = scales with the camera."));

			var fixedRes = VectorFieldInspectorUI.Field(serializedObject.FindProperty("fixedResolution"), "Fixed Resolution",
				"Number of arrows along the field's long axis.");
			VectorFieldInspectorUI.ShowIf(fixedRes, mode, () => mode.enumValueIndex == (int)VectorFieldArrowResolutionMode.Fixed);
			section.Add(fixedRes);

			var spacing = VectorFieldInspectorUI.Field(serializedObject.FindProperty("targetSpacingPixels"), "Target Spacing (px)",
				"Desired screen-space gap between arrows.");
			var maxArrows = VectorFieldInspectorUI.Field(serializedObject.FindProperty("maxArrows"), "Max Arrows",
				"Upper bound on the number of arrows along the long axis.");
			VectorFieldInspectorUI.ShowIf(spacing, mode, () => mode.enumValueIndex == (int)VectorFieldArrowResolutionMode.Adaptive);
			VectorFieldInspectorUI.ShowIf(maxArrows, mode, () => mode.enumValueIndex == (int)VectorFieldArrowResolutionMode.Adaptive);
			section.Add(spacing);
			section.Add(maxArrows);
			return section;
		}

		string ViewKey(string suffix) => $"VF.{target.GetType().Name}.{suffix}";
	}
}
