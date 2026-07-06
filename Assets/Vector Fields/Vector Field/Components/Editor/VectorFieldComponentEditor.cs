using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

// Base UI Toolkit inspector shared by every vector field component. It builds the common chrome — the header,
// the "Field" section (grid size / magnitude / mask), a Rasterize action, and a live Diagnostics card — and
// exposes BuildBody() for per-type editors to add their own grouped sections. The IMGUI preview, scene gizmo and
// frame-bounds are kept (they coexist with CreateInspectorGUI). Registered with editorForChildClasses:true so it
// still covers any subclass that doesn't ship its own editor.
[CustomEditor(typeof(VectorFieldComponent), true), CanEditMultipleObjects]
public class VectorFieldComponentEditor : Editor {
	// The typed target (replaces a typed base class this used to inherit). Unity creates a fresh editor instance
	// when the selection changes, so it stays current.
	protected VectorFieldComponent data => target as VectorFieldComponent;

	// --- Inspector UI ------------------------------------------------------------------------------------------

	public override VisualElement CreateInspectorGUI() {
		var root = new VisualElement();
		VectorFieldInspectorUI.ApplyStyle(root);

		// No title header here — Unity's component title bar already shows the field type's name.
		root.Add(BuildFieldSection());
		BuildBody(root);
		BuildFooter(root);

		return root;
	}

