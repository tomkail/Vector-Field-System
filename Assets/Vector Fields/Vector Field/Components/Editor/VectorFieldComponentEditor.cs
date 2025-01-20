using InControl.NativeDeviceProfiles;
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
	float calculatedScale => automaticScale ? VectorFieldScriptableObject.GetMaxAbsComponent(data.vectorField.values) : maxComponent;

	public override void OnEnable() {
		base.OnEnable();
		texture = TextureX.Create(data.vectorField.size, Color.black);
		maxComponent = 1f;//VectorFieldScriptableObject.GetMaxAbsComponent(data.vectorField.values);
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

		serializedObject.ApplyModifiedProperties();
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
			if (data is DrawableVectorFieldComponent) {
				if (texture.width != data.vectorField.size.x || texture.height != data.vectorField.size.y) {
					DestroyImmediate(texture);
					texture = TextureX.Create(data.vectorField.size, Color.black);
				}
				var colors = VectorFieldUtils.VectorsToColors(data.vectorField.values, 1f / calculatedScale);
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
