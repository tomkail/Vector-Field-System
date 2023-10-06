using UnityEngine;
 
[ExecuteInEditMode]
public class ShaderGradientMapper : MonoBehaviour {
 
    [Header("Gradient map parameters")]
    public Vector2Int gradientMapDimensions = new Vector2Int(16, 1);
    public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
    public FilterMode filterMode = FilterMode.Bilinear;
    public Gradient gradient;
 
    [Header("Enable testing")]
    public bool testing = false;
    
    public Material material;
    public string propertyName = "_GradientMap";
 
    [Disable]
    public Texture2D texture;
 
    public static int totalMaps = 0;
 
    // void OnValidate () {
    //     Refresh();
    // }

    public void Refresh () {
        CreateTexture();
        Apply();
    }

    void CreateTexture () {
        if(texture == null) {
            texture = new Texture2D (gradientMapDimensions.x, gradientMapDimensions.y);
        } else if(texture.width != gradientMapDimensions.x && texture.height != gradientMapDimensions.y) {
            texture.Reinitialize(gradientMapDimensions.x, gradientMapDimensions.y);
        }
        texture.wrapMode = wrapMode;
        texture.filterMode = filterMode;
        for (int x = 0; x < gradientMapDimensions.x; x++) {
            Color color = gradient.Evaluate ((float) x / (float) gradientMapDimensions.x);
            for (int y = 0; y < gradientMapDimensions.y; y++) {
                texture.SetPixel (x, y, color);
            }
        }
        texture.Apply ();
    }

    void Apply () {
        if (material.HasProperty (propertyName)) {
            material.SetTexture (propertyName, texture);
        }
    }
}