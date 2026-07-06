using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// Shared building blocks for the Vector Field component inspectors — the header, collapsible section "cards",
// help text, and the conditional-display helper. Every editor here builds its UI from these so the whole plugin
// reads as one consistent surface. Styling lives in VectorFieldInspector.uss (loaded by ApplyStyle).
public static class VectorFieldInspectorUI {

	// The shared stylesheet, found by name so it survives being moved. Cached across the domain reload.
	static StyleSheet _styleSheet;
	static StyleSheet StyleSheet {
		get {
			if (_styleSheet != null) return _styleSheet;
			foreach (var guid in AssetDatabase.FindAssets("t:StyleSheet VectorFieldInspector")) {
				var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
				if (sheet != null) return _styleSheet = sheet;
			}
			return null;
		}
	}

	public static void ApplyStyle(VisualElement root) {
		var sheet = StyleSheet;
		if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
	}

	// A collapsible card grouping related settings. Add fields straight to the returned element (its
	// contentContainer routes into the foldout body). viewDataKey persists the expand/collapse state.
	public static Section MakeSection(string title, string viewDataKey = null) => new Section(title, viewDataKey);

	// Muted, wrapping help line — use for the "what this means" context that used to live in HelpBoxes.
	public static Label Help(string text) {
		var label = new Label(text);
		label.AddToClassList("vf-help");
		return label;
	}

	// A bound PropertyField with an explicit label and tooltip. The tooltip is set on the field root; UITK's
	// tooltip event bubbles, so hovering the (empty-tooltip) label or control still surfaces it. Use this so every
	// field in the inspectors carries a hint. Fields whose runtime declaration already has [Tooltip] keep that too.
	public static PropertyField Field(SerializedProperty prop, string label, string tooltip = null) {
		var field = new PropertyField(prop, label);
		if (!string.IsNullOrEmpty(tooltip)) field.tooltip = tooltip;
		return field;
	}

	// Show/hide `element` whenever `gate` changes, per `predicate`. This is the conditional-display idiom that
	// replaces "always visible" contingent fields — e.g. hide a force mapping until a force field is assigned.
	public static void ShowIf(VisualElement element, SerializedProperty gate, Func<bool> predicate) {
		void Apply() => element.style.display = predicate() ? DisplayStyle.Flex : DisplayStyle.None;
		Apply();
		element.TrackPropertyValue(gate, _ => Apply());
	}

	// Same, but re-evaluated on any change to the whole object (for predicates spanning several properties).
	public static void ShowIf(VisualElement element, SerializedObject so, Func<bool> predicate) {
		void Apply() => element.style.display = predicate() ? DisplayStyle.Flex : DisplayStyle.None;
		Apply();
		element.TrackSerializedObjectValue(so, _ => Apply());
	}

	// A horizontal segmented toggle for an enum serialized property (the "side by side options" look), built as a
	// native UITK control. Unlike the IMGUI EnumButtonGroup drawer, this works inside list elements (its property
	// path isn't resolved via reflection). Built on BaseField<int> so its label auto-aligns with the inspector's
	// label column, exactly like a PropertyField.
	public static VisualElement EnumSegmentedField(SerializedProperty enumProp, string label, string tooltip = null) {
		var group = new VisualElement();
		group.AddToClassList("vf-seg-group");

		var control = new EnumSegmentedControl(label, group);
		if (!string.IsNullOrEmpty(tooltip)) control.tooltip = tooltip;

		var names = enumProp.enumDisplayNames;
		var buttons = new Button[names.Length];
		void Sync() {
			int idx = enumProp.enumValueIndex;
			for (int i = 0; i < buttons.Length; i++)
				buttons[i].EnableInClassList("vf-seg--active", i == idx);
		}
		for (int i = 0; i < names.Length; i++) {
			int captured = i;
			var button = new Button(() => {
				enumProp.enumValueIndex = captured;
				enumProp.serializedObject.ApplyModifiedProperties();
				Sync();
			}) { text = names[i] };
			button.AddToClassList("vf-seg");
			if (names.Length > 1) {
				if (i == 0) button.AddToClassList("vf-seg--first");
				else if (i == names.Length - 1) button.AddToClassList("vf-seg--last");
				else button.AddToClassList("vf-seg--mid");
			}
			buttons[i] = button;
			group.Add(button);
		}
		Sync();
		control.TrackPropertyValue(enumProp, _ => Sync());
		return control;
	}

