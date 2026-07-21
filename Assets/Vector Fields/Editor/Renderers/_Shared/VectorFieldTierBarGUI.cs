using System;
using UnityEngine;
using UnityEditor;

// LODGroup-style speed-tier editor shared by the tiered renderer editors (Flow Map, Flow Lit, LIC, Flow-Aligned,
// IBFV). Modelled on Unity's LODGroup editor (LODGUI.cs / LODGroupEditor.cs): contiguous colour-coded regions across
// the normalised speed axis with a "Tier n / %" label, draggable boundary handles (hotControl), click-to-select, and a
// right-click Insert / Delete menu. The selected tier's fields are shown beneath the bar. Axis runs 0 (still, left) →
// 1 (Max Speed, right).
//
// Host editors construct one instance with the tier-list property path, the per-tier fields to draw (the "speed" field
// must exist on the tier struct; list it first so it's editable numerically too), and a seeding action for a
// brand-new tier, then add `new IMGUIContainer(bar.OnGUI)` to a section.
public class VectorFieldTierBarGUI {
	// LODGUI layout constants.
	const int BarTopMargin = 18, BarHeight = 30, BarBottomMargin = 16, HandleWidth = 10;

	// LOD-style colour palette (kLODColors), cycled per region.
	static readonly Color[] TierColors = {
		new Color(0.4831376f, 0.6211768f, 0.0219608f), new Color(0.2792160f, 0.4078432f, 0.5835296f),
		new Color(0.2070592f, 0.5333336f, 0.6117648f), new Color(0.5333336f, 0.1600000f, 0.0282352f),
		new Color(0.3827448f, 0.2886272f, 0.5239216f), new Color(0.8000000f, 0.4423528f, 0.0000000f),
		new Color(0.4486272f, 0.4078432f, 0.0501960f), new Color(0.7749016f, 0.6368624f, 0.0250980f),
	};

	readonly SerializedObject serializedObject;
	readonly string tiersPath;
	readonly string[] tierFields;                       // per-tier properties drawn for the selected tier
	readonly Action<SerializedProperty> seedDefaults;   // fills a freshly inserted tier when there's nothing to clone
	readonly int maxTiers;

	int selectedTier;
	int dragTier = -1;
	GUIStyle labelStyle;

	public VectorFieldTierBarGUI(SerializedObject serializedObject, string[] tierFields,
		Action<SerializedProperty> seedDefaults, int maxTiers, string tiersPath = "tiers") {
		this.serializedObject = serializedObject;
		this.tierFields = tierFields;
		this.seedDefaults = seedDefaults;
		this.maxTiers = maxTiers;
		this.tiersPath = tiersPath;
	}

	public void OnGUI() {
		serializedObject.Update();
		var tiersProp = serializedObject.FindProperty(tiersPath);
		int n = tiersProp.arraySize;

		labelStyle ??= new GUIStyle(EditorStyles.miniLabel) {
			alignment = TextAnchor.MiddleCenter, wordWrap = true,
			normal = { textColor = Color.white }, richText = false,
		};

		if (n == 0) {
			EditorGUILayout.HelpBox("No tiers. Add one to start.", MessageType.Info);
			if (GUILayout.Button("Add Tier")) InsertTier(-1, 0.5f, 0f);
			serializedObject.ApplyModifiedProperties();
			return;
		}

		// Display order: tiers sorted ascending by speed (drag clamps between neighbours so this stays stable mid-drag).
		var order = new int[n];
		for (int i = 0; i < n; i++) order[i] = i;
		Array.Sort(order, (a, b) => Speed(tiersProp, a).CompareTo(Speed(tiersProp, b)));

		GUILayout.Space(BarTopMargin);
		Rect bar = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(BarHeight), GUILayout.ExpandWidth(true));
		GUILayout.Space(BarBottomMargin);

		HandleBarInput(bar, tiersProp, order, n);
		// Horizontal-resize cursor over each boundary's (wide) grab area, like the LOD slider.
		for (int d = 0; d < n; d++)
			EditorGUIUtility.AddCursorRect(HandleRect(bar, Speed(tiersProp, order[d])), MouseCursor.ResizeHorizontal);
		if (Event.current.type == EventType.Repaint) DrawBar(bar, tiersProp, order, n);

		// Selected tier's settings, beneath the bar (like the LODGroup renderer list).
		selectedTier = Mathf.Clamp(selectedTier, 0, n - 1);
		int displayPos = Array.IndexOf(order, selectedTier);
		var el = tiersProp.GetArrayElementAtIndex(selectedTier);
		EditorGUILayout.Space(2);
		EditorGUILayout.LabelField($"Tier {Mathf.Max(0, displayPos)}", EditorStyles.boldLabel);
		foreach (var field in tierFields)
			EditorGUILayout.PropertyField(el.FindPropertyRelative(field));

