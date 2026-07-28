using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// Groups the IBFV renderer's buffers and look controls under the shared VectorFieldQuad chrome (Field / … / Placement).
	// IBFV's field reference predates the shared name, so point the base at it.
	[CustomEditor(typeof(VectorFieldFlowIBFV)), CanEditMultipleObjects]
	public class VectorFieldFlowIBFVEditor : VectorFieldQuadEditor {
		protected override string FieldPropertyName => "vectorFieldComponent";

		protected override void BuildBody(VisualElement root) {
			var buffers = VectorFieldInspectorUI.MakeSection("Buffers", ViewKey("buffers"));
			buffers.Add(VectorFieldInspectorUI.Help("Prototype (IBFV, van Wijk 2002). Materials, noise and buffers auto-generate if left empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("ibfvMaterial"), "Advect Material",
				"Material using the \"Vector Fields/IBFV/IBFV\" shader (the feedback pass). Auto-created if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("presentMaterial"), "Present Material",
				"Material using the \"…IBFV Present\" shader that colours the buffer at display time. Auto-created if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseTexture"), "Noise Texture",
				"Noise injected each frame. Auto-generated white noise if empty."));
			buffers.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("resolution"), "Resolution",
				"Size of the ping-pong accumulation buffers."));
			root.Add(buffers);

			var appearance = VectorFieldInspectorUI.MakeSection("Appearance", ViewKey("appearance"));
			VectorFieldInspectorUI.AddChildrenInline(appearance, serializedObject.FindProperty("style"));
			root.Add(appearance);

			var look = VectorFieldInspectorUI.MakeSection("Look", ViewKey("look"));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("flowStep"), "Flow Step",
				"How far the feedback buffer is advected along the flow each frame, in UV units. Bigger = faster / longer streaks."));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseAmount"), "Noise Amount",
				"Fraction of fresh noise injected each frame. Lower = longer-lived streaks; too low blurs to grey."));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseScale"), "Noise Scale",
				"Tiling of the injection noise across the quad. Higher = finer streaks."));
			look.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("noiseRate"), "Noise Rate",
				"Twinkle speed (cycles/sec) — the coherence that lets advection draw the noise into streaks."));
			root.Add(look);
		}
	}
}
