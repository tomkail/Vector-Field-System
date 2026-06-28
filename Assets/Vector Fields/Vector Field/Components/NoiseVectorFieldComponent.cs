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

    // Re-render when any noise parameter changes. If something animates noiseSampler.position over time, this
    // detects it each frame (so the field updates), and when nothing changes nothing re-renders.
    Space lastSpace;
    Vector3 lastPosition = new Vector3(float.NaN, 0, 0);
    float lastVortexAngle = float.NaN;
    NoiseSamplerProperties lastProperties;
    protected override bool ParametersChanged() {
        bool changed = base.ParametersChanged();
        if (lastSpace != space) { lastSpace = space; changed = true; }
        if (lastPosition != noiseSampler.position) { lastPosition = noiseSampler.position; changed = true; }
        if (lastVortexAngle != vortexAngle) { lastVortexAngle = vortexAngle; changed = true; }
        if (lastProperties != noiseSampler.properties) { lastProperties = noiseSampler.properties; changed = true; }
        return changed;
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
        NoiseVectorFieldComputeShader.SetFloat("numOctaves", noiseSampler.properties.octaves);
        NoiseVectorFieldComputeShader.SetFloat("vortexAngle", vortexAngle);

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)gridRenderer.gridSize.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)gridRenderer.gridSize.y / threadsPerGroupY);

        // Dispatch the compute shader
        NoiseVectorFieldComputeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }
}