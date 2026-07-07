using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// UI Toolkit drawer for the stamp brush: the force type (as a segmented, icon-labelled button group) plus only the
// angle that type uses — directionalAngle for Directional, vortexAngle for Spot. Used by the vector field inspectors
// (which are UITK) via PropertyField.
[CustomPropertyDrawer(typeof(VectorFieldBrushSettings))]
public class VectorFieldBrushSettingsDrawer : PropertyDrawer {

	public override VisualElement CreatePropertyGUI(SerializedProperty property) {
		var root = new VisualElement();

		var typeProp = property.FindPropertyRelative("forceType");
		// Icons are ordered to match ForceEmitterType: [0] Directional, [1] Spot.
		root.Add(VectorFieldInspectorUI.EnumSegmentedField(typeProp, "Force Type",
			new Action<Painter2D, Rect, Color>[] { DrawDirectionalIcon, DrawSpotIcon },
			"Directional pushes every cell the same way; Spot emits radially / as a vortex from the centre."));

		var directional = Indented(Tip(new PropertyField(property.FindPropertyRelative("directionalAngle"), "Angle"),
			"Direction of the push, in degrees."));
		var vortex = Indented(Tip(new PropertyField(property.FindPropertyRelative("vortexAngle"), "Vortex Angle"),
			"Swirl angle around the centre. 0° = straight out (source), 90° = pure vortex."));
		root.Add(directional);
		root.Add(vortex);

		bool Is(VectorFieldBrushSettings.ForceEmitterType t) => (VectorFieldBrushSettings.ForceEmitterType)typeProp.enumValueIndex == t;
		VectorFieldInspectorUI.ShowIf(directional, typeProp, () => Is(VectorFieldBrushSettings.ForceEmitterType.Directional));
		VectorFieldInspectorUI.ShowIf(vortex, typeProp, () => Is(VectorFieldBrushSettings.ForceEmitterType.Spot));

		return root;
	}

	static VisualElement Indented(VisualElement element) {
		element.style.marginLeft = 6;
		return element;
	}

	static PropertyField Tip(PropertyField field, string tooltip) {
		field.tooltip = tooltip;
		return field;
	}

	// Force-type glyphs, stroked into the segmented buttons. `color` follows the button's text colour (white when
	// active). UITK's y axis points down; these are drawn in that space. Round caps/joins keep the small strokes clean.
	static void BeginStroke(Painter2D p, Color color) {
		p.strokeColor = color;
		p.lineWidth = 1.5f;
		p.lineCap = LineCap.Round;
		p.lineJoin = LineJoin.Round;
	}

	// A rightward arrow — every cell pushed the same way.
	static void DrawDirectionalIcon(Painter2D p, Rect r, Color color) {
		BeginStroke(p, color);
		float cy = r.center.y;
		float x0 = r.xMin + r.width * 0.12f;
		float x1 = r.xMax - r.width * 0.12f;
		float head = r.height * 0.28f;
		p.BeginPath();
		p.MoveTo(new Vector2(x0, cy));
		p.LineTo(new Vector2(x1, cy));
		p.MoveTo(new Vector2(x1 - head, cy - head));
		p.LineTo(new Vector2(x1, cy));
		p.LineTo(new Vector2(x1 - head, cy + head));
		p.Stroke();
	}

	// A swirl — radial / vortex emission around the centre. An almost-closed circular arc with an arrowhead at its
	// leading end reads as rotation, approximated with line segments so it's independent of the Painter2D Arc API.
	static void DrawSpotIcon(Painter2D p, Rect r, Color color) {
		BeginStroke(p, color);
		Vector2 c = r.center;
		float radius = Mathf.Min(r.width, r.height) * 0.34f;
		const float startDeg = 40f, endDeg = 320f; // leave a gap so the arrowhead sits at an open end
		const int segments = 20;
		p.BeginPath();
		Vector2 last = default;
		for (int i = 0; i <= segments; i++) {
			float a = Mathf.Deg2Rad * Mathf.Lerp(startDeg, endDeg, i / (float)segments);
			last = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
			if (i == 0) p.MoveTo(last); else p.LineTo(last);
		}
		// Arrowhead tangent to the arc at its leading end (endDeg), swept clockwise in screen space.
		float end = Mathf.Deg2Rad * endDeg;
		var tangent = new Vector2(-Mathf.Sin(end), Mathf.Cos(end));
		var normal = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
		float head = radius * 0.7f;
		p.MoveTo(last - tangent * head + normal * head * 0.5f);
		p.LineTo(last);
		p.LineTo(last - tangent * head - normal * head * 0.5f);
		p.Stroke();
	}
}
