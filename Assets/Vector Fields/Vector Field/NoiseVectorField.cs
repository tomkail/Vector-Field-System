using UnityEngine;

// Code-callable noise vector field generator. Writes a fractal-noise flow field into an ARGBFloat render texture on
// the GPU, with no dependency on a MonoBehaviour or GridRenderer: give it the target, the grid size, the matrix that
// maps a grid cell into the space the noise is sampled in, and the sampler parameters.
public static class NoiseVectorField {
	static ComputeShader noiseVectorFieldComputeShader;
	public static ComputeShader NoiseVectorFieldComputeShader => noiseVectorFieldComputeShader ? noiseVectorFieldComputeShader : (noiseVectorFieldComputeShader = Resources.Load<ComputeShader>("NoiseVectorField"));

	// One instantiated copy shared by every dispatch — dispatches are serial on the main thread and every parameter
	// is set per dispatch, so sharing is safe and avoids per-component shader instantiation.
	static ComputeShader sharedShader;
	static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = Object.Instantiate(NoiseVectorFieldComputeShader));

	// Must match what's in the compute shader
	const int threadsPerGroupX = 16;
	const int threadsPerGroupY = 16;

	// `target` must be a valid ARGBFloat random-write texture sized to gridSize. `gridToSampleMatrix` maps a grid
	// cell coordinate into the space the noise is sampled in (world space, or an offset local space).
	public static void Dispatch(RenderTexture target, Vector2Int gridSize, Matrix4x4 gridToSampleMatrix, NoiseSamplerProperties noise, float vortexAngle, float magnitude) {
		if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

		var shader = SharedShader;
		shader.SetTexture(0, "Result", target);
		shader.SetInt("width", gridSize.x);
		shader.SetInt("height", gridSize.y);
		shader.SetFloat("magnitude", magnitude);
		shader.SetMatrix("gridToWorldMatrix", gridToSampleMatrix);
		shader.SetFloat("frequency", noise.frequency);
		shader.SetFloat("persistence", noise.persistence);
		shader.SetFloat("lacunarity", noise.lacunarity);
		shader.SetFloat("numOctaves", noise.octaves);
		shader.SetFloat("vortexAngle", vortexAngle);

		int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
		int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);
		shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	}
}
