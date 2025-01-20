using UnityEngine;

[System.Serializable]
public class VectorFieldBrushTextureCreator : VectorFieldTextureCreator {
    static ComputeShader stampVectorFieldComputeShader;
    public static ComputeShader StampVectorFieldComputeShader => stampVectorFieldComputeShader ? stampVectorFieldComputeShader : (stampVectorFieldComputeShader = Resources.Load<ComputeShader>("StampVectorField"));

    
    ComputeShader computeShader;
    
    VectorFieldBrushSettings _brushSettingsParams;
    public VectorFieldBrushSettings BrushSettingsParams {
        get => _brushSettingsParams;
        set {
            _brushSettingsParams = value;
            // Render();
        }
    }
    
    public VectorFieldBrushTextureCreator(Vector2Int gridSize, VectorFieldBrushSettings brushSettingsParams) : base(gridSize) {
        computeShader = Object.Instantiate(StampVectorFieldComputeShader);
        this._brushSettingsParams = brushSettingsParams;
    }
    
    public override void Dispose() {
        base.Dispose();
        Object.DestroyImmediate(computeShader);
    }
    
    protected override void RenderInternal() {
        EnsureHasValidRenderTexture();
        
        // Must match what's in the compute shader
        const int threadsPerGroupX = 16;
        const int threadsPerGroupY = 16;
        
        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);
        computeShader.SetInt("NumThreadGroupsX", threadGroupsX);

        computeShader.SetTexture(0, "Result", renderTexture);
        computeShader.SetInt("width", gridSize.x);
        computeShader.SetInt("height", gridSize.y);

        computeShader.SetFloat("magnitude", magnitude);
        computeShader.SetFloat("directionalAngle", _brushSettingsParams.directionalAngle);
        computeShader.SetFloat("vortexAngle", _brushSettingsParams.vortexAngle);

        if (_brushSettingsParams.forceType == VectorFieldBrushSettings.ForceEmitterType.Directional)
        {
            computeShader.EnableKeyword("DIRECTIONAL");
            computeShader.DisableKeyword("SPOT");
        }
        else if (_brushSettingsParams.forceType == VectorFieldBrushSettings.ForceEmitterType.Spot)
        {
            computeShader.EnableKeyword("SPOT");
            computeShader.DisableKeyword("DIRECTIONAL");
        }

        // CreateRampTextureFromAnimationCurve(brushParams.falloffCurve, 32, ref curveTexture);
        // computeShader.SetTexture(0, "curveTexture", curveTexture);
        computeShader.SetTexture(0, "cookieTexture", cookieTexture != null ? cookieTexture : Texture2D.whiteTexture);
        
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }





    public static void CreateVectorField(Vector2Int gridSize, float magnitude, VectorFieldBrushSettings _brushSettingsParams, Texture2D cookieTexture, ref RenderTexture renderTexture) {
        // Create a render texture.
        bool needsCreateRenderTexture = renderTexture == null;
        // Potential upgrade: take RenderTextureDescriptor as a parameter and use those settings if the RenderTexture is null, rather than prescribing the settings
        RenderTextureFormat rtFormat = RenderTextureFormat.ARGB32;
        FilterMode rtFilterMode = FilterMode.Bilinear;
        if(renderTexture != null && (renderTexture.width != gridSize.x || renderTexture.height != gridSize.y || renderTexture.format != rtFormat)) {
            rtFormat = renderTexture.format;
            rtFilterMode = renderTexture.filterMode;
            renderTexture.Release();
            needsCreateRenderTexture = true;
        }
        if(needsCreateRenderTexture && gridSize.x > 0 && gridSize.y > 0) {
            renderTexture = new RenderTexture (gridSize.x, gridSize.y, 0, rtFormat) {
                filterMode = rtFilterMode
            };
        }
        
        
        var computeShader = Object.Instantiate(StampVectorFieldComputeShader);
        
        // Must match what's in the compute shader
        const int threadsPerGroupX = 16;
        const int threadsPerGroupY = 16;
        
        // Calculate the number of thread groups
        int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);
        computeShader.SetInt("NumThreadGroupsX", threadGroupsX);

        computeShader.SetTexture(0, "Result", renderTexture);
        computeShader.SetInt("width", gridSize.x);
        computeShader.SetInt("height", gridSize.y);

        computeShader.SetFloat("magnitude", magnitude);
        computeShader.SetFloat("directionalAngle", _brushSettingsParams.directionalAngle);
        computeShader.SetFloat("vortexAngle", _brushSettingsParams.vortexAngle);

        if (_brushSettingsParams.forceType == VectorFieldBrushSettings.ForceEmitterType.Directional)
        {
            computeShader.EnableKeyword("DIRECTIONAL");
            computeShader.DisableKeyword("SPOT");
        }
        else if (_brushSettingsParams.forceType == VectorFieldBrushSettings.ForceEmitterType.Spot)
        {
            computeShader.EnableKeyword("SPOT");
            computeShader.DisableKeyword("DIRECTIONAL");
        }

        // CreateRampTextureFromAnimationCurve(brushParams.falloffCurve, 32, ref curveTexture);
        // computeShader.SetTexture(0, "curveTexture", curveTexture);
        computeShader.SetTexture(0, "cookieTexture", cookieTexture != null ? cookieTexture : Texture2D.whiteTexture);
        
        computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        Object.DestroyImmediate(computeShader);
    }

    public static RenderTexture CreateVectorField(Vector2Int gridSize, float magnitude, VectorFieldBrushSettings _brushSettingsParams, Texture2D cookieTexture) {
        RenderTexture renderTexture = new RenderTexture(gridSize.x, gridSize.y, 0, RenderTextureFormat.ARGBFloat, 0) {
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear
        };
        CreateVectorField(gridSize, magnitude, _brushSettingsParams, cookieTexture, ref renderTexture);
        return renderTexture;
    }
}