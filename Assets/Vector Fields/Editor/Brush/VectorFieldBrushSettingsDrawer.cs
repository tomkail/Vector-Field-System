using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace VectorFields {
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

		// A radial burst — four arrows fanning outward from a centre dot, i.e. the field emitted radially from a point.
		// Reads as "emit from here" (distinct from Directional's single parallel arrow) without implying rotation the way a
		// swirl does; the vortexAngle still tips it toward a vortex, but the base concept is point emission.
		static void DrawSpotIcon(Painter2D p, Rect r, Color color) {
			BeginStroke(p, color);
			Vector2 c = r.center;
			float size = Mathf.Min(r.width, r.height);
			float outer = size * 0.42f;   // arrow tip radius
			float inner = size * 0.16f;   // ray start radius (gap around the centre dot)
			float head = size * 0.13f;

			// Four arrows along the diagonals — diagonals keep it from reading as a 4-way "move" icon.
			p.BeginPath();
			foreach (float deg in new[] { 45f, 135f, 225f, 315f }) {
				float a = Mathf.Deg2Rad * deg;
				var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
				var perp = new Vector2(-dir.y, dir.x);
				var tip = c + dir * outer;
				p.MoveTo(c + dir * inner);
				p.LineTo(tip);
				p.MoveTo(tip - dir * head + perp * head);
				p.LineTo(tip);
				p.LineTo(tip - dir * head - perp * head);
			}
			p.Stroke();

			// Centre dot (small filled diamond) — the point the field emits from.
			float d = size * 0.09f;
			p.fillColor = color;
			p.BeginPath();
			p.MoveTo(c + new Vector2(0f, -d));
			p.LineTo(c + new Vector2(d, 0f));
			p.LineTo(c + new Vector2(0f, d));
			p.LineTo(c + new Vector2(-d, 0f));
			p.ClosePath();
			p.Fill();
		}
	}
}
