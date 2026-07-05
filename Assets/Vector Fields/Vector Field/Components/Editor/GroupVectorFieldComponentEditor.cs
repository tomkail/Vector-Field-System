using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

[CustomEditor(typeof(GroupVectorFieldComponent)), CanEditMultipleObjects]
public class GroupVectorFieldComponentEditor : VectorFieldComponentEditor {
	protected override void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Layers", ViewKey("layers"));
		section.Add(VectorFieldInspectorUI.Help(
			"Layers are gathered from child vector fields, in hierarchy order (top = blended first). Reorder them in the Hierarchy to change blend order."));

		var list = new VisualElement();
		section.Add(list);
		root.Add(section);

		var layersProp = serializedObject.FindProperty("layers");
		if (layersProp == null) return;

		int builtCount = -1;
		void Rebuild() {
			list.Clear();
			if (layersProp.arraySize == 0)
				list.Add(VectorFieldInspectorUI.Help("No child vector fields yet — add vector field components as children of this object."));
			for (int i = 0; i < layersProp.arraySize; i++)
				list.Add(BuildLayerCard(layersProp.GetArrayElementAtIndex(i), i));
			builtCount = layersProp.arraySize;
		}
		Rebuild();

		// The layer list is rebuilt from the children on every render (RefreshLayers), so its size can change under us.
		// Bound child fields update their own values; we only need to re-lay-out the cards when the count changes.
		list.schedule.Execute(() => {
			serializedObject.Update();
			if (layersProp.arraySize != builtCount) Rebuild();
		}).Every(300);
	}

	static VisualElement BuildLayerCard(SerializedProperty layer, int index) {
		var card = new VisualElement();
		card.AddToClassList("vf-subsection");

		var component = layer.FindPropertyRelative("component");
		var name = component.objectReferenceValue != null ? component.objectReferenceValue.name : $"Layer {index}";
		var title = new Label(name);
		title.AddToClassList("vf-subsection__title");
		card.Add(title);

		card.Add(new PropertyField(layer.FindPropertyRelative("strength"), "Strength"));
		card.Add(VectorFieldInspectorUI.EnumSegmentedField(layer.FindPropertyRelative("blendMode"), "Blend Mode"));
		card.Add(new PropertyField(layer.FindPropertyRelative("components"), "Affects"));
		card.Add(new PropertyField(layer.FindPropertyRelative("alignmentRamp"), "Alignment Ramp"));
		card.Add(new PropertyField(layer.FindPropertyRelative("scaleByFieldMagnitude"), "Scale By Field Magnitude"));
		return card;
	}
}
