using UnityEngine;

public class NoiseVectorFieldComponent : VectorFieldComponent {
    static ComputeShader noiseVectorFieldComputeShader;
    public static ComputeShader NoiseVectorFieldComputeShader => noiseVectorFieldComputeShader ? noiseVectorFieldComputeShader : (noiseVectorFieldComputeShader = Resources.Load<ComputeShader>("NoiseVectorField"));
    
    ComputeShader _computeShader;
    public ComputeShader computeShader => _computeShader ? _computeShader : (_computeShader = Instantiate(NoiseVectorFieldComputeShader));
    
    public enum Space {
        Local,
        World
    }
    public Space space = Space.Local;
    public NoiseSampler noiseSampler;
    public float vortexAngle = 90;


    // Must match what's in the compute shader
    const int threadsPerGroupX = 16;
    const int threadsPerGroupY = 16;

    protected override void RenderInternal() {
        RenderInternalGPU();
    }
    
    void RenderInternalGPU() {
	    EnsureHasValidRenderTexture();
        
        // Set compute shader parameters
        NoiseVectorFieldComputeShader.SetTexture(0, "Result", renderTexture);
        NoiseVectorFieldComputeShader.SetInt("width", gridRenderer.gridSize.x);
        NoiseVectorFieldComputeShader.SetInt("height", gridRenderer.gridSize.y);
        NoiseVectorFieldComputeShader.SetFloat("magnitude", magnitude);

        Matrix4x4 gridToWorldMatrix = Matrix4x4.identity;
        if (space == Space.Local)
            gridToWorldMatrix = Matrix4x4.Translate(new Vector3(1000f, 0, 0)) * gridRenderer.cellCenter.gridToLocalMatrix * Matrix4x4.Translate(noiseSampler.position);
        else if (space == Space.World)
            gridToWorldMatrix = gridRenderer.cellCenter.gridToWorldMatrix * Matrix4x4.Translate(noiseSampler.position);
        NoiseVectorFieldComputeShader.SetMatrix("gridToWorldMatrix", gridToWorldMatrix);

        NoiseVectorFieldComputeShader.SetFloat("frequency", noiseSampler.properties.frequency);
        NoiseVectorFieldComputeShader.SetFloat("persistence", noiseSampler.properties.persistence);
        NoiseVectorFieldComputeShader.SetFloat("lacunarity", noiseSampler.properties.lacunarity);
        NoiseVectorFieldComputeShader.SetInt("numOctaves", noiseSampler.properties.octaves);
        NoiseVectorFieldComputeShader.SetFloat("vortexAngle", vortexAngle);

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)gridRenderer.gridSize.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)gridRenderer.gridSize.y / threadsPerGroupY);

        // Dispatch the compute shader
        NoiseVectorFieldComputeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }
}