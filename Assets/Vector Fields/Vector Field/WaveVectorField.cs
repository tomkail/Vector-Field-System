using UnityEngine;

// Code-callable generator: animates a source vector field by modulating its magnitude with a gust wave that travels
// along the flow in world space (wind-ripple). The component (WaveVectorFieldComponent) is a thin wrapper over this.
public static class WaveVectorField {
    static ComputeShader waveVectorFieldComputeShader;
    static ComputeShader WaveVectorFieldComputeShader =>
        waveVectorFieldComputeShader ? waveVectorFieldComputeShader
            : (waveVectorFieldComputeShader = Resources.Load<ComputeShader>("WaveVectorField"));

    static readonly int ID_Result = Shader.PropertyToID("Result");
    static readonly int ID_SourceTex = Shader.PropertyToID("SourceTex");
    static readonly int ID_width = Shader.PropertyToID("width");
    static readonly int ID_height = Shader.PropertyToID("height");
    static readonly int ID_gridToWorldMatrix = Shader.PropertyToID("gridToWorldMatrix");
    static readonly int ID_waveScale = Shader.PropertyToID("waveScale");
    static readonly int ID_waveSpeed = Shader.PropertyToID("waveSpeed");
    static readonly int ID_waveAmount = Shader.PropertyToID("waveAmount");
    static readonly int ID_time = Shader.PropertyToID("time");

    // Writes the gust-modulated source field into target (encoded, unit-magnitude — the caller applies its own
    // magnitude/cookie afterwards). gridToSampleMatrix maps a grid cell to the world position used for the wave phase.
    public static void Dispatch(RenderTexture target, RenderTexture source, Vector2Int gridSize,
                                Matrix4x4 gridToSampleMatrix, float waveScale, float waveSpeed, float waveAmount,
                                float time) {
        var shader = WaveVectorFieldComputeShader;
        if (shader == null || target == null || source == null) return;

        shader.SetTexture(0, ID_Result, target);
        shader.SetTexture(0, ID_SourceTex, source);
        shader.SetInt(ID_width, gridSize.x);
        shader.SetInt(ID_height, gridSize.y);
        shader.SetMatrix(ID_gridToWorldMatrix, gridToSampleMatrix);
        shader.SetFloat(ID_waveScale, waveScale);
        shader.SetFloat(ID_waveSpeed, waveSpeed);
        shader.SetFloat(ID_waveAmount, Mathf.Clamp01(waveAmount));
        shader.SetFloat(ID_time, time);

        int groupsX = Mathf.CeilToInt(gridSize.x / 8f);
        int groupsY = Mathf.CeilToInt(gridSize.y / 8f);
        shader.Dispatch(0, groupsX, groupsY, 1);
    }
}
