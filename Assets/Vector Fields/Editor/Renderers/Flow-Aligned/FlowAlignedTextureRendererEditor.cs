using UnityEngine.UIElements;
using UnityEditor;

// FlowAlignedTextureRenderer: the Texture Renderer chrome (Field / Material / Placement), the shared flow styling
// (VectorFieldFlowStyle), and every Flow-Aligned look/sampling setting — all driven from the component. Per-field
// [Tooltip]s carry through the PropertyFields.
[CustomEditor(typeof(FlowAlignedTextureRenderer)), CanEditMultipleObjects]
public class FlowAlignedTextureRendererEditor : VectorFieldTextureRendererEditor {
	protected override void BuildBody(VisualElement root) {
		base.BuildBody(root); // Material section

		var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
		VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
		appearance.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("useTextureColor"), "Use Texture Colour",
			"Tint the speed colour by the streak texture's own RGB. Off = pure speed colour."));
		root.Add(appearance);

		var streak = VectorFieldInspectorUI.MakeSection("Streak", ViewKey("streak"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("streakTexture"), "Texture"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("textureScale"), "Texture Scale"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("textureRotation"), "Texture Rotation"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("brightness"), "Brightness"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("speed"), "Speed"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("gridCellCount"), "Grid Cell Count"));
		streak.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("rect"), "Rect"));
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
