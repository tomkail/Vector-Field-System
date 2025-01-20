using System;
using UnityEngine;
using Object = UnityEngine.Object;

[System.Serializable]
public class VectorFieldCookieTextureCreator : IDisposable {
    static ComputeShader brushComputeShader;
    public static ComputeShader BrushComputeShader => brushComputeShader ? brushComputeShader : (brushComputeShader = Resources.Load<ComputeShader>("CircularBrushFalloff"));
    
    
    // protected Vector2Int gridSize;
    //
    // public Vector2Int GridSize {
    //     get => gridSize;
    //     set {
    //         gridSize = value;
    //         EnsureHasValidRenderTexture();
    //     }
    // }
    
    protected RenderTexture renderTexture;
    public RenderTexture RenderTexture => renderTexture;

    public static AnimationCurve CreateCurveWithHardness(float brushHardness) {
        return AnimationCurve.Linear(1f-brushHardness, 1, 0, 0);
    }
    
    
    ComputeShader computeShader;
    
    [PreviewTexture]
    public RenderTexture resultTexture;

    private ComputeBuffer curveBuffer;

    public VectorFieldCookieTextureCreator() {}
    
    public void Render(VectorFieldCookieTextureCreatorSettings settings)
    {
        if(computeShader == null) computeShader = Object.Instantiate(BrushComputeShader);
        
        int kernel = computeShader.FindKernel("CSMain");

        // Create the result texture
        if (resultTexture == null)
        {
            resultTexture = new RenderTexture(settings.gridSize.x, settings.gridSize.y, 0, RenderTextureFormat.ARGBFloat) {
                enableRandomWrite = true
            };
            resultTexture.Create();
        }


        switch (settings.generationMode) {
            case VectorFieldCookieTextureCreatorSettings.GenerationMode.Exponent:
                computeShader.EnableKeyword("FalloffSoftness");
                computeShader.DisableKeyword("AnimationCurve");
                computeShader.SetFloat("falloffSoftness", settings.falloffSoftness);
                break;
            case VectorFieldCookieTextureCreatorSettings.GenerationMode.AnimationCurve:
                computeShader.EnableKeyword("AnimationCurve");
                computeShader.DisableKeyword("FalloffSoftness");
                
                var curveResolution = Mathf.Max(settings.gridSize.x, settings.gridSize.y);
                // Generate the curve buffer data
                float[] curveData = new float[curveResolution];
                for (int i = 0; i < curveResolution; i++)
                {
                    float t = i / (curveResolution - 1.0f);
                    curveData[i] = settings.animationCurve.Evaluate(t);
                }

                // Create the buffer and assign it to the compute shader
                curveBuffer = new ComputeBuffer(curveResolution, sizeof(float));
                curveBuffer.SetData(curveData);
                
                computeShader.SetBuffer(kernel, "CurvePoints", curveBuffer);
                computeShader.SetInt("CurvePointCount", curveResolution);
                break;
        }
        

        // Pass texture dimensions to the compute shader
        computeShader.SetInt("textureWidth", resultTexture.width);
        computeShader.SetInt("textureHeight", resultTexture.height);
        
        computeShader.SetTexture(kernel, "ResultTexture", resultTexture);

        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(resultTexture.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(resultTexture.height / 8.0f);
        computeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }

    public void Dispose() {
        // Release the buffer when done
        curveBuffer?.Release();
        Object.DestroyImmediate(computeShader);
    }
    
    public void EnsureHasValidRenderTexture(Vector2Int size) {
        var renderTextureDescriptor = new RenderTextureDescriptor(size.x, size.y, RenderTextureFormat.ARGBFloat, 0) {
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
    }
    
    public void ReleaseRenderTexture () {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        renderTexture.Release();
    }

    public void DestroyRenderTexture() {
        if(renderTexture == null) return;
        if(RenderTexture.active == renderTexture) RenderTexture.active = null;
        if(Application.isPlaying) Object.Destroy(renderTexture);
        else Object.DestroyImmediate(renderTexture);
        renderTexture = null;
    }
}