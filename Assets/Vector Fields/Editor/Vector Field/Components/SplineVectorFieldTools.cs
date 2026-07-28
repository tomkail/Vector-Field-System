namespace VectorFields {
	// Scene-view editor tools for SplineVectorFieldComponent's per-point SplineData channels. Both tools follow the
	// splines package's sample-tool pattern: the package's SplineDataHandles.DataPointHandles supplies add/move/remove of
	// data points along the spline (left-click empty spline = add, drag point = slide along spline, right-click = delete),
	// and each tool draws its own value handles on top — perpendicular width sliders for widthAlongSpline, rotation discs
	// for rotationAlongSpline. While a tool is inactive but the field is selected, it draws a faint read-only ghost of its
	// data (IDrawSelectedHandles), so the width envelope stays visible while editing knots or the other channel.
	// Compiled only while the optional com.unity.splines package is installed (same define as the component).
	#if VECTOR_FIELDS_SPLINES
	using UnityEditor;
	using UnityEditor.EditorTools;
	using UnityEditor.Splines;
	using UnityEngine;
	using UnityEngine.Splines;

	// Shared scaffolding: resolves the container, walks every spline with a world-space NativeSpline, and converts
	// between world space and the field's local plane (widths are authored in field-local units, so handles must measure
	// distances there, not in world units).
	abstract class SplineVectorFieldToolBase : EditorTool, IDrawSelectedHandles {
		protected const float handleSize = 0.15f;

		protected SplineVectorFieldComponent Field => target as SplineVectorFieldComponent;

		public override void OnToolGUI(EditorWindow window) => Draw(false);

		public void OnDrawHandles() {
			// When this tool is active OnToolGUI already draws everything; the ghost is only for when it isn't.
			if (ToolManager.IsActiveTool(this)) return;
			Draw(true);
		}

		void Draw(bool ghost) {
			var field = Field;
			if (field == null) return;
			var container = field.Container;
			if (container == null) return;

			if (!ghost) Undo.RecordObject(field, undoName);

			var splines = container.Splines;
			for (int i = 0; i < splines.Count; i++) {
				var spline = splines[i];
				if (spline == null || spline.Count < 2) continue;
				using var nativeSpline = new NativeSpline(spline, container.transform.localToWorldMatrix);
				DrawSpline(field, spline, nativeSpline, i, ghost);
			}
		}

		protected abstract string undoName { get; }
		protected abstract void DrawSpline(SplineVectorFieldComponent field, Spline spline, NativeSpline nativeSpline, int splineIndex, bool ghost);

		// The world-space direction perpendicular to `worldTangent` within the field's local plane, scaled so one unit of
		// it spans one field-local unit — offsetting a world point by (this × w) moves it w field-local units off the
		// path, matching how the generator measures width. Zero when the tangent has no in-plane component.
		protected static Vector3 FieldPlanePerpendicular(SplineVectorFieldComponent field, Vector3 worldTangent) {
			var localTangent = field.transform.InverseTransformVector(worldTangent);
			var perp = new Vector2(-localTangent.y, localTangent.x);
			if (perp.sqrMagnitude < 1e-10f) return Vector3.zero;
			perp.Normalize();
			return field.transform.TransformVector(new Vector3(perp.x, perp.y, 0f));
		}

		// A world-space offset from a path point, measured in the field's local plane (the units widths are stored in).
		protected static float FieldPlaneDistance(SplineVectorFieldComponent field, Vector3 worldDelta) {
			var local = field.transform.InverseTransformVector(worldDelta);
			return new Vector2(local.x, local.y).magnitude;
		}

		// Draws the field's width envelope: the two edge polylines at ±width either side of the path, sampled like the
		// renderer samples the spline.
		protected static void DrawWidthEnvelope(SplineVectorFieldComponent field, Spline spline, NativeSpline nativeSpline) {
			if (Event.current.type != EventType.Repaint) return;
			int count = Mathf.Max(2, field.samplesPerSpline);
			var left = new Vector3[count];
			var right = new Vector3[count];
			for (int i = 0; i < count; i++) {
				float t = i / (float)(count - 1);
				nativeSpline.Evaluate(t, out var position, out var tangent, out _);
				var perpendicular = FieldPlanePerpendicular(field, tangent);
				float w = field.WidthAt(spline, t);
				left[i] = (Vector3)position + perpendicular * w;
				right[i] = (Vector3)position - perpendicular * w;
			}
			Handles.DrawAAPolyLine(2f, left);
			Handles.DrawAAPolyLine(2f, right);
		}

		// Iterates a SplineData channel's points, resolving each to a world position/tangent, and lets the concrete tool
		// draw a value handle there; a changed value is written back (the component's parameter hash picks up SplineData
		// edits, so the field re-renders without an explicit dirty call).
		protected void DrawDataPointHandles(SplineVectorFieldComponent field, Spline spline, NativeSpline nativeSpline, SplineData<float> data, bool ghost) {
			for (int i = 0; i < data.Count; i++) {
				var dataPoint = data[i];
				float t = SplineUtility.GetNormalizedInterpolation(nativeSpline, dataPoint.Index, data.PathIndexUnit);
				nativeSpline.Evaluate(t, out var position, out var tangent, out _);
				if (((Vector3)tangent).sqrMagnitude < 1e-12f) continue;
				if (DrawValueHandle(field, spline, t, position, tangent, dataPoint.Value, ghost, out float newValue)) {
					dataPoint.Value = newValue;
					data[i] = dataPoint;
				}
			}
		}

		protected abstract bool DrawValueHandle(SplineVectorFieldComponent field, Spline spline, float t,
			Vector3 position, Vector3 tangent, float value, bool ghost, out float newValue);

		protected static Color HandleColor(int id1, int id2) {
			if (GUIUtility.hotControl == id1 || GUIUtility.hotControl == id2) return Handles.selectedColor;
			if (GUIUtility.hotControl == 0 && (HandleUtility.nearestControl == id1 || HandleUtility.nearestControl == id2)) return Handles.preselectionColor;
			return Handles.color;
		}
	}

	// Edits widthAlongSpline: at each data point, a slider either side of the path sits at the point's actual half-width
	// (base width × the point's multiplier) and dragging it writes the multiplier back. The envelope polylines show the
	// interpolated result along the whole path.
	[EditorTool("Vector Field Width", typeof(SplineVectorFieldComponent))]
	class SplineVectorFieldWidthTool : SplineVectorFieldToolBase {
		GUIContent icon;
		public override GUIContent toolbarIcon => icon ??= new GUIContent(EditorGUIUtility.IconContent("ScaleTool").image,
			"Vector Field Width — click the spline to add width points, drag their side handles to set the field's width there. Right-click a point to delete it.");

		protected override string undoName => "Edit Vector Field Width";

		protected override void DrawSpline(SplineVectorFieldComponent field, Spline spline, NativeSpline nativeSpline, int splineIndex, bool ghost) {
			Handles.color = ghost ? new Color(0.3f, 0.7f, 1f, 0.4f) : new Color(0.3f, 0.7f, 1f, 1f);
			DrawWidthEnvelope(field, spline, nativeSpline);
			DrawDataPointHandles(field, spline, nativeSpline, field.widthAlongSpline, ghost);
			if (!ghost) nativeSpline.DataPointHandles(field.widthAlongSpline, false, splineIndex);
		}

		protected override bool DrawValueHandle(SplineVectorFieldComponent field, Spline spline, float t,
			Vector3 position, Vector3 tangent, float value, bool ghost, out float newValue) {
			newValue = value;
			var perpendicular = FieldPlanePerpendicular(field, tangent);
			if (perpendicular == Vector3.zero) return false;

			float halfWidth = field.width * value;
			var extremity1 = position + perpendicular * halfWidth;
			var extremity2 = position - perpendicular * halfWidth;

			if (ghost) {
				if (Event.current.type == EventType.Repaint) Handles.DrawLine(extremity1, extremity2);
				return false;
			}

			int id1 = GUIUtility.GetControlID(FocusType.Passive);
			int id2 = GUIUtility.GetControlID(FocusType.Passive);
			using (new Handles.DrawingScope(HandleColor(id1, id2))) {
				if (Event.current.type == EventType.Repaint) Handles.DrawLine(extremity1, extremity2);
				var direction = perpendicular.normalized;
				float size = handleSize * 0.5f * HandleUtility.GetHandleSize(position);
				var value1 = Handles.Slider(id1, extremity1, direction, size, Handles.CubeHandleCap, 0f);
				var value2 = Handles.Slider(id2, extremity2, direction, size, Handles.CubeHandleCap, 0f);
				var sceneView = SceneView.currentDrawingSceneView;
				var labelUp = sceneView != null ? sceneView.camera.transform.up : Vector3.up;
				Handles.Label(extremity1 + 2f * size * labelUp, (field.width * value).ToString("0.###"));

				// The multiplier is the dragged half-width re-expressed against the base width; with no base width there
				// is nothing to scale, so the handles are display-only until the user sets Width in the inspector.
				if (field.width <= 1e-5f) return false;
				if (GUIUtility.hotControl == id1 && (value1 - extremity1).sqrMagnitude > 0f) {
					newValue = FieldPlaneDistance(field, value1 - position) / field.width;
					return true;
				}
				if (GUIUtility.hotControl == id2 && (value2 - extremity2).sqrMagnitude > 0f) {
					newValue = FieldPlaneDistance(field, value2 - position) / field.width;
					return true;
				}
			}
			return false;
		}
	}

	// Edits rotationAlongSpline: at each data point, a disc around the field's plane normal sets the rotation offset the
	// flow reaches at the field's edge (scaled by signed distance from the path in between — see the component docs). The
	// filled arc previews the authored angle from the local flow direction.
	[EditorTool("Vector Field Rotation", typeof(SplineVectorFieldComponent))]
	class SplineVectorFieldRotationTool : SplineVectorFieldToolBase {
		GUIContent icon;
		public override GUIContent toolbarIcon => icon ??= new GUIContent(EditorGUIUtility.IconContent("RotateTool").image,
			"Vector Field Rotation — click the spline to add rotation points, drag their discs to set the edge rotation offset there. Right-click a point to delete it.");

		protected override string undoName => "Edit Vector Field Rotation";

		protected override void DrawSpline(SplineVectorFieldComponent field, Spline spline, NativeSpline nativeSpline, int splineIndex, bool ghost) {
			Handles.color = ghost ? new Color(1f, 0.7f, 0.3f, 0.4f) : new Color(1f, 0.7f, 0.3f, 1f);
			DrawDataPointHandles(field, spline, nativeSpline, field.rotationAlongSpline, ghost);
			if (!ghost) nativeSpline.DataPointHandles(field.rotationAlongSpline, false, splineIndex);
		}

		protected override bool DrawValueHandle(SplineVectorFieldComponent field, Spline spline, float t,
			Vector3 position, Vector3 tangent, float value, bool ghost, out float newValue) {
			newValue = value;
			var normal = field.transform.forward;

			// Size the disc to the field's width there when it has one (that's where the value takes full effect), else
			// fall back to a screen-relative size.
			var perpendicular = FieldPlanePerpendicular(field, tangent);
			float radius = perpendicular != Vector3.zero ? perpendicular.magnitude * field.WidthAt(spline, t) : 0f;
			if (radius < 1e-5f) radius = HandleUtility.GetHandleSize(position);

			var flowDirection = ((Vector3)tangent).normalized;
			if (Event.current.type == EventType.Repaint) {
				var arcColor = Handles.color;
				arcColor.a *= 0.25f;
				using (new Handles.DrawingScope(arcColor))
					Handles.DrawSolidArc(position, normal, flowDirection, value, radius);
				Handles.Label(position, value.ToString("0.#") + "°");
			}
			if (ghost) return false;

			EditorGUI.BeginChangeCheck();
			var startRotation = Quaternion.AngleAxis(value, normal);
			var newRotation = Handles.Disc(startRotation, position, normal, radius, false, 0f);
			if (EditorGUI.EndChangeCheck()) {
				// The disc only ever rotates about `normal`, so the delta collapses to a single signed angle around it.
				(newRotation * Quaternion.Inverse(startRotation)).ToAngleAxis(out float angle, out Vector3 axis);
				if (angle > 180f) { angle = 360f - angle; axis = -axis; }
				if (angle != 0f) {
					newValue = value + angle * Mathf.Sign(Vector3.Dot(axis, normal));
					return true;
				}
			}
			return false;
		}
	}
	#endif
}
