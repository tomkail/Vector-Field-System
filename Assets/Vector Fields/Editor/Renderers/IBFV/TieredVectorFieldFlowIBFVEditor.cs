using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// TieredVectorFieldFlowIBFV: the IBFV buffers/look chrome minus the global noise scale/amount (the tiers replace
	// those), plus the N speed tiers via the shared LODGroup-style tier bar (VectorFieldTierBarGUI).
	[CustomEditor(typeof(TieredVectorFieldFlowIBFV)), CanEditMultipleObjects]
	public class TieredVectorFieldFlowIBFVEditor : VectorFieldQuadEditor {
		protected override string FieldPropertyName => "vectorFieldComponent";

		VectorFieldTierBarGUI tierBar;

		protected override void BuildBody(VisualElement root) {
			tierBar ??= new VectorFieldTierBarGUI(serializedObject,
				new[] { "speed", "texture", "noiseScale", "noiseAmount" },
				el => {
					el.FindPropertyRelative("noiseScale").floatValue = 6f;
					el.FindPropertyRelative("noiseAmount").floatValue = 0.08f;
				},
				TieredVectorFieldFlowIBFV.MaxTiers);

			var buffers = VectorFieldInspectorUI.MakeSection("Buffers", ViewKey("buffers"));
			buffers.Add(VectorFieldInspectorUI.Help("Prototype (IBFV, van Wijk 2002). Materials, noise and buffers auto-generate if left empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("ibfvMaterial"), "Advect Material",
				"Material using the \"Vector Fields/IBFV/IBFV (Tiered)\" shader (the feedback pass). Auto-created if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("presentMaterial"), "Present Material",
				"Material using the \"…IBFV Present\" shader that colours the buffer at display time. Auto-created if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseTexture"), "Noise Texture",
				"Fallback injection noise for tiers with no texture assigned. Auto-generated white noise if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("resolution"), "Resolution",
				"Size of the ping-pong accumulation buffers."));
			root.Add(buffers);

			var tierSection = VectorFieldInspectorUI.MakeSection("Speed Tiers", ViewKey("tiers"));
			tierSection.Add(VectorFieldInspectorUI.Help("Injection-noise looks keyed to flow speed (0 = still → 1 = Max Speed). " +
				"Each pixel blends the two tiers straddling its local speed. Drag the handles to position tiers; click a region " +
				"to edit it; right-click to add or remove."));
			if (!serializedObject.isEditingMultipleObjects)
				tierSection.Add(new IMGUIContainer(tierBar.OnGUI)); // single-target only; mixed selections fall back to the shared sections
			root.Add(tierSection);

			var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
			VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
			root.Add(appearance);

			var look = VectorFieldInspectorUI.MakeSection("Look (shared)", ViewKey("look"));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("flowStep"), "Flow Step",
				"How far the feedback buffer is advected along the flow each frame, in UV units. Bigger = faster / longer streaks."));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseRate"), "Noise Rate",
				"Twinkle speed (cycles/sec) — the coherence that lets advection draw the noise into streaks."));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("arrayResolution"), "Array Resolution",
				"Resolution each tier noise texture is resampled to inside the packed array."));
			root.Add(look);
		}
	}
}
