using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Code-callable spline vector field generator (GPU). Its geometry is a flat list of attribute-carrying samples in the
// destination field's local plane space, arranged as segment *pairs* (segment i = samples[2i], samples[2i+1]) — so any
// number of splines (or any polyline source) feed the same dispatch, and consecutive segments of one path share
// endpoint samples for continuous attribute interpolation. Each cell finds its nearest point on the polyline and emits
// either the flow (tangent) direction there or a fixed direction. The cell's distance from the path, normalized
// against the interpolated per-sample width, drives everything across the path: strength comes from the baked falloff
// curve sampled at that normalized distance, and the interpolated per-sample edge rotation is scaled by the *signed*
// normalized distance (0 on the path, ±1 at the edges) and added to the per-sample base rotation. MonoBehaviour-free:
// the caller owns the sample and curve ComputeBuffers (passed by ref) so their lifetime is explicit, like the
// render-texture ref the rest of the system uses.
public static class SplineVectorFieldGenerator {
	public enum DirectionMode {
		// Vectors follow the path: each cell uses the tangent at its nearest point on the spline.
		Flow = 0,
		// Every cell uses the same fixed direction (still rotated and faded per-point).
		Fixed = 1,
	}

	// A point sampled along a spline, in field-local plane space. rotation is the base rotation in degrees around the
	// plane normal; edgeRotation (degrees) is the extra rotation at signed normalized distance ±1 from the path (its
	// sign follows the side: + on the tangent's left, - on its right); width is the distance (field-local units) the
	// field spans either side of the path at this point — distance is normalized against it for the falloff curve and
	// the edge rotation (<= 0 = no width: constant curve(0) strength, no edge rotation). Layout must match
	// SplineSample in SplineVectorField.compute.
	[StructLayout(LayoutKind.Sequential)]
	public struct Sample {
		public Vector2 position;
		public Vector2 tangent;
		public float rotation;
		public float edgeRotation;
		public float width;
	}

	static ComputeShader splineVectorFieldComputeShader;
	public static ComputeShader SplineVectorFieldComputeShader => splineVectorFieldComputeShader ? splineVectorFieldComputeShader : (splineVectorFieldComputeShader = Resources.Load<ComputeShader>("SplineVectorField"));

	// One instantiated copy shared by every dispatch — dispatches are serial on the main thread and every parameter is
	// set per dispatch, so sharing is safe and avoids per-component shader instantiation.
	static ComputeShader sharedShader;
	static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = Object.Instantiate(SplineVectorFieldComputeShader));

	// Must match what's in the compute shader.
	const int threadsPerGroupX = 16;
	const int threadsPerGroupY = 16;
	const int SampleStride = sizeof(float) * 7; // float2 position + float2 tangent + float rotation + float edgeRotation + float width

	// Fills `target` (a valid ARGBFloat random-write texture of gridSize) with the spline field on the GPU. `samples`
	// are segment pairs in field-local plane space (segment i = samples[2i], samples[2i+1]); `falloffCurve` is the
	// strength curve baked to evenly-spaced samples over normalized distance 0..1 (null/empty = constant 1).
	// `sampleBuffer`/`falloffCurveBuffer` are the caller-owned GPU buffers they're uploaded into (created/grown here,
	// released by the caller). `gridToPlane` maps a grid cell to a field-local plane point. `fixedDirection` is only
	// read in Fixed mode and should be normalized. Fewer than one segment writes a defined zero field.
	public static void Dispatch(RenderTexture target, ref ComputeBuffer sampleBuffer, ref ComputeBuffer falloffCurveBuffer,
		Vector2Int gridSize, List<Sample> samples, float[] falloffCurve, Matrix4x4 gridToPlane,
		DirectionMode directionMode, Vector2 fixedDirection, float magnitude) {
		if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

		var shader = SharedShader;
		if (shader == null) return;

		// Only whole segments are drawn; a trailing unpaired sample (shouldn't happen) is ignored.
		int sampleCount = samples != null ? (samples.Count & ~1) : 0;
		int segmentCount = sampleCount / 2;
		EnsureBuffer(ref sampleBuffer, sampleCount, SampleStride);

		int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
		int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);

		shader.SetTexture(0, "Result", target);
		shader.SetInt("width", gridSize.x);
		shader.SetInt("height", gridSize.y);
		shader.SetBuffer(0, "Samples", sampleBuffer);

		int curveCount = falloffCurve != null ? falloffCurve.Length : 0;
		EnsureBuffer(ref falloffCurveBuffer, curveCount, sizeof(float));
		shader.SetBuffer(0, "FalloffCurve", falloffCurveBuffer);

		if (segmentCount < 1) {
			// Nothing to draw — the shader writes a zero field and ignores the rest, so skip the upload/param set.
			shader.SetInt("segmentCount", 0);
			shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
			return;
		}

		sampleBuffer.SetData(samples, 0, 0, sampleCount);
		if (curveCount > 0) falloffCurveBuffer.SetData(falloffCurve, 0, 0, curveCount);
		shader.SetInt("falloffCurveCount", curveCount);
		shader.SetInt("segmentCount", segmentCount);
		shader.SetMatrix("gridToPlane", gridToPlane);
		shader.SetInt("directionMode", (int)directionMode);
		shader.SetVector("fixedDirection", fixedDirection);
		shader.SetFloat("magnitude", magnitude);

		shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	}

	// Ensures the buffer exists and holds at least `count` elements of `stride` bytes (StructuredBuffer can't be
	// zero-length, so the empty case still gets a 1-element dummy the shader never reads).
	static void EnsureBuffer(ref ComputeBuffer buffer, int count, int stride) {
		int needed = Mathf.Max(count, 1);
		if (buffer == null || buffer.count < needed || buffer.stride != stride) {
			buffer?.Release();
			buffer = new ComputeBuffer(needed, stride);
		}
	}
}
