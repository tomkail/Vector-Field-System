using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityX.Geometry;

public class VectorFieldEditorInfoSubWindow : VectorFieldEditorSubWindow {

	public override string name {get {return "Info";}}
	public override Vector2 minSize {get {return new Vector2(140,100);}}

	private Vector2 scrollPosition;

	private bool _createNewMapExpanded;
	public bool createNewMapExpanded {
		get {
			return _createNewMapExpanded;
		} set {
			_createNewMapExpanded = value;
		}
	}
	VectorFieldEditorWindow.NewMapProperties newMapProperties;

	private float newMaxMagnitude = 1;
	private float highPassFilter = 5;
	private float lowPassFilter = 50;
	public VectorFieldEditorInfoSubWindow (VectorFieldEditorWindow editorWindow) : base (editorWindow) {
		rect = new Rect(0, 80, 260, 320);
		newMapProperties = new VectorFieldEditorWindow.NewMapProperties();
	}

	public override void DrawWindow() {
		scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(rect.height-(EditorGUIX.windowHeaderHeight + 15 + 10)));

		EditorGUI.BeginChangeCheck();
		editorWindow.background = EditorGUILayout.ObjectField("Background", editorWindow.background, typeof(Texture2D), false) as Texture2D;
		if (EditorGUI.EndChangeCheck()) {
			editorWindow.material.SetTexture("_BackgroundTex", editorWindow.background);
		}

		VectorFieldScriptableObject _vectorFieldScriptableObject = EditorGUILayout.ObjectField("Vector Field", editorWindow.vectorFieldScriptableObject, typeof(VectorFieldScriptableObject), false) as VectorFieldScriptableObject;
		if(editorWindow.vectorFieldScriptableObject != _vectorFieldScriptableObject) {
			editorWindow.vectorFieldScriptableObject = _vectorFieldScriptableObject;
		}

		if(editorWindow.vectorField != null) {
			EditorGUILayout.HelpBox(
			"Size - "+editorWindow.vectorField.size,
			MessageType.None);
		}

		EditorGUILayout.HelpBox(
		"Controls"+"\n"+
		"Left Click - Paint"+"\n"+
		"Alt - Sample pressure"+"\n"+
		"Cmd - Clamp magnitude"+"\n"+
		"Ctrl - Drag move"+"\n"+
		"Arrows - Move"+"\n"+
		"Mouse Wheel - Zoom"+"\n"+
		"F - Reset Zoom"+"\n"+
		"Q - Undo"+"\n"+
		"W - Redo"+"\n"+
		"S - Save", 
		MessageType.None);


		if(editorWindow.vectorField != null) {
			editorWindow.showTurbulence = EditorGUILayout.Toggle("Show Turbulence", editorWindow.showTurbulence);

			GUILayout.BeginVertical(GUI.skin.box);
			GUI.enabled = editorWindow.maxMagnitude > 0;
			AnimationCurve animationCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(editorWindow.maxAllowedMagnitude * 0.75f, 1), new Keyframe(editorWindow.maxAllowedMagnitude * 0.85f, 0.5f), new Keyframe(editorWindow.maxAllowedMagnitude, 0));
			Color color = Color.Lerp(Color.red, Color.green, animationCurve.Evaluate(editorWindow.maxMagnitude));
			Color savedColor = GUI.color;
			GUI.color = color;
			EditorGUILayout.LabelField("Max magnitude: "+ editorWindow.maxMagnitude);
			
			color = Color.Lerp(Color.red, Color.green, animationCurve.Evaluate(newMaxMagnitude));
			GUI.color = color;
			newMaxMagnitude = EditorGUILayout.Slider(newMaxMagnitude,editorWindow.minAllowedMagnitude,editorWindow.maxAllowedMagnitude);
			GUI.color = savedColor;

