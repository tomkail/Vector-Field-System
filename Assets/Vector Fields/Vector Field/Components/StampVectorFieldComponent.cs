using UnityEngine;

public class StampVectorFieldComponent : VectorFieldComponent {
    public VectorFieldBrush brushParams;
    Texture2D curveTexture;

    public ComputeShader computeShader;
    ComputeBuffer computeBuffer;

    // Must match what's in the compute shader
    const int threadsPerGroupX = 16;
    const int threadsPerGroupY = 16;


    protected override void RenderInternal() {
        // CPU version
        // vectorField = VectorFieldBrush.CreateVectorField(brushParams, gridRenderer.gridSize);
        
        vectorField = new Vector2Map(gridRenderer.gridSize);

        // Initialize ComputeBuffer
        computeBuffer = new ComputeBuffer(vectorField.values.Length, sizeof(float) * 2);
        computeShader.SetBuffer(0, "Result", computeBuffer);

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)vectorField.size.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)vectorField.size.y / threadsPerGroupY);

        
        computeShader.SetInt("NumThreadGroupsX", threadGroupsX);
        computeShader.SetInt("width", vectorField.size.x);
        computeShader.SetInt("height", vectorField.size.y);
        
        computeShader.SetFloat("magnitude", magnitude);
        computeShader.SetFloat("directionalAngle", brushParams.directionalAngle);
        computeShader.SetFloat("vortexAngle", brushParams.vortexAngle);
        
        if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Directional) {
            computeShader.EnableKeyword("DIRECTIONAL");
            computeShader.DisableKeyword("SPOT");
        } else if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Spot) {
            computeShader.EnableKeyword("SPOT");
            computeShader.DisableKeyword("DIRECTIONAL");
        }

        curveTexture = CreateRampTextureFromAnimationCurve(brushParams.falloffCurve, 32);
        computeShader.SetTexture(0, "curveTexture", curveTexture);
        
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        computeBuffer.GetData(vectorField.values);
        computeBuffer.Release();
    }

    
}