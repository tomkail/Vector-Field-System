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

	// The accent title bar at the top of every field inspector, showing the field type's friendly name.
	public static VisualElement Header(string title) {
		var header = new VisualElement();
		header.AddToClassList("vf-header");
		var mark = new VisualElement();
		mark.AddToClassList("vf-header__mark");
		header.Add(mark);
		var label = new Label(title);
		label.AddToClassList("vf-header__title");
		header.Add(label);
		return header;
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
	// path isn't resolved via reflection). Uses Unity's field USS classes so its label aligns with sibling fields.
	public static VisualElement EnumSegmentedField(SerializedProperty enumProp, string label) {
		var row = new VisualElement();
		row.AddToClassList("unity-base-field");
		row.style.flexDirection = FlexDirection.Row;

		var labelEl = new Label(label);
		labelEl.AddToClassList("unity-base-field__label");
		row.Add(labelEl);

		var group = new VisualElement();
		group.AddToClassList("unity-base-field__input");
		group.AddToClassList("vf-seg-group");
		row.Add(group);

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
		row.TrackPropertyValue(enumProp, _ => Sync());
		return row;
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
