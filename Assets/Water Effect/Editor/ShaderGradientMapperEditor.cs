using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;

[CustomEditor(typeof(ShaderGradientMapper))]
public class ShaderGradientMapperEditor : Editor {
 
     public override void OnInspectorGUI () {
        ShaderGradientMapper gradientMapper = target as ShaderGradientMapper;
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if(EditorGUI.EndChangeCheck()) {
            gradientMapper.Refresh();
        }
        var textureProperty = serializedObject.FindProperty("texture");
        if(!EditorUtility.IsPersistent(textureProperty.objectReferenceValue)) {
            if (GUILayout.Button("Save Texture Asset")) {
                gradientMapper.Refresh();
                
                if (!AssetDatabase.IsValidFolder ("Assets/GradientMaps")) {
                    AssetDatabase.CreateFolder ("Assets/", "GradientMaps");
                    AssetDatabase.SaveAssets ();
                }
                if (!Directory.Exists (Application.dataPath + "GradientMaps")) {
                    Directory.CreateDirectory (Application.dataPath + "/GradientMaps/");
                    ShaderGradientMapper.totalMaps = 0;
                } else {
                    ShaderGradientMapper.totalMaps = Directory.GetFiles (Application.dataPath + "/GradientMaps/").Length;
                }
    
                byte[] bytes = ((Texture2D)textureProperty.objectReferenceValue).EncodeToPNG ();
                var assetPath = SetAssetPath();
                string SetAssetPath () {
                    return assetPath = "GradientMaps/gradient_map_" + ShaderGradientMapper.totalMaps.ToString () + ".png";
                }
                while (File.Exists (Application.dataPath + "/"+SetAssetPath())) {
                    ShaderGradientMapper.totalMaps++;
                }
                File.WriteAllBytes (Application.dataPath + "/"+assetPath, bytes);
                AssetDatabase.Refresh ();
                
                var assetDatabasePath = "Assets/"+assetPath;
                TextureImporter textureImporter = (TextureImporter) AssetImporter.GetAtPath(assetDatabasePath);
                textureImporter.isReadable = true;
                AssetDatabase.ImportAsset(assetDatabasePath, ImportAssetOptions.ForceUpdate);
                textureProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(assetDatabasePath);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}