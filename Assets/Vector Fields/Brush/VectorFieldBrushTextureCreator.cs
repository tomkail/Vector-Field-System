using UnityEngine;

// Generates a "stamp" vector field (a single directional or spot/vortex emitter, shaped by a cookie/falloff) into
// an ARGBFloat render texture on the GPU.
//
// Two ways to use it, both routing through the same Dispatch:
//   * Statically — VectorFieldBrushTextureCreator.Dispatch(...) — for code that already owns its target texture
//     (e.g. StampVectorFieldComponent writes straight into the component's renderTexture).
//   * As an instance — for callers that want a creator object owning its own texture (e.g. the scene drawing tool).
//     The instance inherits its texture lifecycle from VectorFieldTextureCreator.
[System.Serializable]
public class VectorFieldBrushTextureCreator : VectorFieldTextureCreator
{
	static ComputeShader stampVectorFieldComputeShader;
	public static ComputeShader StampVectorFieldComputeShader => stampVectorFieldComputeShader ? stampVectorFieldComputeShader : (stampVectorFieldComputeShader = Resources.Load<ComputeShader>("StampVectorField"));

	// One instantiated copy of the compute shader, shared by every static dispatch. Dispatches are serial on the
	// main thread and every parameter/keyword is set per dispatch, so a single shared instance is safe and saves
	// instantiating+destroying a shader on every render.
	static ComputeShader sharedStampShader;
	static ComputeShader SharedStampShader => sharedStampShader ? sharedStampShader : (sharedStampShader = Object.Instantiate(StampVectorFieldComputeShader));

	// Must match what's in the compute shader
	const int threadsPerGroupX = 16;
	const int threadsPerGroupY = 16;

	VectorFieldBrushSettings _brushSettingsParams;
	public VectorFieldBrushSettings BrushSettingsParams
	{
		get => _brushSettingsParams;
		set => _brushSettingsParams = value;
	}

	public VectorFieldBrushTextureCreator(Vector2Int gridSize, VectorFieldBrushSettings brushSettingsParams) : base(gridSize)
	{
		this._brushSettingsParams = brushSettingsParams;
	}

	protected override void RenderInternal()
	{
		EnsureHasValidRenderTexture();
		Dispatch(renderTexture, gridSize, magnitude, _brushSettingsParams, cookieTexture);
	}

	// The single dispatch routine. `target` must already be a valid ARGBFloat random-write texture of gridSize;
	// `cookieTexture` shapes the stamp (a Texture2D or RenderTexture); null falls back to a solid white cookie.
	public static void Dispatch(RenderTexture target, Vector2Int gridSize, float magnitude, VectorFieldBrushSettings brushSettings, Texture cookieTexture)
	{
		if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

		var computeShader = SharedStampShader;

		int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
		int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);
		computeShader.SetInt("NumThreadGroupsX", threadGroupsX);

		computeShader.SetTexture(0, "Result", target);
		computeShader.SetInt("width", gridSize.x);
		computeShader.SetInt("height", gridSize.y);

		computeShader.SetFloat("magnitude", magnitude);
		computeShader.SetFloat("directionalAngle", brushSettings.directionalAngle);
		computeShader.SetFloat("vortexAngle", brushSettings.vortexAngle);

		if (brushSettings.forceType == VectorFieldBrushSettings.ForceEmitterType.Directional)
		{
			computeShader.EnableKeyword("DIRECTIONAL");
			computeShader.DisableKeyword("SPOT");
		}
		else if (brushSettings.forceType == VectorFieldBrushSettings.ForceEmitterType.Spot)
		{
			computeShader.EnableKeyword("SPOT");
			computeShader.DisableKeyword("DIRECTIONAL");
		}

		computeShader.SetTexture(0, "cookieTexture", cookieTexture != null ? cookieTexture : Texture2D.whiteTexture);

		computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	}
}
