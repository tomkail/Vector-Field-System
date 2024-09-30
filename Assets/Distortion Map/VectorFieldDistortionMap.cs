using System;
using UnityEngine;

[ExecuteAlways]
public class VectorFieldDistortionMap : MonoBehaviour {
    public VectorFieldComponent vectorField;
    public bool automaticScale;
    public float maxComponent = 1;
    float calculatedScale => automaticScale?VectorFieldScriptableObject.GetMaxAbsComponent(vectorField.vectorField.values):maxComponent;
    
    [AssetSaver] public Texture2D distortionMap;
    public Material distortionMaterial;
    void Update() {
        if (distortionMap == null || distortionMap.width != vectorField.vectorField.size.x || distortionMap.height != vectorField.vectorField.size.y) {
            distortionMap = new Texture2D(vectorField.vectorField.size.x, vectorField.vectorField.size.y, TextureFormat.RGFloat, false);
            distortionMap.filterMode = FilterMode.Bilinear;
        }
        var colors = VectorFieldUtils.VectorsToColors(vectorField.vectorField.values, 1f/calculatedScale);
        
        // Color[] colors = new Color[vectorField.vectorField.values.Length];
        // for(int i = 0; i < vectorField.vectorField.values.Length; i++) {
        //     colors[i] = new Color(vectorField.vectorField.values[i].x, vectorField.vectorField.values[i].y, 0);
        // }
        
        distortionMap.SetPixels(colors);
        distortionMap.Apply();
        distortionMaterial.SetTexture("_NormalMap", distortionMap);
    }

    void OnDisable() {
        ObjectX.DestroyAutomatic(distortionMap);
    }
}