		serializedObject.ApplyModifiedProperties();
	}

	// ── Drawing ──────────────────────────────────────────────────────────────────────────────────────────────────
	void DrawBar(Rect bar, SerializedProperty tiersProp, int[] order, int n) {
		EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.5f)); // track background

		// A region per tier spanning [its speed → next tier's speed] (last tier extends to 1). A leading region from 0
		// to the first tier's speed shows the first tier held (clamped below its position).
		float firstSpeed = Speed(tiersProp, order[0]);
		if (firstSpeed > 0f) EditorGUI.DrawRect(FromSpeeds(bar, 0f, firstSpeed), TierColors[0] * 0.6f);

		for (int d = 0; d < n; d++) {
			int e = order[d];
			float s = Speed(tiersProp, e);
			float sNext = d < n - 1 ? Speed(tiersProp, order[d + 1]) : 1f;
			Rect region = FromSpeeds(bar, s, sNext);
			EditorGUI.DrawRect(region, TierColors[d % TierColors.Length]);
			// Selected range: a soft inset highlight (like LOD's selected range), not a hard full outline.
			if (e == selectedTier && region.width > 2f) {
				var inset = new Rect(region.x + 2f, region.y + 2f, Mathf.Max(0f, region.width - 4f), region.height - 4f);
				DrawOutline(inset, new Color(1f, 1f, 1f, 0.5f), 1);
			}
			if (region.width > 24) GUI.Label(region, $"Tier {d}\n{Mathf.RoundToInt(s * 100)}%", labelStyle);
		}

		// Thin, subtle boundary markers at each interior tier position (the edges have nothing to grab). The grab area
		// is still the wider HandleRect used in HandleBarInput — only the drawn marker is slim, matching the LOD slider.
		for (int d = 0; d < n; d++) {
			float s = Speed(tiersProp, order[d]);
			if (s <= 0.002f || s >= 0.998f) continue;
			EditorGUI.DrawRect(new Rect(XForSpeed(bar, s) - 1f, bar.y, 2f, bar.height), new Color(1f, 1f, 1f, 0.5f));
		}
	}

	static void DrawOutline(Rect r, Color c, int t) {
		EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
		EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
		EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
		EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
	}

	// ── Input (LODGroupEditor-style hotControl drag) ─────────────────────────────────────────────────────────────
	void HandleBarInput(Rect bar, SerializedProperty tiersProp, int[] order, int n) {
		int id = GUIUtility.GetControlID(FocusType.Passive);
		Event e = Event.current;
		switch (e.GetTypeForControl(id)) {
			case EventType.MouseDown:
				if (!bar.Contains(e.mousePosition)) break;
				if (e.button == 1) { ShowContextMenu(bar, tiersProp, order, n, e.mousePosition); e.Use(); break; }
				// Handles first, then regions (like the LOD slider).
				for (int d = 0; d < n; d++) {
					if (HandleRect(bar, Speed(tiersProp, order[d])).Contains(e.mousePosition)) {
						GUIUtility.hotControl = id; dragTier = order[d]; selectedTier = order[d];
						e.Use(); return;
					}
				}
				for (int d = 0; d < n; d++) {
					float s = Speed(tiersProp, order[d]);
					float sNext = d < n - 1 ? Speed(tiersProp, order[d + 1]) : 1f;
					if (FromSpeeds(bar, s, sNext).Contains(e.mousePosition)) { selectedTier = order[d]; e.Use(); break; }
				}
				break;

			case EventType.MouseDrag:
				if (GUIUtility.hotControl != id || dragTier < 0) break;
				{
					int d = Array.IndexOf(order, dragTier);
					float lo = d > 0 ? Speed(tiersProp, order[d - 1]) + 0.001f : 0f;
					float hi = d < n - 1 ? Speed(tiersProp, order[d + 1]) - 0.001f : 1f;
					float s = Mathf.Clamp((e.mousePosition.x - bar.x) / Mathf.Max(1f, bar.width), lo, hi);
					tiersProp.GetArrayElementAtIndex(dragTier).FindPropertyRelative("speed").floatValue = Mathf.Clamp01(s);
					e.Use();
				}
				break;

			case EventType.MouseUp:
				if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; dragTier = -1; e.Use(); }
				break;
		}
	}

	void ShowContextMenu(Rect bar, SerializedProperty tiersProp, int[] order, int n, Vector2 mouse) {
		float atSpeed = Mathf.Clamp01((mouse.x - bar.x) / Mathf.Max(1f, bar.width));
		// Source tier to clone: the one whose region the cursor is over (last tier at or below the click speed).
		int srcElem = n > 0 ? order[0] : -1;
		int srcPos = -1;
		for (int d = 0; d < n; d++) {
			if (Speed(tiersProp, order[d]) <= atSpeed) { srcElem = order[d]; srcPos = d; }
			else break;
		}
		if (srcPos < 0) srcPos = 0; // click below the first tier → split the first tier's region

		// Split the clicked tier's region [sA, sB] into the source (2/3) and a new half-width tier (1/3), placed on the
		// SIDE of the region the cursor is on: left of the midpoint → new tier on the LEFT (source shifts right); right
		// of the midpoint → new tier on the RIGHT (source stays). Either way the new tier is half the source's width.
		float sA = n > 0 ? Speed(tiersProp, order[srcPos]) : 0f;
		float sB = srcPos < n - 1 ? Speed(tiersProp, order[srcPos + 1]) : 1f;
		float third = (sB - sA) / 3f;
		bool leftSide = atSpeed < (sA + sB) * 0.5f;
		float newTierSpeed = leftSide ? sA : sA + 2f * third;   // the clone's speed
		float sourceSpeed  = leftSide ? sA + third : sA;        // the source's (possibly shifted) speed

		var menu = new GenericMenu();
		if (n < maxTiers) menu.AddItem(new GUIContent("Add Tier Here"), false, () => InsertTier(srcElem, newTierSpeed, sourceSpeed));
		else menu.AddDisabledItem(new GUIContent("Add Tier Here"));
		if (n > 1) menu.AddItem(new GUIContent("Delete Selected Tier"), false, () => DeleteTier(selectedTier));
		else menu.AddDisabledItem(new GUIContent("Delete Selected Tier"));
		menu.ShowAsContext();
	}

	// ── Array edits (deferred menu callbacks: re-fetch + apply on their own) ──────────────────────────────────────
	// Clone the source tier's settings (texture + all params) into a new tier. The clone's speed = newTierSpeed; the
	// source is (re)set to sourceSpeed (only differs from its current speed when inserting on the LEFT, which shifts the
	// source right to make room). srcElem < 0 (empty array) seeds a default tier at newTierSpeed instead.
	void InsertTier(int srcElem, float newTierSpeed, float sourceSpeed) {
		serializedObject.Update();
		var tiers = serializedObject.FindProperty(tiersPath);
		if (srcElem >= 0 && srcElem < tiers.arraySize) {
			tiers.InsertArrayElementAtIndex(srcElem);   // clone at srcElem, original shifts to srcElem+1
			tiers.GetArrayElementAtIndex(srcElem).FindPropertyRelative("speed").floatValue = newTierSpeed;
			tiers.GetArrayElementAtIndex(srcElem + 1).FindPropertyRelative("speed").floatValue = sourceSpeed;
			selectedTier = srcElem;
		} else {
			int ni = tiers.arraySize;
			tiers.InsertArrayElementAtIndex(ni);
			var el = tiers.GetArrayElementAtIndex(ni);
			el.FindPropertyRelative("speed").floatValue = newTierSpeed;
			seedDefaults?.Invoke(el);
			selectedTier = ni;
		}
		serializedObject.ApplyModifiedProperties();
	}

	void DeleteTier(int index) {
		serializedObject.Update();
		var tiers = serializedObject.FindProperty(tiersPath);
		if (tiers.arraySize <= 1 || index < 0 || index >= tiers.arraySize) return;
		tiers.DeleteArrayElementAtIndex(index);
		serializedObject.ApplyModifiedProperties();
		selectedTier = Mathf.Clamp(selectedTier, 0, tiers.arraySize - 1);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────
	static float Speed(SerializedProperty tiersProp, int i) =>
		tiersProp.GetArrayElementAtIndex(i).FindPropertyRelative("speed").floatValue;

	static float XForSpeed(Rect bar, float s) => bar.x + Mathf.Clamp01(s) * bar.width;

	static Rect FromSpeeds(Rect bar, float a, float b) {
		float xa = XForSpeed(bar, a), xb = XForSpeed(bar, b);
		return new Rect(xa, bar.y, Mathf.Max(0f, xb - xa), bar.height);
	}

	static Rect HandleRect(Rect bar, float s) =>
		new Rect(XForSpeed(bar, s) - HandleWidth * 0.5f, bar.y, HandleWidth, bar.height);
}
