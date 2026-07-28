using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// TieredFlowAlignedTextureRenderer: everything from the Flow-Aligned editor except the single streak
	// texture/scale/speed (the tiers replace those), plus the N speed tiers via the shared LODGroup-style tier bar
	// (VectorFieldTierBarGUI).
	[CustomEditor(typeof(TieredFlowAlignedTextureRenderer)), CanEditMultipleObjects]
	public class TieredFlowAlignedTextureRendererEditor : VectorFieldTextureRendererEditor {
		VectorFieldTierBarGUI tierBar;

		protected override void BuildBody(VisualElement root) {
			base.BuildBody(root); // Material section

			tierBar ??= new VectorFieldTierBarGUI(serializedObject,
				new[] { "speed", "texture", "textureScale", "scrollSpeed" },
				el => {
					el.FindPropertyRelative("textureScale").floatValue = 10f;
					el.FindPropertyRelative("scrollSpeed").floatValue = 93f;
				},
				TieredFlowAlignedTextureRenderer.MaxTiers);

			var tierSection = VectorFieldInspectorUI.MakeSection("Speed Tiers", ViewKey("tiers"));
			tierSection.Add(VectorFieldInspectorUI.Help("Streak looks keyed to flow speed (0 = still → 1 = Max Speed). Each sample " +
				"blends the two tiers straddling its local speed. Drag the handles to position tiers; click a region to edit it; " +
				"right-click to add or remove."));
			if (!serializedObject.isEditingMultipleObjects)
				tierSection.Add(new IMGUIContainer(tierBar.OnGUI)); // single-target only; mixed selections fall back to the shared sections
			root.Add(tierSection);

			var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
			VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
			appearance.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("useTextureColor"), "Use Texture Colour",
				"Tint the speed colour by the streak texture's own RGB. Off = pure speed colour."));
			root.Add(appearance);

			var streak = VectorFieldInspectorUI.MakeSection("Streak (shared)", ViewKey("streak"));
			streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("textureRotation"), "Texture Rotation"));
			streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("brightness"), "Brightness"));
			streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("gridCellCount"), "Grid Cell Count"));
			streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("rect"), "Rect"));
			streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("arrayResolution"), "Array Resolution"));
			root.Add(streak);

			var sampling = VectorFieldInspectorUI.MakeSection("Flow Sampling", ViewKey("sampling"));
			sampling.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("flowSamplingMode"), "Mode"));
			sampling.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("continuousAmplitude"), "Continuous Amplitude"));
			sampling.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("seamBand"), "Seam Band"));
			sampling.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("seamReach"), "Seam Reach"));
			sampling.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("seamDebug"), "Seam Debug"));
			root.Add(sampling);
		}
	}
}
