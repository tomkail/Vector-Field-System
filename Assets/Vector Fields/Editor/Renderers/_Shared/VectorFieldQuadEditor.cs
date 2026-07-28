using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace VectorFields {
	// Base UI Toolkit inspector for the quad field renderers (VectorFieldQuad subclasses), built in the plugin's card
	// style (see VectorFieldInspectorUI). It lays down the shared chrome — a "Field" section for the field reference and
	// a "Placement" section for matchFieldBounds / depthOffset (depthOffset hidden while we're not following the field) —
	// and calls BuildBody() so per-type editors can slot their own sections in between. editorForChildClasses:true so any
	// subclass without its own editor still gets this look.
	[CustomEditor(typeof(VectorFieldQuad), editorForChildClasses: true), CanEditMultipleObjects]
	public class VectorFieldQuadEditor : Editor {
		public override VisualElement CreateInspectorGUI() {
			var root = new VisualElement();
			VectorFieldInspectorUI.ApplyStyle(root);
			root.Add(BuildFieldSection());
			BuildBody(root);
			BuildPlacementSection(root);
			return root;
		}

		// Serialized name of the field-reference property. Overridden where it differs (IBFV predates the shared name).
		protected virtual string FieldPropertyName => "_vectorFieldComponent";

		VectorFieldInspectorUI.Section BuildFieldSection() {
			var section = VectorFieldInspectorUI.MakeSection("Field", ViewKey("field"));
			var prop = serializedObject.FindProperty(FieldPropertyName);
			if (prop != null)
				section.Add(VectorFieldInspectorUI.Field(prop, "Vector Field", "The vector field this renderer displays."));
			return section;
		}

		// Per-type editors add their own sections here (material, appearance, look…); they land between Field and Placement.
		protected virtual void BuildBody(VisualElement root) { }

		void BuildPlacementSection(VisualElement root) {
			var match = serializedObject.FindProperty("matchFieldBounds");
			if (match == null) return;

			var section = VectorFieldInspectorUI.MakeSection("Placement", ViewKey("placement"));
			var matchField = VectorFieldInspectorUI.Field(match, "Match Field Bounds",
				"Pin the quad over the field's world rect every frame (position, rotation and size). Turn off to place and " +
				"size the quad yourself — the renderer then never touches the transform.");
			// Ticking this on snaps the transform onto the field. Do that move here, synchronously, inside an
			// Undo.RecordObject scope collapsed into the toggle's own undo step — otherwise the reposition happens in the
			// next LateUpdate (outside undo) and undoing the checkbox leaves the transform stranded where it was snapped.
			matchField.RegisterCallback<ChangeEvent<bool>>(evt => {
				if (!evt.newValue) return;
				int group = Undo.GetCurrentGroup();
				foreach (var t in targets) {
					var quad = (VectorFieldQuad)t;
					Undo.RecordObject(quad.transform, "Match Field Bounds");
					quad.SnapToFieldBounds();
				}
				Undo.CollapseUndoOperations(group);
			});
			section.Add(matchField);

			var depth = serializedObject.FindProperty("depthOffset");
			if (depth != null) {
				var depthField = VectorFieldInspectorUI.Field(depth, "Depth Offset",
					"Shift the quad along the field's plane normal for draw-order control.");
				VectorFieldInspectorUI.ShowIf(depthField, match, () => match.boolValue);
				section.Add(depthField);
			}
			root.Add(section);
		}

		// viewDataKey scoped to the concrete type so section expand/collapse persists per renderer type.
		protected string ViewKey(string suffix) => $"VF.{target.GetType().Name}.{suffix}";
	}
}
