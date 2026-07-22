using System;
using UnityEngine;
using UnityEditor;

// Speed-tier editor shared by the tiered renderer editors (Flow Map, Flow Lit, LIC, Flow-Aligned, IBFV). Tiers are
// interpolation ANCHORS on the normalised speed axis (0 = still, left → 1 = Max Speed, right); each sample blends the
// two tiers straddling its local speed. Because a tier is a point (not a range), it's drawn as a fixed-size draggable
// STOP under a blended-colour bar — like a Gradient editor's colour stops or an AnimationCurve's keys. Stops FAN OUT
// when they'd overlap, so two coincident tiers (or a tier parked at the 0/1 edge) can never collapse to nothing and
// vanish. A second, always-visible tier-select row below the bar is a belt-and-suspenders guarantee that every tier —
// including a zero-gap one — stays reachable/selectable even if the bar can't separate them. The selected tier's fields
// are drawn beneath. Right-click adds/removes tiers.
//
// (Was modelled on Unity's LODGroup slider, which draws each LOD as a region spanning to the next boundary. That model
// hides any tier whose region is zero-width — a tier parked at speed 1, or coincident with a neighbour — which is what
// motivated the stop-based rework. See git history for the region version.)
//
// Host editors construct one instance with the tier-list property path, the per-tier fields to draw (the "speed" field
// must exist on the tier struct; list it first so it's editable numerically too), and a seeding action for a
// brand-new tier, then add `new IMGUIContainer(bar.OnGUI)` to a section.
public class VectorFieldTierBarGUI {
	// Layout constants.
	const int BarTopMargin = 18, BarHeight = 22, StopGap = 5, StopHeight = 14, BarBottomMargin = 10;
	const float StopWidth = 11f;          // fixed-size stop marker: never shrinks, so a tier can't disappear
	const float GradientStepPx = 3f;      // horizontal resolution of the blended-colour fill between two stops

