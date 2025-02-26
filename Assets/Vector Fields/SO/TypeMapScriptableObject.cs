﻿using UnityEditor;
using UnityEngine;

public abstract class TypeMapScriptableObject<T> : ScriptableObject where T : Grid {
	public const TextureFormat textureFormat = TextureFormat.RGBAFloat;

	public Point size;
	public float maxComponent = 1;
	public Texture2D texture;

	public abstract T CreateMap ();


	public virtual void Save (T map) {
		SetMaxComponent(map);
		CreateTextureAsset(CreateTexture(map), ref texture, "Texture");
	}

	public abstract void SetMaxComponent (T map);

	public virtual Texture2D CreateTexture (T map) {
		Texture2D _texture = new Texture2D(map.size.x, map.size.y, textureFormat, false);
		_texture.filterMode = FilterMode.Point;
		_texture.SetPixels(GetMapColors(map));
		_texture.Apply();
		return _texture;
	}

	public abstract Color[] GetMapColors (T map);

	public virtual void CreateTextureAsset (Texture2D sourceTexture, ref Texture2D outputTextureAsset, string _fileName) {
		if(sourceTexture == null) {
			Debug.LogError("Texture is null!");
			return;
		}
		#if UNITY_EDITOR
		int width = sourceTexture.width;
		int height = sourceTexture.height;

		string fileName = string.Format("{0}_{1}", name, _fileName);
		ScreenshotExportSettings exportSettings = null;
		string pathName = AssetDatabase.GetAssetPath(this).BeforeLast(name);
		exportSettings = new ScreenshotExportSettings(sourceTexture, pathName, fileName, ScreenshotExportFormat.PNG);
		ScreenshotExporter.Export(exportSettings);

		var filePath = exportSettings.filePath;
		Debug.Log(filePath);

		TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(filePath);
		textureImporter.maxTextureSize = Mathf.Max(width, height);
		textureImporter.textureCompression = TextureImporterCompression.Compressed;
		TextureImporterSettings importerSettings = new TextureImporterSettings();
		importerSettings.readable = true;
		importerSettings.mipmapEnabled = false;
		importerSettings.filterMode = FilterMode.Point;
		textureImporter.SetTextureSettings(importerSettings);
		AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceUpdate);

		outputTextureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
		EditorUtility.SetDirty(this);
		AssetDatabase.SaveAssets();
		#endif
	}
}
