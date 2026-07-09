using UnityEngine.UIElements;
using UnityEditor;

// LICTextureRenderer: the Texture Renderer chrome (Field / Material / Placement), the shared flow styling
// (VectorFieldFlowStyle), and the LIC look settings — all driven from the component. Per-field [Tooltip]s carry through
// the PropertyFields, so labels here stay terse.
[CustomEditor(typeof(LICTextureRenderer)), CanEditMultipleObjects]
public class LICTextureRendererEditor : VectorFieldTextureRendererEditor {
	protected override void BuildBody(VisualElement root) {
		base.BuildBody(root); // Material section

		var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
		VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
		root.Add(appearance);

		var lic = VectorFieldInspectorUI.MakeSection("LIC", ViewKey("lic"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseTexture"), "Noise Texture"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseScale"), "Noise Scale"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("stepCount"), "Steps Per Side"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("stepLength"), "Step Length"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("phase"), "Phase"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("animSpeed"), "Anim Speed"));
		root.Add(lic);
	}
}