	// A horizontal multi-toggle for a [Flags] enum serialized property — one button per single-bit flag, value is
	// the OR of the selected bits. Native replacement for UnityX's [EnumFlagsButtonGroup] drawer, so fields using it
	// no longer depend on that attribute. Pass the enum type so bit values are read directly (no reflection by path).
	public static VisualElement EnumFlagsSegmentedField(SerializedProperty flagsProp, System.Type enumType, string label, string tooltip = null) {
		var group = new VisualElement();
		group.AddToClassList("vf-seg-group");

		var control = new EnumSegmentedControl(label, group);
		if (!string.IsNullOrEmpty(tooltip)) control.tooltip = tooltip;

		// Single-bit flags only — skip None (0) and composite values like All.
		var bits = new System.Collections.Generic.List<(int value, string name)>();
		foreach (var v in System.Enum.GetValues(enumType)) {
			int iv = System.Convert.ToInt32(v);
			if (iv != 0 && (iv & (iv - 1)) == 0)
				bits.Add((iv, ObjectNames.NicifyVariableName(System.Enum.GetName(enumType, v))));
		}

		var buttons = new Button[bits.Count];
		void Sync() {
			int mask = flagsProp.enumValueFlag;
			for (int i = 0; i < bits.Count; i++)
				buttons[i].EnableInClassList("vf-seg--active", (mask & bits[i].value) != 0);
		}
		for (int i = 0; i < bits.Count; i++) {
			int bit = bits[i].value;
			var button = new Button(() => {
				flagsProp.enumValueFlag ^= bit;   // toggle this flag
				flagsProp.serializedObject.ApplyModifiedProperties();
				Sync();
			}) { text = bits[i].name };
			button.AddToClassList("vf-seg");
			if (bits.Count > 1) {
				if (i == 0) button.AddToClassList("vf-seg--first");
				else if (i == bits.Count - 1) button.AddToClassList("vf-seg--last");
				else button.AddToClassList("vf-seg--mid");
			}
			buttons[i] = button;
			group.Add(button);
		}
		Sync();
		control.TrackPropertyValue(flagsProp, _ => Sync());
		return control;
	}

	// A bound AnimationCurve field constrained to `ranges` — native replacement for UnityX's [CurveRange] drawer
	// (which just calls EditorGUI.CurveField with bounds). Being a real CurveField (a BaseField), its label aligns.
	public static VisualElement RangedCurveField(SerializedProperty curveProp, string label, Rect ranges, string tooltip = null) {
		var field = new CurveField(label) { ranges = ranges };
		field.BindProperty(curveProp);
		if (!string.IsNullOrEmpty(tooltip)) field.tooltip = tooltip;
		return field;
	}

	// A default-style inspector (one PropertyField per visible property, script field shown disabled) that renders
	// the named AnimationCurve property as a ranged CurveField. For simple components that only used [CurveRange]
	// and have no other custom UI, so they no longer depend on that attribute.
	public static VisualElement DefaultInspectorWithRangedCurve(SerializedObject so, string curvePropertyName, Rect ranges, string curveTooltip = null) {
		var root = new VisualElement();
		var it = so.GetIterator();
		if (it.NextVisible(true)) {
			do {
				if (it.propertyPath == "m_Script") {
					var script = new PropertyField(it.Copy());
					script.SetEnabled(false);
					root.Add(script);
				} else if (it.name == curvePropertyName) {
					root.Add(RangedCurveField(it.Copy(), it.displayName, ranges, curveTooltip));
				} else {
					root.Add(new PropertyField(it.Copy()));
				}
			} while (it.NextVisible(false));
		}
		return root;
	}

	// Thin BaseField wrapper: we drive the value ourselves (the button callbacks write the serialized enum), but
	// deriving from BaseField gives us the label element and the inspector's automatic label-column alignment.
	// The "unity-base-field__aligned" class is what the alignment pass looks for.
	class EnumSegmentedControl : BaseField<int> {
		public EnumSegmentedControl(string label, VisualElement input) : base(label, input) {
			AddToClassList("unity-base-field__aligned");
		}
	}

	// A card whose children are hosted inside a collapsible Foldout. contentContainer is overridden so callers
	// can Add() fields directly and have them land in the foldout body, while the card border wraps both.
	public class Section : VisualElement {
		readonly Foldout foldout;
		public override VisualElement contentContainer => foldout != null ? foldout.contentContainer : base.contentContainer;

		public Section(string title, string viewDataKey = null) {
			AddToClassList("vf-section");
			foldout = new Foldout { text = title, value = true };
			if (!string.IsNullOrEmpty(viewDataKey)) foldout.viewDataKey = viewDataKey;
			hierarchy.Add(foldout);
		}
	}
}
