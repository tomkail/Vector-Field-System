using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// TieredFlowLitRenderer: the Texture Renderer chrome (Field / Material / Placement), the global water look, and the N
	// speed tiers via the shared LODGroup-style tier bar (VectorFieldTierBarGUI). No flow-styling section — the lit shader
	// takes its surface/colour/specular from the material, not VectorFieldFlowStyle.
	[CustomEditor(typeof(TieredFlowLitRenderer)), CanEditMultipleObjects]
	public class TieredFlowLitRendererEditor : VectorFieldTextureRendererEditor {
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
				TieredFlowLitRenderer.MaxTiers);

			var tierSection = VectorFieldInspectorUI.MakeSection("Speed Tiers", ViewKey("tiers"));
			tierSection.Add(VectorFieldInspectorUI.Help("Water height looks keyed to flow speed (0 = still → 1 = Max Speed). Each " +
				"pixel height-blends the two tiers straddling its local speed before lighting. Drag the handles to position tiers; " +
				"click a region to edit it; right-click to add or remove. Surface/colour/specular live on the material."));
			if (!serializedObject.isEditingMultipleObjects)
				tierSection.Add(new IMGUIContainer(tierBar.OnGUI)); // single-target only; mixed selections fall back to the shared sections
			root.Add(tierSection);

			var water = VectorFieldInspectorUI.MakeSection("Water (shared)", ViewKey("water"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("dualScale"), "Second Layer"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailTiling"), "Detail Tiling"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailSpeed"), "Detail Speed"));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("maxSpeed"), "Max Speed",
				"Flow speed that maps to the top of the tier axis (tier position 1)."));
			water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("arrayResolution"), "Array Resolution"));
			root.Add(water);
		}
	}
}