			EditorGUI.BeginDisabledGroup(editorWindow.maxMagnitude > 0 && newMaxMagnitude != editorWindow.maxMagnitude);
			float deltaMagnitudeFactor = newMaxMagnitude / editorWindow.maxMagnitude;
			if(GUILayout.Button("Scale magnitude by "+deltaMagnitudeFactor.RoundTo(3)+"x")) {
				editorWindow.vectorField.Multiply(deltaMagnitudeFactor);
				editorWindow.maxMagnitude = newMaxMagnitude;
			}
			EditorGUI.EndDisabledGroup();
			EditorGUI.BeginDisabledGroup(editorWindow.maxMagnitude <= 100);
			if(GUILayout.Button("Clamp magnitude to 100 ")) {
				editorWindow.vectorField.ClampMagnitude(100);
				editorWindow.maxMagnitude = 100;
			}
			EditorGUI.EndDisabledGroup();

			GUILayout.BeginHorizontal();
			highPassFilter = EditorGUILayout.Slider(highPassFilter, 0, 100);
			if(GUILayout.Button("High Pass")) {
				editorWindow.HighPassFilter(highPassFilter);
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			lowPassFilter = EditorGUILayout.Slider(lowPassFilter, 0, 100);
			if(GUILayout.Button("Low Pass")) {
				editorWindow.LowPassFilter(lowPassFilter);
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		if(editorWindow.vectorField != null) {
			EditorGUILayout.BeginVertical(GUI.skin.box);
//			editorWindow.material.SetFloat("_GridCellCount", EditorGUILayout.Slider("Resolution", editorWindow.material.GetFloat("_GridCellCount"), 0, 10000));
			editorWindow.material.SetFloat("_Speed", EditorGUILayout.Slider("Speed", editorWindow.material.GetFloat("_Speed"), 0, 100));
			editorWindow.material.SetFloat("_TextureScale", EditorGUILayout.Slider("Texture Scale", editorWindow.material.GetFloat("_TextureScale"), 0, 1000));
			editorWindow.material.SetFloat("_Brightness", EditorGUILayout.Slider("Brightness", editorWindow.material.GetFloat("_Brightness"), 0, 50));
			editorWindow.material.SetTexture("_Tex", EditorGUILayout.ObjectField("Texture", editorWindow.material.GetTexture("_Tex"), typeof(Texture2D), true) as Texture2D);
			EditorGUILayout.EndVertical();
		} else {
		}

		GUILayout.BeginVertical(GUI.skin.box);
		if(GUILayout.Button(createNewMapExpanded ? "Close" : "Create New Map")) {
			createNewMapExpanded = !createNewMapExpanded;
		}
		if(createNewMapExpanded) {
			newMapProperties.importTexture = EditorGUILayout.ObjectField("Import", newMapProperties.importTexture, typeof(Texture2D), false) as Texture2D;
			if(newMapProperties.importTexture != null) {
				if(newMapProperties.importTexture.width != newMapProperties.importTexture.height) {
					EditorGUILayout.HelpBox("Import texture must have equal width and height", MessageType.Error);
				} else {
					bool needsReimporting = false;
					string path = UnityEditor.AssetDatabase.GetAssetPath(newMapProperties.importTexture);
					UnityEditor.TextureImporter textureImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
					if(!textureImporter.isReadable) {
						EditorGUILayout.HelpBox("Texture must allow read/write access", MessageType.Error);
						if(GUILayout.Button("Fix")) {
							textureImporter.isReadable = true;
							needsReimporting = true;
						}
					}
					if(textureImporter.mipmapEnabled) {
						EditorGUILayout.HelpBox("Texture should have mip maps disabled", MessageType.Warning);
						if(GUILayout.Button("Fix")) {
							textureImporter.mipmapEnabled = false;
							needsReimporting = true;
						}
					}
					if(needsReimporting)
						UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
				}
			}

			if(newMapProperties.importTexture == null) {
				newMapProperties.name = EditorGUILayout.TextField("Name", newMapProperties.name);
				newMapProperties.size = EditorGUILayout.IntField("Size", newMapProperties.size);
			} else {
				EditorGUILayout.TextField("Name", newMapProperties.name);
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.IntField("Size", Mathf.RoundToInt(newMapProperties.importTexture.width));
				EditorGUI.EndDisabledGroup();
			}

			EditorGUI.BeginDisabledGroup(!newMapProperties.isValid);
			if(GUILayout.Button("Create New Map")) {
				editorWindow.CreateNewMap(newMapProperties);
			}
			EditorGUI.EndDisabledGroup();
		}
		GUILayout.EndVertical();

		GUILayout.EndScrollView();
	}
}