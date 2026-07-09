using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Code-callable spline vector field generator (GPU). Its geometry is a flat list of attribute-carrying samples in the
// destination field's local plane space, arranged as segment *pairs* (segment i = samples[2i], samples[2i+1]) — so any
// number of splines (or any polyline source) feed the same dispatch, and consecutive segments of one path share
// endpoint samples for continuous attribute interpolation. Each cell finds its nearest point on the polyline and emits
// either the flow (tangent) direction there or a fixed direction, rotated by the interpolated per-sample angle and
// faded by the interpolated per-sample falloff distance. MonoBehaviour-free: the caller owns the sample ComputeBuffer
// (passed by ref) so its lifetime is explicit, like the render-texture ref the rest of the system uses.
public static class SplineVectorFieldGenerator {
	public enum DirectionMode {
		// Vectors follow the path: each cell uses the tangent at its nearest point on the spline.
		Flow = 0,
		// Every cell uses the same fixed direction (still rotated and faded per-point).
		Fixed = 1,
	}

	// A point sampled along a spline, in field-local plane space. rotation is in degrees around the plane normal;
	// falloff is the distance (field-local units) over which strength fades from full (on the path) to zero at this
	// point (<= 0 = no falloff, constant strength). Layout must match SplineSample in SplineVectorField.compute.
	[StructLayout(LayoutKind.Sequential)]
	public struct Sample {
		public Vector2 position;
		public Vector2 tangent;
		public float rotation;
		public float falloff;
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
	const int SampleStride = sizeof(float) * 6; // float2 position + float2 tangent + float rotation + float falloff

	// Fills `target` (a valid ARGBFloat random-write texture of gridSize) with the spline field on the GPU. `samples`
	// are segment pairs in field-local plane space (segment i = samples[2i], samples[2i+1]); `sampleBuffer` is the
	// caller-owned GPU buffer they're uploaded into (created/grown here, released by the caller). `gridToPlane` maps a
	// grid cell to a field-local plane point. `fixedDirection` is only read in Fixed mode and should be normalized.
	// Fewer than one segment writes a defined zero field.
	public static void Dispatch(RenderTexture target, ref ComputeBuffer sampleBuffer, Vector2Int gridSize,
		List<Sample> samples, Matrix4x4 gridToPlane,
		DirectionMode directionMode, Vector2 fixedDirection, float magnitude) {
		if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

		var shader = SharedShader;
		if (shader == null) return;

		// Only whole segments are drawn; a trailing unpaired sample (shouldn't happen) is ignored.
		int sampleCount = samples != null ? (samples.Count & ~1) : 0;
		int segmentCount = sampleCount / 2;
		EnsureSampleBuffer(ref sampleBuffer, sampleCount);

		int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
		int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);

		shader.SetTexture(0, "Result", target);
		shader.SetInt("width", gridSize.x);
		shader.SetInt("height", gridSize.y);
		shader.SetBuffer(0, "Samples", sampleBuffer);

		if (segmentCount < 1) {
			// Nothing to draw — the shader writes a zero field and ignores the rest, so skip the upload/param set.
			shader.SetInt("segmentCount", 0);
			shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
			return;
		}

		sampleBuffer.SetData(samples, 0, 0, sampleCount);
		shader.SetInt("segmentCount", segmentCount);
		shader.SetMatrix("gridToPlane", gridToPlane);
		shader.SetInt("directionMode", (int)directionMode);
		shader.SetVector("fixedDirection", fixedDirection);
		shader.SetFloat("magnitude", magnitude);

		shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	}

	// Ensures the buffer exists and holds at least `count` samples (StructuredBuffer can't be zero-length, so the
	// empty case still gets a 1-element dummy the shader never reads).
	static void EnsureSampleBuffer(ref ComputeBuffer sampleBuffer, int count) {
		int needed = Mathf.Max(count, 1);
		if (sampleBuffer == null || sampleBuffer.count < needed) {
			sampleBuffer?.Release();
			sampleBuffer = new ComputeBuffer(needed, SampleStride);
		}
	}
}
