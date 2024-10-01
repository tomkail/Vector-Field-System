using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways] // Ensure this script runs in both edit mode and play mode
public class CombineVectorFields : MonoBehaviour
{
    public VectorFieldComponent[] vectorFields;  // Array of input vector field components
    public Material combineShaderMaterial;  // Material for combining vector fields
    [PreviewTexture(200, ScaleMode.ScaleToFit)]
    public RenderTexture outputTexture;  // The output texture
    private Matrix4x4[] relativeTransforms;

    void Update()
    {
        if (vectorFields == null || vectorFields.Length == 0 ||
            combineShaderMaterial == null)
            return;

        Combine();
    }

    void Combine()
    {
        // Create or recreate the output texture
        if (outputTexture == null || !outputTexture.IsCreated())
        {
            outputTexture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32);
            outputTexture.Create();
        }

        // Clear the output texture with black using GL.Clear
        RenderTexture.active = outputTexture;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = null;

        // Prepare relative transforms
        relativeTransforms = new Matrix4x4[vectorFields.Length];
        for (int i = 0; i < vectorFields.Length; i++)
        {
            relativeTransforms[i] = GetRelativeTransform(vectorFields[i].transform, transform);
        }

        // Combine vector fields
        for (int i = 0; i < vectorFields.Length; i++)
        {

            if (vectorFields[i] == null || vectorFields[i].vectorFieldTexture == null) continue;

            combineShaderMaterial.SetTexture("_VectorField", vectorFields[i].vectorFieldTexture);
            combineShaderMaterial.SetMatrix("_RelativeTransform", relativeTransforms[i]);
            combineShaderMaterial.SetVector("_TextureSize", new Vector4(vectorFields[i].vectorFieldTexture.width, vectorFields[i].vectorFieldTexture.height, 0, 0));

            Graphics.Blit(null, outputTexture, combineShaderMaterial);
        }
    }

    // Calculate the relative transformation matrix between texture and canvas
    Matrix4x4 GetRelativeTransform(Transform textureTransform, Transform canvasTransform)
    {
        // Get the transformation of the texture relative to the canvas
        Matrix4x4 canvasToWorld = canvasTransform.localToWorldMatrix;
        Matrix4x4 textureToWorld = textureTransform.localToWorldMatrix;
        return canvasToWorld * textureToWorld.inverse;
    }

    // Optional: Debug output to a material in the scene or some visualization
    public void SetDebugMaterial(Material debugMaterial)
    {
        if (debugMaterial != null && outputTexture != null)
        {
            debugMaterial.SetTexture("_MainTex", outputTexture);
        }
    }
}
