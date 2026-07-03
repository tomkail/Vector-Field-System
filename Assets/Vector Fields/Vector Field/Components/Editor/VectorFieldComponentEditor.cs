using System.Linq;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VectorFieldComponent), true), CanEditMultipleObjects]
public class VectorFieldComponentEditor : BaseEditor<VectorFieldComponent> {
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
	float maxComponent;
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
	Vector2Map PreviewField => data is DrawableVectorFieldComponent drawable ? drawable.PaintField : data.vectorField;

	public override void OnEnable() {
		base.OnEnable();
		maxComponent = 1f;
	}

	void OnDisable() {
		if (previewMaterial != null) DestroyImmediate(previewMaterial);
	}

	public override void OnInspectorGUI() {
		base.OnInspectorGUI();
		if (GUILayout.Button("Rasterize")) {
			Rasterize();
		}

		DrawDiagnostics();

		serializedObject.ApplyModifiedProperties();
	}

	bool showDiagnostics {
		get => EditorPrefs.GetBool("VectorFieldComponentEditor_ShowDiagnostics", false);
		set => EditorPrefs.SetBool("VectorFieldComponentEditor_ShowDiagnostics", value);
	}

	// Read-only live state of the field: how it's backed, whether a CPU copy exists, whether it's up to date, the
	// readback mode, and who's consuming the CPU copy. Tucked in a collapsed foldout so it reads as info, not
	// controls; consumer entries are links you can click to ping them.
	void DrawDiagnostics() {
		if (targets.Length != 1) return; // per-object state; only meaningful for a single selection

		EditorGUILayout.Space();
		showDiagnostics = EditorGUILayout.Foldout(showDiagnostics, "Diagnostics", true);
		if (!showDiagnostics) return;

		var consumers = data.CpuConsumers;
		using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
			if (data.renderTexture != null)
				DiagnosticRow("Backing", $"GPU — {data.renderTexture.width}×{data.renderTexture.height} {data.renderTexture.graphicsFormat}");
			else
				DiagnosticRow("Backing", "CPU");

			if (data is GroupVectorFieldComponent group)
				DiagnosticRow("Combine", $"{group.layers.Count} layer(s)");

			DiagnosticRow("CPU copy", data.vectorField != null ? $"{data.vectorField.size.x}×{data.vectorField.size.y}" : "none");
			DiagnosticRow("State", data.IsDirty ? "re-render pending" : "up to date");

			if (data.renderTexture != null && consumers.Count > 0) {
				bool immediate = consumers.Any(c => data.IsImmediateCpuConsumer(c));
				DiagnosticRow("Readback", immediate ? "synchronous" : (data.IsReadbackPending ? "async (in flight)" : "async (idle)"));
			}

			GUILayout.Space(4);
			GUILayout.Label($"CPU consumers ({consumers.Count})", EditorStyles.miniBoldLabel);
			if (consumers.Count == 0) {
				GUILayout.Label("Nothing is reading this field's CPU copy — it runs GPU-only.", EditorStyles.wordWrappedMiniLabel);
			} else {
				foreach (var consumer in consumers) {
					EditorGUILayout.BeginHorizontal();
					if (GUILayout.Button($"{consumer.name}  ({consumer.GetType().Name})", EditorStyles.linkLabel))
						EditorGUIUtility.PingObject(consumer);
					GUILayout.FlexibleSpace();
					GUILayout.Label(data.IsImmediateCpuConsumer(consumer) ? "immediate" : "async", EditorStyles.miniLabel);
					EditorGUILayout.EndHorizontal();
				}
			}
		}
	}

	static void DiagnosticRow(string label, string value) {
		EditorGUILayout.BeginHorizontal();
		GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(70));
		GUILayout.Label(value, EditorStyles.miniLabel);
		EditorGUILayout.EndHorizontal();
	}


	public void Rasterize() {
		// Bake the source field's CPU copy into a new editable Drawable. PreviewField resolves Drawable's authored
		// paint vs every other type's readback copy; it's null for a GPU-only source with no CPU data yet.
		var source = PreviewField;
		if (source == null) {
			Debug.LogWarning("Nothing to rasterize — this field has no CPU data. Attach a CPU consumer (or paint into it) first.", data);
			return;
		}

		// Create new undo group
		Undo.IncrementCurrentGroup();

		// Create GameObject hierarchy
		GameObject go = new GameObject("Vector Field");
		Undo.RegisterCreatedObjectUndo(go, "Create my GameObject");
		Undo.SetTransformParent(go.transform, data.gameObject.transform.parent, "Modify parent");

		// Move GameObject hierarchy
		Undo.RegisterFullObjectHierarchyUndo(go, "Update my GameObject position");
		go.transform.position = data.gameObject.transform.position;
		go.transform.rotation = data.gameObject.transform.rotation;
		go.transform.localScale = data.gameObject.transform.localScale;

		var vectorFieldComponent = Undo.AddComponent<DrawableVectorFieldComponent>(go);
		// Write into the painted field (the authored source of truth), not base.vectorField (the non-serialized
		// readback target) — otherwise the rasterized field is empty and doesn't persist.
		vectorFieldComponent.gridRenderer.scaleWithGridSize = data.gridRenderer.scaleWithGridSize;
		vectorFieldComponent.LoadPaintField(source);
		Undo.RegisterCompleteObjectUndo(vectorFieldComponent, "Update Vector Field");
		Undo.RegisterCompleteObjectUndo(vectorFieldComponent.gridRenderer, "Update Vector Field");

		// Name undo group
		Undo.SetCurrentGroupName("Create and Reposition GameObject with Child");
	}
	public override bool RequiresConstantRepaint() {
		return true;
	}

	// Frame the field's grid bounds (the same rect drawn as the selection gizmo) instead of the transform center
	// when the user frames the selection (F / double-click in the hierarchy).
	bool HasFrameBounds() => data != null && data.gridRenderer != null;

	Bounds OnGetFrameBounds() {
		var bounds = data.GetBounds();
		// Encapsulate every selected field so framing a multi-selection fits them all.
		foreach (var t in targets) {
			if (t is VectorFieldComponent field && field != data && field.gridRenderer != null)
				bounds.Encapsulate(field.GetBounds());
		}
		return bounds;
	}

	public override bool HasPreviewGUI() { return true; }

	public override void OnPreviewGUI(Rect r, GUIStyle background) {
		if (Event.current.type != EventType.Repaint || data.renderTexture == null) return;

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
		if (newAutomaticScale != automaticScale) {
			automaticScale = newAutomaticScale;
		}

		EditorGUI.BeginDisabledGroup(automaticScale);
		if (automaticScale) EditorGUILayout.FloatField(calculatedScale, GUILayout.Width(120));
		else maxComponent = Mathf.Max(0, EditorGUILayout.FloatField(maxComponent, GUILayout.Width(120)));
		EditorGUI.EndDisabledGroup();
	}

	[DrawGizmo(GizmoType.Selected)]
	static void DrawGizmoForMyScript(VectorFieldComponent vectorFieldComponent, GizmoType gizmoType) {
		GizmosX.BeginColor(Color.white.WithAlpha(1f));
		var bounds = vectorFieldComponent.gridRenderer.edge.NormalizedToWorldRect(new Rect(0, 0, 1, 1));
		GizmosX.DrawWirePolygon(bounds);
	}
}