	// LOD-style colour palette (kLODColors), cycled per tier so each stop reads as a distinct band.
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
			alignment = TextAnchor.MiddleCenter, wordWrap = false,
			normal = { textColor = Color.white }, richText = false, fontSize = 9,
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
		GUILayout.Space(StopGap);
		Rect stops = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(StopHeight), GUILayout.ExpandWidth(true));
		GUILayout.Space(BarBottomMargin);

		// Fan-out layout: each stop wants to sit centred on its speed, but a run of overlapping stops is left-packed with
		// a minimum gap so every one stays fully drawn and grabbable — coincident/edge tiers can't hide behind each other.
		float[] drawnX = ComputeStopPositions(bar, tiersProp, order, n);

		HandleBarInput(bar, stops, drawnX, tiersProp, order, n);
		for (int d = 0; d < n; d++)
			EditorGUIUtility.AddCursorRect(StopRect(stops, drawnX[d]), MouseCursor.ResizeHorizontal);
		if (Event.current.type == EventType.Repaint) DrawBar(bar, stops, drawnX, tiersProp, order, n);

		// Belt-and-suspenders reachability: a row of buttons for EVERY tier, in speed order, so a zero-gap tier the bar
		// can't separate is still one click away. Labels carry the tier's % so identical-position tiers stay legible.
		using (new EditorGUILayout.HorizontalScope()) {
			EditorGUILayout.LabelField("Tiers", GUILayout.Width(34));
			for (int d = 0; d < n; d++) {
				int e = order[d];
				bool on = e == selectedTier;
				var label = new GUIContent($"Tier {d}  {Mathf.RoundToInt(Speed(tiersProp, e) * 100)}%");
				if (GUILayout.Toggle(on, label, EditorStyles.miniButton) && !on) selectedTier = e;
			}
			GUILayout.FlexibleSpace();
		}

		// Selected tier's settings (like the LODGroup renderer list).
		selectedTier = Mathf.Clamp(selectedTier, 0, n - 1);
		int displayPos = Array.IndexOf(order, selectedTier);
		var el = tiersProp.GetArrayElementAtIndex(selectedTier);
		EditorGUILayout.Space(2);
		EditorGUILayout.LabelField($"Tier {Mathf.Max(0, displayPos)}", EditorStyles.boldLabel);
		foreach (var field in tierFields)
			EditorGUILayout.PropertyField(el.FindPropertyRelative(field));

		serializedObject.ApplyModifiedProperties();
	}

	// ── Layout ───────────────────────────────────────────────────────────────────────────────────────────────────
	// Fixed-size stop x-positions (top-left of each marker), in display order. Each stop is centred on its speed, then
	// clamped inside the bar and left-packed so consecutive stops keep at least StopWidth+1 between them. That gap is
	// what makes coincident or edge tiers all stay visible and separately grabbable.
	float[] ComputeStopPositions(Rect bar, SerializedProperty tiersProp, int[] order, int n) {
		float half = StopWidth * 0.5f;
		float minGap = StopWidth + 1f;
		var xs = new float[n];
		float prev = float.NegativeInfinity;
		for (int d = 0; d < n; d++) {
			float want = Mathf.Clamp(XForSpeed(bar, Speed(tiersProp, order[d])) - half, bar.x, bar.xMax - StopWidth);
			if (want < prev + minGap) want = prev + minGap;   // overlaps its neighbour → shove right
			xs[d] = want;
			prev = want;
		}
		// If left-packing ran off the right edge, right-pack the tail back on so the last stop stays on-screen.
		float overflow = xs[n - 1] - (bar.xMax - StopWidth);
		if (overflow > 0f)
			for (int d = n - 1; d >= 0; d--) {
				xs[d] = Mathf.Max(bar.x, xs[d] - overflow);
				if (d == 0) break;                                 // no earlier stop to push
				overflow = (xs[d - 1] + minGap) - xs[d];           // how far the previous stop still overlaps
				if (overflow <= 0f) break;                         // earlier stops already clear
			}
		return xs;
	}

	Rect StopRect(Rect stops, float x) => new Rect(x, stops.y, StopWidth, StopHeight);

	// ── Drawing ──────────────────────────────────────────────────────────────────────────────────────────────────
	void DrawBar(Rect bar, Rect stops, float[] drawnX, SerializedProperty tiersProp, int[] order, int n) {
		EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.5f)); // track background

		// Blended-colour fill: flat below the first tier and above the last, and a smooth lerp between each adjacent
		// pair — a direct picture of "each sample blends the two tiers straddling its speed".
		float firstS = Speed(tiersProp, order[0]);
		float lastS = Speed(tiersProp, order[n - 1]);
		if (firstS > 0f) EditorGUI.DrawRect(FromSpeeds(bar, 0f, firstS), TierColors[0]);
		if (lastS < 1f) EditorGUI.DrawRect(FromSpeeds(bar, lastS, 1f), TierColors[(n - 1) % TierColors.Length]);
		for (int d = 0; d < n - 1; d++)
			DrawGradientSpan(bar, Speed(tiersProp, order[d]), Speed(tiersProp, order[d + 1]),
				TierColors[d % TierColors.Length], TierColors[(d + 1) % TierColors.Length]);

		// Stops: a stem from each stop up to its TRUE speed position on the bar (so a fanned-out stop still points at
		// where it really sits), then the fixed-size marker, tier index, and a selection ring.
		for (int d = 0; d < n; d++) {
			int e = order[d];
			float trueX = XForSpeed(bar, Speed(tiersProp, e));
			float cx = drawnX[d] + StopWidth * 0.5f;
			EditorGUI.DrawRect(new Rect(trueX - 0.5f, bar.y, 1f, bar.height), new Color(1f, 1f, 1f, 0.5f)); // tick on bar
			if (Mathf.Abs(cx - trueX) > 1f)   // fanned out → draw the leaning stem
				DrawStem(trueX, bar.yMax, cx, stops.y);

			var r = StopRect(stops, drawnX[d]);
			bool sel = e == selectedTier;
			EditorGUI.DrawRect(r, TierColors[d % TierColors.Length]);
			if (sel) DrawOutline(r, Color.white, 1);
			else DrawOutline(r, new Color(0f, 0f, 0f, 0.6f), 1);
			GUI.Label(new Rect(r.x - 4f, r.y, r.width + 8f, r.height), d.ToString(), labelStyle);
		}
	}

	// A stepped horizontal lerp between two colours across [sA → sB] in speed space.
	void DrawGradientSpan(Rect bar, float sA, float sB, Color colA, Color colB) {
		float xa = XForSpeed(bar, sA), xb = XForSpeed(bar, sB);
		float width = xb - xa;
		if (width <= 0f) return;
		int steps = Mathf.Max(1, Mathf.CeilToInt(width / GradientStepPx));
		for (int i = 0; i < steps; i++) {
			float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
			float x0 = xa + t0 * width, x1 = xa + t1 * width;
			EditorGUI.DrawRect(new Rect(x0, bar.y, Mathf.Max(1f, x1 - x0), bar.height),
				Color.Lerp(colA, colB, (t0 + t1) * 0.5f));
		}
	}

	// Diagonal stem (two thin segments in an L) linking a stop's marker to its true position on the bar.
	static void DrawStem(float xTop, float yTop, float xBot, float yBot) {
		var c = new Color(1f, 1f, 1f, 0.35f);
		float mid = (yTop + yBot) * 0.5f;
		EditorGUI.DrawRect(new Rect(xTop - 0.5f, yTop, 1f, mid - yTop), c);
		EditorGUI.DrawRect(new Rect(Mathf.Min(xTop, xBot), mid - 0.5f, Mathf.Abs(xBot - xTop) + 1f, 1f), c);
		EditorGUI.DrawRect(new Rect(xBot - 0.5f, mid, 1f, yBot - mid), c);
	}

	static void DrawOutline(Rect r, Color c, int t) {
		EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
		EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
		EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
		EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
	}

	// ── Input (hotControl drag on the stops) ─────────────────────────────────────────────────────────────────────
	void HandleBarInput(Rect bar, Rect stops, float[] drawnX, SerializedProperty tiersProp, int[] order, int n) {
		int id = GUIUtility.GetControlID(FocusType.Passive);
		Event e = Event.current;
		Rect interactive = Rect.MinMaxRect(bar.xMin, bar.yMin, bar.xMax, stops.yMax);
		switch (e.GetTypeForControl(id)) {
			case EventType.MouseDown:
				if (!interactive.Contains(e.mousePosition)) break;
				if (e.button == 1) { ShowContextMenu(bar, tiersProp, order, n, e.mousePosition); e.Use(); break; }
				// Stop markers first (topmost priority), then fall back to selecting by the bar region clicked.
				for (int d = 0; d < n; d++) {
					if (StopRect(stops, drawnX[d]).Contains(e.mousePosition)) {
						GUIUtility.hotControl = id; dragTier = order[d]; selectedTier = order[d];
						e.Use(); return;
					}
				}
				selectedTier = order[NearestDisplay(bar, tiersProp, order, n, e.mousePosition.x)];
				e.Use();
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

	// Display index of the tier whose speed is nearest the given screen x (for click-to-select on the bar background).
	int NearestDisplay(Rect bar, SerializedProperty tiersProp, int[] order, int n, float x) {
		float s = Mathf.Clamp01((x - bar.x) / Mathf.Max(1f, bar.width));
		int best = 0; float bestD = float.MaxValue;
		for (int d = 0; d < n; d++) {
			float dist = Mathf.Abs(Speed(tiersProp, order[d]) - s);
			if (dist < bestD) { bestD = dist; best = d; }
		}
		return best;
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
}
