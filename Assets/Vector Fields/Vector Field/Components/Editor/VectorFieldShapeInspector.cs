using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// Shared "Shape" section for the boundary-based fields (Polygon and Mesh), which expose the same sides / boundary
// flip / falloff / angle controls. Inner falloff only shows when the Inside side is active, outer only when Outside
// is; boundary flip only matters once a side is chosen.
public static class VectorFieldShapeInspector {

	public static VectorFieldInspectorUI.Section Build(SerializedObject so, string viewDataKey) {
		var section = VectorFieldInspectorUI.MakeSection("Shape", viewDataKey);

		var sides = so.FindProperty("sides");
		section.Add(new PropertyField(sides, "Sides"));

		var boundaryFlip = new PropertyField(so.FindProperty("boundaryFlip"), "Boundary Flip");
		section.Add(boundaryFlip);

		var inner = new PropertyField(so.FindProperty("innerFalloff"), "Inner Falloff");
		var outer = new PropertyField(so.FindProperty("outerFalloff"), "Outer Falloff");
		section.Add(inner);
		section.Add(outer);

		section.Add(new PropertyField(so.FindProperty("angle"), "Angle"));

		bool Has(PolygonVectorFieldGenerator.Sides s) => (sides.intValue & (int)s) != 0;
		VectorFieldInspectorUI.ShowIf(inner, sides, () => Has(PolygonVectorFieldGenerator.Sides.Inside));
		VectorFieldInspectorUI.ShowIf(outer, sides, () => Has(PolygonVectorFieldGenerator.Sides.Outside));
		VectorFieldInspectorUI.ShowIf(boundaryFlip, sides, () => sides.intValue != (int)PolygonVectorFieldGenerator.Sides.None);

		return section;
	}
}
