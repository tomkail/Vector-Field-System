using UnityEngine.UIElements;
using UnityEditor;

// FlowMapRenderer: Texture Renderer chrome (Field / Material / Placement), the single water look, and the
// shared flow styling — all plain fields (the minimal counterpart to the tiered renderer's LODGroup-style editor).
[CustomEditor(typeof(FlowMapRenderer)), CanEditMultipleObjects]
public class FlowMapRendererEditor : VectorFieldTextureRendererEditor {
	protected override void BuildBody(VisualElement root) {
		base.BuildBody(root); // Material section

		var water = VectorFieldInspectorUI.MakeSection("Water Flow", ViewKey("water"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("waterTexture"), "Water Texture"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("tiling"), "Tiling"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("flowStrength"), "Flow Strength"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("flowSpeed"), "Flow Speed"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("dualScale"), "Second Layer"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailTiling"), "Detail Tiling"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("detailSpeed"), "Detail Speed"));
		water.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("tint"), "Tint"));
		root.Add(water);

		var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
		VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
		root.Add(appearance);
	}
}
