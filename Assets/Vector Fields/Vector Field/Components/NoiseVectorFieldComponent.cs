using System.Linq;
using UnityEngine;

public class NoiseVectorFieldComponent : VectorFieldComponent
{
    public enum Space
    {
        Local,
        World
    }
    public Space space = Space.Local;
    public NoiseSampler noiseSampler;
    public float vortexAngle = 90;

    static ComputeShader _computeShader;
    public static ComputeShader computeShader => _computeShader ?? (_computeShader = Resources.Load<ComputeShader>("NoiseVectorField"));

    // Must match what's in the compute shader
    const int threadsPerGroupX = 16;
    const int threadsPerGroupY = 16;

    protected override void RenderInternal()
    {
        RenderInternalGPU();
    }


    RenderTexture renderTexture;
    void Awake() {
	    renderTexture = null;
    }
    
    public void ReleaseRenderTexture () {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        renderTexture.Release();
    }

    public void DestroyRenderTexture() {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        if(Application.isPlaying) Destroy(renderTexture);
        else DestroyImmediate(renderTexture);
        renderTexture = null;
    }
    
    void RenderInternalGPU()
    {
        var renderTextureDescriptor = new RenderTextureDescriptor(gridRenderer.gridSize.x, gridRenderer.gridSize.y, RenderTextureFormat.ARGBFloat, 0) {
            enableRandomWrite = true,
		};
	    if (renderTexture == null) {
		    renderTexture = new RenderTexture (renderTextureDescriptor) {
			    filterMode = FilterMode.Bilinear
		    };
	    } else if(!RenderTextureDescriptorsMatch(renderTexture.descriptor, renderTextureDescriptor)) {
		    var rtFilterMode = renderTexture.filterMode;
                
		    if(RenderTexture.active == renderTexture) RenderTexture.active = null;
		    renderTexture.Release();

		    renderTexture.descriptor = renderTextureDescriptor;
		    renderTexture.Create();
		    renderTexture.filterMode = rtFilterMode;
	    }
	    static bool RenderTextureDescriptorsMatch(RenderTextureDescriptor descriptorA, RenderTextureDescriptor descriptorB) {
		    if (descriptorA.depthBufferBits != descriptorB.depthBufferBits) return false;
		    if (descriptorA.width != descriptorB.width) return false;
		    if (descriptorA.height != descriptorB.height) return false;
		    if (descriptorA.depthStencilFormat != descriptorB.depthStencilFormat) return false;
		    if (descriptorA.enableRandomWrite != descriptorB.enableRandomWrite) return false;
		    if (descriptorA.colorFormat != descriptorB.colorFormat) return false;
		    if (descriptorA.dimension != descriptorB.dimension) return false;
		    return true;
	    }
        
        // Set compute shader parameters
        computeShader.SetTexture(0, "Result", renderTexture);
        computeShader.SetInt("width", gridRenderer.gridSize.x);
        computeShader.SetInt("height", gridRenderer.gridSize.y);
        computeShader.SetFloat("magnitude", magnitude);

        Matrix4x4 gridToWorldMatrix = Matrix4x4.identity;
        if (space == Space.Local)
            gridToWorldMatrix = Matrix4x4.Translate(new Vector3(1000f, 0, 0)) * gridRenderer.cellCenter.gridToLocalMatrix * Matrix4x4.Translate(noiseSampler.position);
        else if (space == Space.World)
            gridToWorldMatrix = gridRenderer.cellCenter.gridToWorldMatrix * Matrix4x4.Translate(noiseSampler.position);
        computeShader.SetMatrix("gridToWorldMatrix", gridToWorldMatrix);

        computeShader.SetFloat("frequency", noiseSampler.properties.frequency);
        computeShader.SetFloat("persistence", noiseSampler.properties.persistence);
        computeShader.SetFloat("lacunarity", noiseSampler.properties.lacunarity);
        computeShader.SetInt("numOctaves", noiseSampler.properties.octaves);
        computeShader.SetFloat("vortexAngle", vortexAngle);

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)gridRenderer.gridSize.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)gridRenderer.gridSize.y / threadsPerGroupY);

        // Dispatch the compute shader
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        
        CreateVectorFieldTexture();

        // Convert RenderTexture to Texture2D
        RenderTexture.active = renderTexture;
        vectorFieldTexture.ReadPixels(new Rect(0, 0, vectorFieldTexture.width, vectorFieldTexture.height), 0, 0);
        vectorFieldTexture.Apply();
        RenderTexture.active = null;

        // Update the vectorField data
        Color[] colors = vectorFieldTexture.GetPixels();
        Vector2[] vectors = VectorFieldUtils.ColorsToVectors(colors, 1);
        vectorField = new Vector2Map(new Point(vectorFieldTexture.width, vectorFieldTexture.height), vectors);
    }
}