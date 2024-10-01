using UnityEngine;

public class StampVectorFieldComponent : VectorFieldComponent
{
    public VectorFieldBrush brushParams;
    Texture2D curveTexture;

    static ComputeShader _computeShader;
    public static ComputeShader computeShader => _computeShader ?? (_computeShader = Resources.Load<ComputeShader>("StampVectorField"));

    ComputeBuffer computeBuffer;

    // Must match what's in the compute shader
    const int threadsPerGroupX = 16;
    const int threadsPerGroupY = 16;


    protected override void RenderInternal()
    {
        // RenderInternalCPU();
        RenderInternalGPU();
    }

    void RenderInternalCPU()
    {
        vectorField = VectorFieldBrush.CreateVectorField(brushParams, gridRenderer.gridSize);
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

        // Initialize ComputeBuffer
        if (computeBuffer == null || computeBuffer.count != vectorField.values.Length)
        {
            computeBuffer = new ComputeBuffer(vectorField.values.Length, sizeof(float) * 2);
        }

        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)vectorField.size.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)vectorField.size.y / threadsPerGroupY);
        computeShader.SetInt("NumThreadGroupsX", threadGroupsX);

        computeShader.SetTexture(0, "Result", renderTexture);
        computeShader.SetInt("width", vectorField.size.x);
        computeShader.SetInt("height", vectorField.size.y);

        computeShader.SetFloat("magnitude", magnitude);
        computeShader.SetFloat("directionalAngle", brushParams.directionalAngle);
        computeShader.SetFloat("vortexAngle", brushParams.vortexAngle);

        if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Directional)
        {
            computeShader.EnableKeyword("DIRECTIONAL");
            computeShader.DisableKeyword("SPOT");
        }
        else if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Spot)
        {
            computeShader.EnableKeyword("SPOT");
            computeShader.DisableKeyword("DIRECTIONAL");
        }

        CreateRampTextureFromAnimationCurve(brushParams.falloffCurve, 32, ref curveTexture);
        computeShader.SetTexture(0, "curveTexture", curveTexture);

        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        // This is very slow! We might want to prefer writing to a rendertexture instead.
        // computeBuffer.GetData(vectorField.values);
        // computeBuffer.Release();

        
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

    protected override void OnDisable()
    {
        computeBuffer?.Release();
        computeBuffer = null;
    }
}