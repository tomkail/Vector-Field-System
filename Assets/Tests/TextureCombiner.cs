using System;
using UnityEngine;

// This code attempted to combine multiple textures into a single texture using a compute shader. It turns out Texture2DArray only works if the textures are the same size.
[ExecuteAlways]
public class TextureCombiner : MonoBehaviour
{
    public ComputeShader computeShader;
    public Texture2D[] textures;
    public Layer[] layers;
    public RenderTexture resultTexture;

    private ComputeBuffer layerBuffer;
    private Texture2DArray textureArray;

    void Update()
    {
        // Create a Texture2DArray with the largest dimensions found among the textures
        int maxWidth = textures.Max(t => t.width);
        int maxHeight = textures.Max(t => t.height);
        textureArray = new Texture2DArray(maxWidth, maxHeight, textures.Length, textures[0].format, false);

        for (int i = 0; i < textures.Length; i++)
        {
            // Copy each texture into the Texture2DArray
            Graphics.CopyTexture(textures[i], 0, 0, textureArray, i, 0);
        }

        // Initialize the compute buffer
        layerBuffer = new ComputeBuffer(layers.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Layer)));
        for (int i = 0; i < layers.Length; i++) {
            layers[i].TextureIndex = i;
            layers[i].Width = textures[layers[i].TextureIndex].width;
            layers[i].Height = textures[layers[i].TextureIndex].height;
        }
        layerBuffer.SetData(layers);

        // Initialize the result texture
        resultTexture = new RenderTexture(maxWidth, maxHeight, 0, RenderTextureFormat.ARGB32);
        resultTexture.enableRandomWrite = true;
        resultTexture.Create();

        // Set compute shader parameters
        computeShader.SetBuffer(0, "Layers", layerBuffer);
        computeShader.SetTexture(0, "Textures", textureArray);
        computeShader.SetTexture(0, "ResultTexture", resultTexture);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(maxWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(maxHeight / 8.0f);
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }

    void OnDestroy()
    {
        // Release the compute buffer
        layerBuffer.Release();
    }
}

[System.Serializable]
public struct Layer {
    public int Width;
    public int Height;
    public int TextureIndex;

}
