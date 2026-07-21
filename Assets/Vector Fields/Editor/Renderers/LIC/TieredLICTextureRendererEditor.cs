using UnityEngine.UIElements;
using UnityEditor;

// TieredLICTextureRenderer: the Texture Renderer chrome (Field / Material / Placement), the shared flow styling, the
// global LIC settings, and the N speed tiers via the shared LODGroup-style tier bar (VectorFieldTierBarGUI).
[CustomEditor(typeof(TieredLICTextureRenderer)), CanEditMultipleObjects]
public class TieredLICTextureRendererEditor : VectorFieldTextureRendererEditor {
	VectorFieldTierBarGUI tierBar;

	protected override void BuildBody(VisualElement root) {
		base.BuildBody(root); // Material section

		tierBar ??= new VectorFieldTierBarGUI(serializedObject,
			new[] { "speed", "noiseTexture", "noiseScale", "stepLength", "animSpeed" },
			el => {
				el.FindPropertyRelative("noiseScale").floatValue = 2f;
				el.FindPropertyRelative("stepLength").floatValue = 0.003f;
				el.FindPropertyRelative("animSpeed").floatValue = 2f;
			},
			TieredLICTextureRenderer.MaxTiers);

		var tierSection = VectorFieldInspectorUI.MakeSection("Speed Tiers", ViewKey("tiers"));
		tierSection.Add(VectorFieldInspectorUI.Help("LIC looks keyed to flow speed (0 = still → 1 = Max Speed). Each pixel " +
			"convolves and blends the two tiers straddling its local speed (up to 2x the single-tier cost). Drag the handles " +
			"to position tiers; click a region to edit it; right-click to add or remove."));
		if (!serializedObject.isEditingMultipleObjects)
			tierSection.Add(new IMGUIContainer(tierBar.OnGUI)); // single-target only; mixed selections fall back to the shared sections
		root.Add(tierSection);

		var lic = VectorFieldInspectorUI.MakeSection("LIC (shared)", ViewKey("lic"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("stepCount"), "Steps Per Side"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("phase"), "Phase"));
		lic.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("arrayResolution"), "Array Resolution"));
		root.Add(lic);

		var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
		VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
		root.Add(appearance);
	}
}
