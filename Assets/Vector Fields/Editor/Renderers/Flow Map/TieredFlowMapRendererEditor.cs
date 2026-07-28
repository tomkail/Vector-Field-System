using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// TieredFlowMapRenderer: the Texture Renderer chrome (Field / Material / Placement), the global water look, the shared
	// flow styling, and the N speed tiers via the shared LODGroup-style tier bar (VectorFieldTierBarGUI).
	[CustomEditor(typeof(TieredFlowMapRenderer)), CanEditMultipleObjects]
	public class TieredFlowMapRendererEditor : VectorFieldTextureRendererEditor {
		VectorFieldTierBarGUI tierBar;

		protected override void BuildBody(VisualElement root) {
			base.BuildBody(root); // Material section

			tierBar ??= new VectorFieldTierBarGUI(serializedObject,
				new[] { "speed", "texture", "tiling", "flowStrength", "flowSpeed" },
				el => {
					el.FindPropertyRelative("tiling").floatValue = 4f;
					el.FindPropertyRelative("flowStrength").floatValue = 0.3f;
					el.FindPropertyRelative("flowSpeed").floatValue = 1f;
				},
				TieredFlowMapRenderer.MaxTiers);

			var tierSection = VectorFieldInspectorUI.MakeSection("Speed Tiers", ViewKey("tiers"));
			tierSection.Add(VectorFieldInspectorUI.Help("Water looks keyed to flow speed (0 = still → 1 = Max Speed). Each pixel " +
				"blends the two tiers straddling its local speed. Drag the handles to position tiers; click a region to edit it; " +
				"right-click to add or remove."));
			if (!serializedObject.isEditingMultipleObjects)
				tierSection.Add(new IMGUIContainer(tierBar.OnGUI)); // single-target only; mixed selections fall back to the shared sections
			root.Add(tierSection);

			var water = VectorFieldInspectorUI.MakeSection("Water (shared)", ViewKey("water"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("dualScale"), "Second Layer"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailTiling"), "Detail Tiling"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailSpeed"), "Detail Speed"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("tint"), "Tint"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("arrayResolution"), "Array Resolution"));
			root.Add(water);

			var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
			VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
			root.Add(appearance);
		}
	}
}
