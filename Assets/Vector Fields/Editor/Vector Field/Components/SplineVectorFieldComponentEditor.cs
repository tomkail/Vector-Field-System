namespace VectorFields {
	// Compiled only while the optional com.unity.splines package is installed (same define as the component).
	#if VECTOR_FIELDS_SPLINES
	using UnityEditor;
	using UnityEditor.EditorTools;
	using UnityEngine.UIElements;

	[CustomEditor(typeof(SplineVectorFieldComponent)), CanEditMultipleObjects]
	public class SplineVectorFieldComponentEditor : VectorFieldComponentEditor {
		protected override void BuildBody(VisualElement root) {
			var spline = VectorFieldInspectorUI.MakeSection("Spline", ViewKey("spline"));
			var containerProp = serializedObject.FindProperty("splineContainer");
			spline.Add(VectorFieldInspectorUI.Field(containerProp, "Container",
				"The spline(s) to trace — every spline in the container contributes. Falls back to a SplineContainer on this GameObject when empty."));
			var noContainer = VectorFieldInspectorUI.Help("No container assigned — a SplineContainer on this GameObject will be used if present.");
			spline.Add(noContainer);
			VectorFieldInspectorUI.ShowIf(noContainer, containerProp, () => containerProp.objectReferenceValue == null);
			spline.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("samplesPerSpline"), "Samples Per Spline",
				"How finely each spline is flattened for the GPU each render. Raise it if tight curves look faceted."));
			root.Add(spline);

			var direction = VectorFieldInspectorUI.MakeSection("Direction", ViewKey("direction"));
			var modeProp = serializedObject.FindProperty("directionMode");
			direction.Add(VectorFieldInspectorUI.EnumSegmentedField(modeProp, "Mode",
				"Flow: vectors follow the path (the tangent at each cell's nearest point). Fixed: every cell uses the same direction."));
			var fixedDirection = VectorFieldInspectorUI.Field(serializedObject.FindProperty("fixedDirection"), "Fixed Direction",
				"The direction every cell uses, in this field's local plane space (normalized before use).");
			direction.Add(fixedDirection);
			VectorFieldInspectorUI.ShowIf(fixedDirection, modeProp,
				() => modeProp.enumValueIndex == (int)SplineVectorFieldGenerator.DirectionMode.Fixed);
			root.Add(direction);

			var rotation = VectorFieldInspectorUI.MakeSection("Rotation", ViewKey("rotation"));
			rotation.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("rotation"), "Rotation",
				"Rotates every vector around the plane normal, in degrees. Applied everywhere, in both direction modes."));
			rotation.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("rotationAlongSpline"), "Edge Rotation",
				"Extra rotation (degrees) the flow reaches at the field's edge, authored at points along the spline. Each cell " +
				"scales the value at its nearest point by its signed normalized distance from the path (0 on the path, ±1 at " +
				"the width edges), so positive values fan the flow outward and negative values pull it inward. Needs Width > 0."));
			AddSceneToolButton<SplineVectorFieldRotationTool>(rotation, "Edit Rotation in Scene",
				"Activate the scene-view rotation tool: click the spline to add points, drag their discs to set the edge rotation, right-click points to delete.");
			root.Add(rotation);

			var widthSection = VectorFieldInspectorUI.MakeSection("Width & Falloff", ViewKey("width"));
			widthSection.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("width"), "Width",
				"How far the field reaches either side of the path, in this field's local units. Distance from the path is " +
				"normalized against this for the falloff curve and edge rotation. 0 = no width: constant strength, no edge rotation."));
			widthSection.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("falloffCurve"), "Falloff",
				"Strength across the width: x is normalized distance from the path (0 = on it, 1 = at the width edge; the end " +
				"value holds beyond), y is the strength multiplier. The default linear 1→0 fades to nothing at the edge."));
			widthSection.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("widthAlongSpline"), "Width Along Spline",
				"Multiplier on Width authored at points along the spline and interpolated between them. Empty = 1 everywhere. " +
				"Easier to author with the scene-view width tool below."));
			AddSceneToolButton<SplineVectorFieldWidthTool>(widthSection, "Edit Width in Scene",
				"Activate the scene-view width tool: click the spline to add points, drag their side handles to set the width, right-click points to delete.");
			root.Add(widthSection);
		}

		// The tools also appear in the scene view's component-tools toolbar; the button is the discoverable path.
		void AddSceneToolButton<T>(VisualElement section, string label, string tooltip) where T : EditorTool {
			if (serializedObject.isEditingMultipleObjects) return;
			var button = new Button(ToolManager.SetActiveTool<T>) { text = label, tooltip = tooltip };
			section.Add(button);
		}
	}
	#endif
}
