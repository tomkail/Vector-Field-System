using UnityEngine.UIElements;
using UnityEditor;

// Adds the Texture Renderer's optional material override to the shared VectorFieldQuad chrome (Field / … / Placement).
[CustomEditor(typeof(VectorFieldTextureRenderer)), CanEditMultipleObjects]
public class VectorFieldTextureRendererEditor : VectorFieldQuadEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Material", ViewKey("material"));
		section.Add(VectorFieldInspectorUI.Help(
			"Leave empty to keep the material already on the MeshRenderer (the common case); assign one to have the " +
			"renderer drive its material too."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("materialPrefab"), "Material"));
		root.Add(section);
	}
}
