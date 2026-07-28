using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
	[CustomEditor(typeof(DrawableVectorFieldComponent)), CanEditMultipleObjects]
	public class DrawableVectorFieldComponentEditor : VectorFieldComponentEditor {
		protected override void BuildBody(VisualElement root) {
			var storage = VectorFieldInspectorUI.MakeSection("Storage", ViewKey("storage"));

			var assetProp = serializedObject.FindProperty("sourceAsset");
			storage.Add(VectorFieldInspectorUI.Field(assetProp, "Source Asset",
				"Optional: store this field in a reusable asset instead of on the component. Leave empty to keep it on the component (saved in the scene)."));

			var componentHelp = VectorFieldInspectorUI.Help(
				"Stored on this component and saved in the scene. The scene format is set in Project Settings ▸ Vector Fields ▸ Storage.");
			var assetHelp = VectorFieldInspectorUI.Help(
				"Painting into the linked asset — the data lives in the asset and is reusable across components.");
			storage.Add(componentHelp);
			storage.Add(assetHelp);
			VectorFieldInspectorUI.ShowIf(componentHelp, assetProp, () => assetProp.objectReferenceValue == null);
			VectorFieldInspectorUI.ShowIf(assetHelp, assetProp, () => assetProp.objectReferenceValue != null);

			// Action buttons, wired explicitly. Clear acts on every selected field; Extract/Bake are
			// single-object mode switches, so they act on the primary target and swap by which mode you're in.
			var clear = new Button(() => {
				foreach (var t in targets)
					if (t is DrawableVectorFieldComponent d) d.Clear();
			}) { text = "Clear" };
			storage.Add(clear);

			var extract = new Button(() => { if (target is DrawableVectorFieldComponent d) d.ExtractToAsset(); }) { text = "Extract to Asset" };
			var bake = new Button(() => { if (target is DrawableVectorFieldComponent d) d.BakeIntoComponent(); }) { text = "Bake into Component" };
			storage.Add(extract);
			storage.Add(bake);
			VectorFieldInspectorUI.ShowIf(extract, assetProp, () => assetProp.objectReferenceValue == null);
			VectorFieldInspectorUI.ShowIf(bake, assetProp, () => assetProp.objectReferenceValue != null);

			root.Add(storage);
		}
	}
}
