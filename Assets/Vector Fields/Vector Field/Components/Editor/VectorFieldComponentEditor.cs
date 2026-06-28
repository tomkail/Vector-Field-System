using System.Linq;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(VectorFieldComponent), true), CanEditMultipleObjects]
public class VectorFieldComponentEditor : BaseEditor<VectorFieldComponent> {
	Texture2D texture;
	bool automaticScale {
		get => EditorPrefs.GetBool("VectorFieldComponentEditor_AutomaticScale", true);
		set => EditorPrefs.SetBool("VectorFieldComponentEditor_AutomaticScale", value);
	}
	float maxComponent;
	float calculatedScale => automaticScale ? (PreviewField != null ? VectorFieldScriptableObject.GetMaxAbsComponent(PreviewField.values) : 1f) : maxComponent;

	// The CPU field to preview. Drawable authors into its own PaintField (base.vectorField is its readback target,
	// which is null until a CPU consumer is attached); every other component exposes its CPU copy via vectorField.
	Vector2Map PreviewField => data is DrawableVectorFieldComponent drawable ? drawable.PaintField : data.vectorField;

	// Texture2D throws on a zero/negative size, so clamp to at least 1×1. A field can be empty (size 0×0)
	// when its component hasn't rendered yet on the frame the editor is enabled.
	static Point PreviewTextureSize(Vector2Map field) => field != null
		? new Point(Mathf.Max(1, field.size.x), Mathf.Max(1, field.size.y))
		: new Point(1, 1);

	public override void OnEnable() {
		base.OnEnable();
		texture = TextureX.Create(PreviewTextureSize(PreviewField), Color.black);
		maxComponent = 1f;
	}

	void OnDisable() {
		DestroyImmediate(texture);
	}

	public override void OnInspectorGUI() {
		base.OnInspectorGUI();
		if (GUILayout.Button("Rasterize")) {
			Rasterize();
		}
		// if(serializedObject.FindProperty("fullScreen").boolValue) {
		// 	EditorGUI.BeginDisabledGroup(true);
		// 	EditorGUILayout.Vector2IntField("Size", RenderTextureCreator.screenSize);
		// 	EditorGUI.EndDisabledGroup();
		// }

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
		vectorFieldComponent.vectorField = new Vector2Map(data.vectorField);
		vectorFieldComponent.gridRenderer.gridSize = vectorFieldComponent.vectorField.size;
		vectorFieldComponent.gridRenderer.scaleWithGridSize = data.gridRenderer.scaleWithGridSize;
		Undo.RegisterCompleteObjectUndo(vectorFieldComponent, "Update Vector Field");
		Undo.RegisterCompleteObjectUndo(vectorFieldComponent.gridRenderer, "Update Vector Field");

		// Name undo group
		Undo.SetCurrentGroupName("Create and Reposition GameObject with Child");
	}
	public override bool RequiresConstantRepaint() {
		return true;
	}

	public override bool HasPreviewGUI() { return true; }

	public override void OnPreviewGUI(Rect r, GUIStyle background) {
		if (Event.current.type == EventType.Repaint) {
			if (data is DrawableVectorFieldComponent && PreviewField != null && PreviewField.size.x > 0 && PreviewField.size.y > 0) {
				var previewField = PreviewField;
				if (texture.width != previewField.size.x || texture.height != previewField.size.y) {
					DestroyImmediate(texture);
					texture = TextureX.Create(previewField.size, Color.black);
				}
				var colors = VectorFieldUtils.VectorsToColors(previewField.values, 1f / calculatedScale);
				texture.SetPixels(colors);
				texture.Apply();
				EditorGUI.DrawPreviewTexture(r, texture, null, ScaleMode.ScaleToFit);
			} else if (data.renderTexture != null) {
				EditorGUI.DrawPreviewTexture(r, data.renderTexture, null, ScaleMode.ScaleToFit);
			}

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
		// bounds = cellCenter.NormalizedRectToWorldRect(new Rect(0,0,1,1));
		// GizmosX.DrawWirePolygon(bounds);
		// GizmosX.EndColor();
		//
		// GizmosX.BeginColor(Color.white.WithAlpha(0.25f));
		// for(int y = 1; y < gridRenderer.gridSize.y; y++)
		// 	Gizmos.DrawLine(gridRenderer.edge.GridToWorldPoint(new Vector2(0,y)), gridRenderer.edge.GridToWorldPoint(new Vector2(gridRenderer.gridSize.x,y)));
		// for(int x = 1; x < gridRenderer.gridSize.x; x++)
		// 	Gizmos.DrawLine(gridRenderer.edge.GridToWorldPoint(new Vector2(x,0)), gridRenderer.edge.GridToWorldPoint(new Vector2(x,gridRenderer.gridSize.y)));
		//
		// GizmosX.EndColor();
	}
}
