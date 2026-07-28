using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
	// "Shape" section for MeshVectorField's boundary controls: sides / boundary flip / falloff / angle. Inner falloff only
	// shows when the Inside side is active, outer only when Outside is; boundary flip only matters once a side is chosen.
	public static class VectorFieldShapeInspector {

		public static VectorFieldInspectorUI.Section Build(SerializedObject so, string viewDataKey) {
			var section = VectorFieldInspectorUI.MakeSection("Shape", viewDataKey);

			var sides = so.FindProperty("sides");
			section.Add(VectorFieldInspectorUI.EnumFlagsSegmentedField(sides, typeof(MeshVectorFieldGenerator.Sides), "Sides",
				"Which side(s) of the shape get a vector. Enable both for the whole grid."));

			var boundaryFlip = VectorFieldInspectorUI.Field(so.FindProperty("boundaryFlip"), "Boundary Flip",
				"By default both sides flow outward (continuous across the edge). Flip one side to make the field diverge from or converge on the outline.");
			section.Add(boundaryFlip);

			var inner = VectorFieldInspectorUI.Field(so.FindProperty("innerFalloff"), "Inner Falloff",
				"Distance (local units) over which the inside vectors fade from full strength at the edge to zero. 0 = constant strength.");
			var outer = VectorFieldInspectorUI.Field(so.FindProperty("outerFalloff"), "Outer Falloff",
				"Distance (local units) over which the outside vectors fade from full strength at the edge to zero. 0 = constant strength.");
			section.Add(inner);
			section.Add(outer);

			section.Add(VectorFieldInspectorUI.Field(so.FindProperty("angle"), "Angle",
				"Rotates each vector around the plane normal. 0° points toward the nearest edge; 90° circulates around the shape; 180° points away."));

			bool Has(MeshVectorFieldGenerator.Sides s) => (sides.intValue & (int)s) != 0;
			VectorFieldInspectorUI.ShowIf(inner, sides, () => Has(MeshVectorFieldGenerator.Sides.Inside));
			VectorFieldInspectorUI.ShowIf(outer, sides, () => Has(MeshVectorFieldGenerator.Sides.Outside));
			VectorFieldInspectorUI.ShowIf(boundaryFlip, sides, () => sides.intValue != (int)MeshVectorFieldGenerator.Sides.None);

			return section;
		}
	}
}