	// The base block every field shares: grid resolution, output magnitude, and the output mask (cookie).
	VectorFieldInspectorUI.Section BuildFieldSection() {
		var section = VectorFieldInspectorUI.MakeSection("Field", ViewKey("field"));

		// grid is a [field: SerializeField] auto-property → backing field "<grid>k__BackingField"; bind straight to
		// its serialized _size so the user sees a single "Grid Size" field, not a nested GridTransform foldout.
		var gridSize = serializedObject.FindProperty("<grid>k__BackingField")?.FindPropertyRelative("_size");
		if (gridSize != null)
			section.Add(VectorFieldInspectorUI.Field(gridSize, "Grid Size",
				"The field's resolution in cells (X × Y). Higher is more detailed but costs more GPU/CPU."));

		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("magnitude"), "Magnitude",
			"Uniform scalar applied to the field's output. Every consumer sees the scaled result."));
		section.Add(VectorFieldInspectorUI.Field(serializedObject.FindProperty("cookie"), "Mask",
			"Optional falloff mask multiplied into the field's output — radial softness, an authored curve, or a texture."));
		return section;
	}

	// Per-type editors override this to add their own sections. The base fallback dumps any remaining serialized
	// fields into a generic "Settings" card so an uncovered subclass still gets the shared look.
	protected virtual void BuildBody(VisualElement root) {
		var section = VectorFieldInspectorUI.MakeSection("Settings", ViewKey("settings"));
		bool any = false;
		var it = serializedObject.GetIterator();
		if (it.NextVisible(true)) {
			do {
				if (BaseHandledPaths.Contains(it.propertyPath)) continue;
				section.Add(new PropertyField(it.Copy()));
				any = true;
			} while (it.NextVisible(false));
		}
		if (any) root.Add(section);
	}

	// Property paths the base already renders (or that are internal); subclass/fallback bodies skip these.
	protected static readonly HashSet<string> BaseHandledPaths = new() {
		"m_Script", "<grid>k__BackingField", "magnitude", "cookie",
	};

	void BuildFooter(VisualElement root) {
		var footer = new VisualElement();
		footer.AddToClassList("vf-footer");

		footer.Add(new Button(Rasterize) { text = "Rasterize" });

		// Per-object live state — only meaningful for a single selection.
		if (!serializedObject.isEditingMultipleObjects)
			footer.Add(BuildDiagnostics(root));

		root.Add(footer);
	}

	// viewDataKey scoped to the concrete type so section expand/collapse persists per field type.
	protected string ViewKey(string suffix) => $"VF.{target.GetType().Name}.{suffix}";

	// --- Diagnostics (live, read-only) -------------------------------------------------------------------------
	// Mirrors the old IMGUI foldout: how the field is backed, whether a CPU copy exists and is fresh, the readback
	// mode, and who's consuming the CPU copy (clickable to ping). Rebuilt on a timer while expanded.

	Foldout BuildDiagnostics(VisualElement root) {
		var fold = new Foldout { text = "Diagnostics", value = false, viewDataKey = ViewKey("diag") };
		fold.AddToClassList("vf-section");
		var body = new VisualElement();
		fold.Add(body);

		void Refresh() {
			if (!fold.value || data == null) return;
			body.Clear();
			PopulateDiagnostics(body);
		}
		Refresh();
		fold.RegisterValueChangedCallback(_ => Refresh());
		root.schedule.Execute(Refresh).Every(250);
		return fold;
	}

	void PopulateDiagnostics(VisualElement body) {
		var consumers = data.CpuConsumers;

		body.Add(DiagRow("Backing", data.renderTexture != null
			? $"GPU — {data.renderTexture.width}×{data.renderTexture.height} {data.renderTexture.graphicsFormat}"
			: "CPU"));

		if (data is GroupVectorFieldComponent group)
			body.Add(DiagRow("Combine", $"{group.layers.Count} layer(s)"));

		body.Add(DiagRow("CPU copy", data.vectorField != null ? $"{data.vectorField.size.x}×{data.vectorField.size.y}" : "none"));
		body.Add(DiagRow("State", data.IsDirty ? "re-render pending" : "up to date"));

		if (data.renderTexture != null && consumers.Count > 0) {
			bool immediate = consumers.Any(c => data.IsImmediateCpuConsumer(c));
			body.Add(DiagRow("Readback", immediate ? "synchronous" : (data.IsReadbackPending ? "async (in flight)" : "async (idle)")));
		}

		var consumerTitle = new Label($"CPU consumers ({consumers.Count})");
		consumerTitle.style.marginTop = 4;
		consumerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
		consumerTitle.style.fontSize = 11;
		body.Add(consumerTitle);

		if (consumers.Count == 0) {
			body.Add(VectorFieldInspectorUI.Help("Nothing is reading this field's CPU copy — it runs GPU-only."));
		} else {
			foreach (var consumer in consumers) {
				var row = new VisualElement();
				row.AddToClassList("vf-diag-row");
				var link = new Button(() => EditorGUIUtility.PingObject(consumer)) { text = $"{consumer.name}  ({consumer.GetType().Name})" };
				link.AddToClassList("vf-diag-link");
				link.style.flexGrow = 1;
				row.Add(link);
				var tag = new Label(data.IsImmediateCpuConsumer(consumer) ? "immediate" : "async");
				tag.AddToClassList("vf-diag-label");
				row.Add(tag);
				body.Add(row);
			}
		}
	}

	static VisualElement DiagRow(string label, string value) {
		var row = new VisualElement();
		row.AddToClassList("vf-diag-row");
		var l = new Label(label);
		l.AddToClassList("vf-diag-label");
		var v = new Label(value);
		v.AddToClassList("vf-diag-value");
		row.Add(l);
		row.Add(v);
		return row;
	}

	// --- Rasterize -------------------------------------------------------------------------------------------
	public void Rasterize() {
		// Bake the source field's CPU copy into a new editable Drawable. PreviewField resolves Drawable's authored
		// paint vs every other type's readback copy; it's null for a GPU-only source with no CPU data yet.
		var source = PreviewField;
		if (source == null) {
			Debug.LogWarning("Nothing to rasterize — this field has no CPU data. Attach a CPU consumer (or paint into it) first.", data);
			return;
		}

		Undo.IncrementCurrentGroup();

		GameObject go = new GameObject("Vector Field");
		Undo.RegisterCreatedObjectUndo(go, "Create Vector Field");
		Undo.SetTransformParent(go.transform, data.gameObject.transform.parent, "Modify parent");

		Undo.RegisterFullObjectHierarchyUndo(go, "Update Vector Field position");
		go.transform.position = data.gameObject.transform.position;
		go.transform.rotation = data.gameObject.transform.rotation;
		go.transform.localScale = data.gameObject.transform.localScale;

		var drawable = Undo.AddComponent<DrawableVectorFieldComponent>(go);
		// Write into the painted field (the authored source of truth), not base.vectorField (the non-serialized
		// readback target) — otherwise the rasterized field is empty and doesn't persist.
		drawable.LoadPaintField(source);
		Undo.RegisterCompleteObjectUndo(drawable, "Rasterize Vector Field");
		Undo.SetCurrentGroupName("Rasterize Vector Field");
	}

	// --- Preview (IMGUI) ---------------------------------------------------------------------------------------
	// Re-encodes the field's render texture for the inspector preview, applying the contrast scale on the GPU (see
	// VectorFieldPreview.shader) rather than rebuilding a CPU Texture2D every repaint.
	static Shader previewShader;
	static Shader PreviewShader => previewShader ? previewShader : (previewShader = Resources.Load<Shader>("VectorFieldPreview"));
	static readonly int ScaleID = Shader.PropertyToID("_Scale");
	Material previewMaterial;

	bool automaticScale {
		get => EditorPrefs.GetBool("VectorFieldComponentEditor_AutomaticScale", true);
		set => EditorPrefs.SetBool("VectorFieldComponentEditor_AutomaticScale", value);
	}
	float maxComponent = 1f;
	// Auto Scale normalizes preview contrast against the field's largest absolute component; manual mode uses the
	// user's maxComponent. The field is small and the scan is far cheaper than the per-repaint Texture2D upload it
	// replaced, so computing this on the CPU (and feeding it to the GPU shader) is fine — no GPU reduction needed.
	float calculatedScale => automaticScale ? (PreviewField != null ? MaxAbsComponent(PreviewField.values) : 1f) : maxComponent;

	static float MaxAbsComponent(Vector2[] vectors) {
		float max = 0;
		for (int i = 0; i < vectors.Length; i++)
			max = Mathf.Max(max, Mathf.Abs(vectors[i].x), Mathf.Abs(vectors[i].y));
		return max;
	}

	// The CPU field used only to derive the Auto Scale value (and as Rasterize's source). Drawable authors into its
	// own PaintField; every other component exposes its CPU copy via vectorField (null until a consumer attaches, in
	// which case Auto Scale falls back to 1). The drawn preview always comes from the GPU renderTexture, not this.
	VectorFieldMap PreviewField => data is DrawableVectorFieldComponent drawable ? drawable.PaintField : data.vectorField;

	void OnDisable() {
		if (previewMaterial != null) DestroyImmediate(previewMaterial);
	}

	public override bool RequiresConstantRepaint() => true;

	public override bool HasPreviewGUI() => true;

	public override void OnPreviewGUI(Rect r, GUIStyle background) {
		if (Event.current.type != EventType.Repaint || data == null || data.renderTexture == null) return;

		// Draw the (cookie-masked) GPU field straight through the preview material, which applies the contrast scale
		// on the GPU. Falls back to an unscaled draw if the shader is missing.
		VectorFieldRendererUtils.GetOrCreateMaterial(ref previewMaterial, PreviewShader, hideAndDontSave: true);

		if (previewMaterial != null) {
			previewMaterial.SetTexture("_MainTex", data.renderTexture);
			previewMaterial.SetFloat(ScaleID, calculatedScale);
			EditorGUI.DrawPreviewTexture(r, data.renderTexture, previewMaterial, ScaleMode.ScaleToFit);
		} else {
			EditorGUI.DrawPreviewTexture(r, data.renderTexture, null, ScaleMode.ScaleToFit);
		}
	}

	public override void OnPreviewSettings() {
		bool newAutomaticScale = GUILayout.Toggle(automaticScale, new GUIContent("Auto Scale"), EditorStyles.toolbarButton, GUILayout.Width(80));
		if (newAutomaticScale != automaticScale) automaticScale = newAutomaticScale;

		EditorGUI.BeginDisabledGroup(automaticScale);
		if (automaticScale) EditorGUILayout.FloatField(calculatedScale, GUILayout.Width(120));
		else maxComponent = Mathf.Max(0, EditorGUILayout.FloatField(maxComponent, GUILayout.Width(120)));
		EditorGUI.EndDisabledGroup();
	}

	// --- Frame bounds & gizmo ----------------------------------------------------------------------------------

	// Frame the field's grid bounds (the same rect drawn as the selection gizmo) instead of the transform center
	// when the user frames the selection (F / double-click in the hierarchy).
	bool HasFrameBounds() => data != null;

	Bounds OnGetFrameBounds() {
		var bounds = data.GetBounds();
		// Encapsulate every selected field so framing a multi-selection fits them all.
		foreach (var t in targets) {
			if (t is VectorFieldComponent field && field != data)
				bounds.Encapsulate(field.GetBounds());
		}
		return bounds;
	}

	static Vector3[] s_gizmoCorners;
	[DrawGizmo(GizmoType.Selected)]
	static void DrawGizmoForField(VectorFieldComponent vectorFieldComponent, GizmoType gizmoType) {
		Gizmos.color = Color.white;
		vectorFieldComponent.grid.GetWorldCorners(ref s_gizmoCorners);
		for (int i = 0; i < 4; i++)
			Gizmos.DrawLine(s_gizmoCorners[i], s_gizmoCorners[(i + 1) % 4]);
	}
}
