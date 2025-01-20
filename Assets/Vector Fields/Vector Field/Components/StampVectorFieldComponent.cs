using UnityEngine;

[ExecuteAlways]
public class StampVectorFieldComponent : VectorFieldComponent {

	[SerializeReference]
	VectorFieldBrushTextureCreator vectorFieldBrushTextureCreator;
	// VectorFieldCookieTextureCreator cookieTextureCreator;
	//
	// VectorFieldCookieTextureCreatorSettings cookieTextureCreatorSettings;

	public VectorFieldBrushSettings brushSettingsParams;
	// public RenderTexture renderTexture => vectorFieldBrushTextureCreator.RenderTexture;

	protected override void EnsureInitialized() {
		base.EnsureInitialized();
		if (vectorFieldBrushTextureCreator == null || vectorFieldBrushTextureCreator.GridSize != new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y) || vectorFieldBrushTextureCreator.BrushSettingsParams != brushSettingsParams) {
			vectorFieldBrushTextureCreator = new VectorFieldBrushTextureCreator(new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y), brushSettingsParams);
			vectorFieldBrushTextureCreator.cookieTexture = cookieTexture;
		}
	}

	protected override void OnDisable() {
		// vectorFieldBrushTextureCreator.Dispose();
		// vectorFieldBrushTextureCreator = null;
		base.OnDisable();
	}

	// #if UNITY_EDITOR
	// 	protected override void OnValidate() {
	// 		base.OnValidate();
	// 		if (!isActiveAndEnabled) return;
	// 		if (vectorFieldBrushTextureCreator == null) {
	// 			vectorFieldBrushTextureCreator = new VectorFieldBrushTextureCreator(new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y), brushSettingsParams);
	// 		}
	// 	}
	// #endif

	protected override void RenderInternal() {
		if (vectorFieldBrushTextureCreator == null) {
			Debug.Log("VectorFieldBrushTextureCreator is null", this);
		}
		vectorFieldBrushTextureCreator.Render();
		renderTexture = vectorFieldBrushTextureCreator.RenderTexture;
	}

	// static ComputeShader stampVectorFieldComputeShader;
	// public static ComputeShader StampVectorFieldComputeShader => stampVectorFieldComputeShader ? stampVectorFieldComputeShader : (stampVectorFieldComputeShader = Resources.Load<ComputeShader>("StampVectorField"));
	//
	// ComputeShader _computeShader;
	// public ComputeShader computeShader => _computeShader ? _computeShader : (_computeShader = Instantiate(StampVectorFieldComputeShader));
	//
	// public VectorFieldBrush brushParams;
	// Texture2D curveTexture;
	//
	// // Must match what's in the compute shader
	// const int threadsPerGroupX = 16;
	// const int threadsPerGroupY = 16;
	//
	// protected override void OnEnable() {
	//     // vectorFieldBrushTextureCreator = new VectorFieldBrushTextureCreator(new Vector2Int(gridRenderer.gridSize.x, gridRenderer.gridSize.y), brushParams);
	//     base.OnEnable();
	// }
	//
	// protected override void OnDisable() {
	//     // computeBuffer?.Release();
	//     // computeBuffer = null;
	//     // vectorFieldBrushTextureCreator.Dispose();
	//     DestroyImmediate(computeShader);
	//     base.OnDisable();
	// }
	//
	// protected override void RenderInternal() {
	//     // RenderInternalCPU();
	//     RenderInternalGPU();
	// }
	//
	// // void RenderInternalCPU() {
	// //     vectorField = VectorFieldBrush.CreateVectorFieldCPU(brushParams, gridRenderer.gridSize);
	// // }
	//
	// void RenderInternalGPU() {
	//     EnsureHasValidRenderTexture();
	//
	//     // vectorFieldBrushTextureCreator.magnitude = magnitude;
	//     // vectorFieldBrushTextureCreator.Render();
	//
	//     // VectorFieldBrush.CreateVectorFieldGPU(brushParams, ref renderTexture);
	//
	//     // Calculate the number of thread groups
	//     int threadGroupsX = Mathf.CeilToInt((float)gridRenderer.gridSize.x / threadsPerGroupX);
	//     int threadGroupsY = Mathf.CeilToInt((float)gridRenderer.gridSize.y / threadsPerGroupY);
	//     computeShader.SetInt("NumThreadGroupsX", threadGroupsX);
	//
	//     computeShader.SetTexture(0, "Result", renderTexture);
	//     computeShader.SetInt("width", gridRenderer.gridSize.x);
	//     computeShader.SetInt("height", gridRenderer.gridSize.y);
	//
	//     computeShader.SetFloat("magnitude", magnitude);
	//     computeShader.SetFloat("directionalAngle", brushParams.directionalAngle);
	//     computeShader.SetFloat("vortexAngle", brushParams.vortexAngle);
	//
	//     if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Directional)
	//     {
	//         computeShader.EnableKeyword("DIRECTIONAL");
	//         computeShader.DisableKeyword("SPOT");
	//     }
	//     else if (brushParams.forceType == VectorFieldBrush.ForceEmitterType.Spot)
	//     {
	//         computeShader.EnableKeyword("SPOT");
	//         computeShader.DisableKeyword("DIRECTIONAL");
	//     }
	//
	//     // cookieTextureCreator = new VectorFieldCookieTextureCreator(new VectorFieldCookieTextureCreatorSettings())
	//     // cookieTextureCreator.Render();
	//     // VectorFieldCookieTextureCreator.CreateCurveWithHardness(0.5f);
	//     // CreateRampTextureFromAnimationCurve(brushParams.falloffCurve, 32, ref cookie);
	//     computeShader.SetTexture(0, "cookieTexture", cookieTexture != null ? cookieTexture : Texture2D.whiteTexture);
	//
	//     computeShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	// }
}
