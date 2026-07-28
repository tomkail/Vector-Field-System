using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
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

			// Read-only: layers are auto-gathered from the child hierarchy, so this isn't user-editable. Disabled like the
			// component Script field — you can still click it to ping/highlight the source object, just not reassign it.
			var componentField = VectorFieldInspectorUI.Field(component, "Field",
				"The child vector field this layer blends in (read-only). Click it to highlight the source object in the hierarchy.");
			componentField.SetEnabled(false);
			card.Add(componentField);
			card.Add(VectorFieldInspectorUI.Field(layer.FindPropertyRelative("strength"), "Strength",
				"This layer's blend weight (0 = no contribution, 1 = full)."));
			card.Add(VectorFieldInspectorUI.EnumSegmentedField(layer.FindPropertyRelative("blendMode"), "Blend Mode",
				"How this layer combines with the result beneath it. Add sums the vectors; Blend interpolates toward this layer by its strength."));
			card.Add(VectorFieldInspectorUI.EnumFlagsSegmentedField(layer.FindPropertyRelative("components"), typeof(VectorFieldCombiner.Component), "Affects",
				"Which parts of the vector this layer contributes — Magnitude, Direction, or both."));
			card.Add(VectorFieldInspectorUI.RangedCurveField(layer.FindPropertyRelative("alignmentRamp"), "Alignment Ramp", new UnityEngine.Rect(0, 0, 1, 1),
				"Scales the layer by how aligned it is with the field beneath it (left = opposed, right = aligned). A flat curve has no effect."));
			card.Add(VectorFieldInspectorUI.Field(layer.FindPropertyRelative("scaleByFieldMagnitude"), "Scale By Field Magnitude",
				"Multiply this layer by the underlying field's magnitude before blending, so it only acts where there's already flow."));
			return card;
		}
	}
}
