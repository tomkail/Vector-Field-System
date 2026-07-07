using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// Shared inspector for every VectorFieldQuad. editorForChildClasses:true means VectorFieldTextureRenderer and
// VectorFieldFlowIBFV pick this up automatically (no editor of their own); FlowAlignedTextureRendererEditor C#-inherits
// it to add its ranged curve while keeping this behaviour. It renders the default inspector (one PropertyField per
// visible property, script field disabled) and hides depthOffset while matchFieldBounds is off — the offset is applied
// inside MatchFieldRect, so it does nothing when the quad isn't following the field.
[CustomEditor(typeof(VectorFieldQuad), editorForChildClasses: true), CanEditMultipleObjects]
public class VectorFieldQuadEditor : Editor {
	public override VisualElement CreateInspectorGUI() {
		var root = new VisualElement();
		VisualElement depthOffsetField = null;

		var it = serializedObject.GetIterator();
		if (it.NextVisible(true)) {
			do {
				var element = BuildField(it.Copy());
				if (it.propertyPath == "m_Script") element.SetEnabled(false);
				if (it.name == "depthOffset") depthOffsetField = element;
				root.Add(element);
			} while (it.NextVisible(false));
		}

		// Only meaningful while we own the transform — hide it otherwise so it doesn't read as a live control.
		var matchProp = serializedObject.FindProperty("matchFieldBounds");
		if (depthOffsetField != null && matchProp != null)
			VectorFieldInspectorUI.ShowIf(depthOffsetField, matchProp, () => matchProp.boolValue);

		return root;
	}

	// Per-property element. Subclasses override to customise specific fields (e.g. a ranged curve) while keeping the
	// default rendering — and the depthOffset gating above — for everything else.
	protected virtual VisualElement BuildField(SerializedProperty property) => new PropertyField(property);
}
