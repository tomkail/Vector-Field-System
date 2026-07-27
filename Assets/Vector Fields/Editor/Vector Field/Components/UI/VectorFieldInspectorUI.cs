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
		// USS can't detect the editor theme, so scope the sheet's theme-specific colours with a root class.
		root.EnableInClassList("vf-theme-dark", EditorGUIUtility.isProSkin);
		root.EnableInClassList("vf-theme-light", !EditorGUIUtility.isProSkin);
		ScheduleOverrideBarsFix(root);
	}

	// Unity 6000.5 workaround: for inspectors with a UITK custom editor, the default editor stylesheet gives the
	// InspectorElement's absolute prefab-override / live-property bar containers margin-left:-15px (aimed at the
	// window's gutter, but it overshoots the InspectorElement's left edge), which widens the inspector ScrollView's
	// scroll range by 15px and shows a phantom horizontal scrollbar on every vector field inspector. An inline
	// margin-left:0 beats the sheet and pins them flush to the InspectorElement, exactly like IMGUI inspectors.
	// Applied only when geometry shows the container out of place, so if Unity fixes the sheet this never triggers.
	static readonly string[] _barContainerNames = { "unity-prefab-override-bars-container", "unity-live-property-bars-container" };
	const string _barsFixMarkerClass = "vf-bars-fix-watched";
	static void ScheduleOverrideBarsFix(VisualElement root) {
		root.RegisterCallback<AttachToPanelEvent>(_ => {
			var inspectorElement = root.GetFirstAncestorOfType<InspectorElement>();
			if (inspectorElement == null) return;
			foreach (var name in _barContainerNames) {
				var bars = inspectorElement.Q(name);
				if (bars == null || bars.ClassListContains(_barsFixMarkerClass)) continue;
				bars.AddToClassList(_barsFixMarkerClass);
				bars.RegisterCallback<GeometryChangedEvent>(_2 => {
					if (bars.worldBound.x < inspectorElement.worldBound.x - 0.5f) bars.style.marginLeft = 0f;
				});
			}
		});
	}

	// A collapsible card grouping related settings. Add fields straight to the returned element (its
	// contentContainer routes into the foldout body). viewDataKey persists the expand/collapse state.
	public static Section MakeSection(string title, string viewDataKey = null) => new Section(title, viewDataKey);

	// Muted, wrapping help line — use for the "what this means" context.
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

	// Add each direct child of a serialized (nested [Serializable] class) property straight into `container`, so its
	// fields appear inline — without the extra foldout Unity draws for the parent. Use when the block already sits in a
	// titled Section (the section is the grouping, so the parent's own foldout is redundant). Per-field [Tooltip]s carry.
	public static void AddChildrenInline(VisualElement container, SerializedProperty parent) {
		if (parent == null) return;
		var it = parent.Copy();
		var end = parent.GetEndProperty();
		bool enterChildren = true;
		while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end)) {
			enterChildren = false;
			container.Add(new PropertyField(it.Copy()));
		}
	}

	// A Vector2Int grid-size field with a chain-link lock (like the Transform scale lock): while locked, editing
	// either axis mirrors onto the other so the grid stays square. `lockProp` is the serialized bool that persists
	// the lock; `sizeProp` is the Vector2Int size. The lock button is injected into the field's own input row so the
	// label stays aligned with sibling fields.
	public static VisualElement GridSizeField(SerializedProperty sizeProp, SerializedProperty constrainProp, string tooltip = null) {
		var field = new Vector2IntField("Grid Size");
		field.AddToClassList("unity-base-field__aligned"); // align the label with sibling PropertyFields
		field.BindProperty(sizeProp);
		if (!string.IsNullOrEmpty(tooltip)) field.tooltip = tooltip;

		var lockButton = new Button();
		lockButton.AddToClassList("vf-lock");
		// Render the icon through an Image (ScaleToFit) rather than a stretched background so it isn't distorted.
		var lockIcon = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
		lockIcon.AddToClassList("vf-lock__icon");
		lockButton.Add(lockIcon);

		void UpdateIcon() {
			lockIcon.image = EditorGUIUtility.IconContent(constrainProp.boolValue ? "Linked" : "Unlinked").image;
			lockButton.tooltip = constrainProp.boolValue ? "Disable constrained proportions" : "Enable constrained proportions";
		}

		lockButton.clicked += () => {
			constrainProp.boolValue = !constrainProp.boolValue;
			constrainProp.serializedObject.ApplyModifiedProperties();
			UpdateIcon();
		};

		// While constrained, scale the OTHER axis by the same factor as the edited one, preserving the X:Y ratio
		// (rounded to whole cells, min 1) — the Transform constrain-proportions behaviour. A re-entrancy guard stops
		// the value write from recursing.
		bool updating = false;
		field.RegisterValueChangedCallback(evt => {
			if (!constrainProp.boolValue || updating) return;
			var nv = evt.newValue;
			var ov = evt.previousValue;
			Vector2Int result;
			if (nv.x != ov.x && ov.x != 0) {
				float factor = (float)nv.x / ov.x;
				result = new Vector2Int(Mathf.Max(1, nv.x), Mathf.Max(1, Mathf.RoundToInt(ov.y * factor)));
			} else if (nv.y != ov.y && ov.y != 0) {
				float factor = (float)nv.y / ov.y;
				result = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(ov.x * factor)), Mathf.Max(1, nv.y));
			} else {
				return;
			}
			if (nv != result) {
				updating = true;
				field.value = result;
				updating = false;
			}
		});

		field.TrackPropertyValue(constrainProp, _ => UpdateIcon()); // keep the icon right on undo/redo

		// Sit the lock to the LEFT of the X/Y fields (like the Transform scale constrain-proportions lock): insert it
		// at the front of the field's input row, before X. A freshly-constructed composite field doesn't always have its
		// input row built yet (Q returns null until layout), so also retry on attach — otherwise the lock silently never
		// appears. Reparent into the input row once it exists so it lands in the gutter, not appended at the field's end.
		void InsertLock() {
			var input = field.Q(className: "unity-base-field__input");
			if (input != null) { if (lockButton.parent != input) input.Insert(0, lockButton); }
			else if (lockButton.parent == null) field.Add(lockButton);
		}

		// EditorGUIUtility.IconContent can transiently return a null image while a domain reload settles (right after a
		// recompile), which would leave the lock as an invisible icon-less button. So (re)insert AND refresh the icon on
		// construction, on attach, and once more on the next scheduled tick — by then IconContent is reliably populated.
		void Reconcile() { InsertLock(); UpdateIcon(); }
		Reconcile();
		field.RegisterCallback<AttachToPanelEvent>(_ => Reconcile());
		field.schedule.Execute(Reconcile);
		return field;
	}

	// The full resolution control: an Auto-resolution toggle that switches between the manual Grid Size field (with its
	// constrain-proportions lock) and a density field (cells per world unit) whose derived X×Y grid is shown read-only.
	// In Auto mode the grid size follows the transform scale so per-axis fidelity stays equal (see GridTransform).
	// `autoSizePreview` returns the size Auto mode would currently derive (typically target.grid.ComputeAutoSize()).
	public static VisualElement ResolutionField(SerializedProperty sizeProp, SerializedProperty constrainProp,
		SerializedProperty autoProp, SerializedProperty cellsProp, System.Func<Vector2Int> autoSizePreview,
		System.Func<Vector3> ownerLossyScale, string manualTooltip = null) {
		var container = new VisualElement();

		var autoToggle = new Toggle("Auto Resolution") {
			tooltip = "Derive the grid resolution from the transform scale (a constant number of cells per world unit), " +
			          "so a non-uniformly scaled field gets a matching non-square grid and equal fidelity on both axes."
		};
		autoToggle.AddToClassList("unity-base-field__aligned");
		autoToggle.BindProperty(autoProp);
		container.Add(autoToggle);

		var manual = GridSizeField(sizeProp, constrainProp, manualTooltip);

		var auto = new VisualElement();
		var cells = new FloatField("Cells / Unit") { tooltip = "Grid cells per world unit. Higher is more detailed." };
		cells.AddToClassList("unity-base-field__aligned");
		cells.BindProperty(cellsProp);
		auto.Add(cells);
		var derived = new Label { name = "vf-derived-size" };
		derived.AddToClassList("unity-base-field__aligned");
		derived.style.opacity = 0.7f;
		derived.style.marginLeft = 3;
		auto.Add(derived);

		void RefreshDerived() {
			if (autoSizePreview == null) return;
			var s = autoSizePreview();
			derived.text = $"Grid Size:  {s.x} × {s.y} cells";
		}

		void ApplyMode() {
			bool isAuto = autoProp.boolValue;
			manual.style.display = isAuto ? DisplayStyle.None : DisplayStyle.Flex;
			auto.style.display = isAuto ? DisplayStyle.Flex : DisplayStyle.None;
			if (isAuto) RefreshDerived();
		}

		container.Add(manual);
		container.Add(auto);
		ApplyMode();
		autoToggle.RegisterValueChangedCallback(evt => {
			// When enabling Auto, seed CellsPerUnit so the derived size matches the CURRENT size — otherwise a large
			// transform (e.g. lossyScale 200) instantly explodes to a huge/clamped map. Density = current cells per world
			// unit, averaged over both axes (a single density can't preserve a non-square grid on a uniform transform).
			if (evt.newValue) {
				var sz = sizeProp.vector2IntValue;
				var sc = ownerLossyScale != null ? ownerLossyScale() : Vector3.one;
				float ax = Mathf.Abs(sc.x), ay = Mathf.Abs(sc.y);
				float dx = ax > 1e-4f ? sz.x / ax : sz.x;
				float dy = ay > 1e-4f ? sz.y / ay : sz.y;
				cellsProp.floatValue = Mathf.Max(0.01f, (dx + dy) * 0.5f);
				cellsProp.serializedObject.ApplyModifiedProperties();   // flush with the toggle so no interim huge derive
			}
			ApplyMode();
		});
		container.TrackPropertyValue(autoProp, _ => ApplyMode());   // keep in sync on undo/redo
		// The derived size follows the transform scale (edited outside this inspector), so poll while Auto is visible.
		container.schedule.Execute(() => { if (autoProp.boolValue) RefreshDerived(); }).Every(200);
		return container;
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

	// As EnumSegmentedField, but each option also shows an icon drawn above/left of its label. `iconPainters[i]`
	// strokes option i's glyph into the given rect using the supplied colour, which follows the button's text colour
	// (so it turns white when the option is active). A null entry (or a shorter array) just omits that option's icon.
	public static VisualElement EnumSegmentedField(SerializedProperty enumProp, string label, Action<Painter2D, Rect, Color>[] iconPainters, string tooltip = null) {
		var group = new VisualElement();
		group.AddToClassList("vf-seg-group");

		var control = new EnumSegmentedControl(label, group);
		if (!string.IsNullOrEmpty(tooltip)) control.tooltip = tooltip;

		var names = enumProp.enumDisplayNames;
		var buttons = new Button[names.Length];
		var icons = new VisualElement[names.Length];
		void Sync() {
			int idx = enumProp.enumValueIndex;
			for (int i = 0; i < buttons.Length; i++) {
				buttons[i].EnableInClassList("vf-seg--active", i == idx);
				icons[i]?.MarkDirtyRepaint(); // re-stroke with the new (inherited) colour
			}
		}
		for (int i = 0; i < names.Length; i++) {
			int captured = i;
			var button = new Button(() => {
				enumProp.enumValueIndex = captured;
				enumProp.serializedObject.ApplyModifiedProperties();
				Sync();
			});
			button.AddToClassList("vf-seg");
			if (names.Length > 1) {
				if (i == 0) button.AddToClassList("vf-seg--first");
				else if (i == names.Length - 1) button.AddToClassList("vf-seg--last");
				else button.AddToClassList("vf-seg--mid");
			}

			var content = new VisualElement();
			content.AddToClassList("vf-seg__content");
			var painter = iconPainters != null && i < iconPainters.Length ? iconPainters[i] : null;
			if (painter != null) {
				var icon = new VisualElement { pickingMode = PickingMode.Ignore };
				icon.AddToClassList("vf-seg__icon");
				icon.generateVisualContent += mgc => painter(mgc.painter2D, icon.contentRect, icon.resolvedStyle.color);
				icons[i] = icon;
				content.Add(icon);
			}
			content.Add(new Label(names[i]));
			button.Add(content);

			buttons[i] = button;
			group.Add(button);
		}
		Sync();
		control.TrackPropertyValue(enumProp, _ => Sync());
		return control;
	}

	// A horizontal multi-toggle for a [Flags] enum serialized property — one button per single-bit flag, value is
	// the OR of the selected bits. A native control, with no dependency on UnityX's [EnumFlagsButtons] drawer.
	// Pass the enum type so bit values are read directly (no reflection by path).
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
	// the named AnimationCurve property as a ranged CurveField. For simple components that need a ranged curve and
	// have no other custom UI, without depending on the [CurveRange] attribute.
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

	// A collapsible section header, styled to match the Camera inspector's sections (full-width header bar, content
	// one indent deeper; colours from RP-core's HeaderFoldout theme sheets — see VectorFieldInspector.uss).
	// It IS a Foldout (not a wrapper around one) so it sits as a direct child of the inspector
	// — an extra wrapper made the full-width bleed report an over-wide extent and pushed the content column off. Callers
	// Add() fields straight in (Foldout routes them to its content body). Same shape as the Diagnostics foldout, which
	// also just tags a Foldout with the vf-section class.
	public class Section : Foldout {
		public Section(string title, string viewDataKey = null) {
			AddToClassList("vf-section");
			text = title;
			value = true;
			if (!string.IsNullOrEmpty(viewDataKey)) this.viewDataKey = viewDataKey;
		}
	}
}
