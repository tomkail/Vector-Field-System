using System;
using System.Collections.Generic;
using UnityEngine;

namespace VectorFields {
	// Code-callable mesh/silhouette vector field generator (GPU). Its geometry is a flat list of 2D line segments — so 3D
	// mesh cross-sections and 2D sprite/collider silhouettes (extracted by MeshVectorFieldExtractors) both feed the same
	// dispatch. Segments are given in the destination field's local plane space (endpoint pairs: seg i = endpoints[2i],
	// endpoints[2i+1]); gridToPlane maps a grid cell into that same space. MonoBehaviour-free: the caller owns the segment
	// ComputeBuffer (passed by ref) so its lifetime is explicit, like the render-texture ref the rest of the system uses.
	public static class MeshVectorFieldGenerator {
		// Which side(s) of the boundary get a vector. Enable both for the whole grid. (Owned here as the generator for
		// boundary-distance fields; the component API exposes these directly.)
		[Flags]
		public enum Sides {
			None = 0,
			Inside = 1 << 0,
			Outside = 1 << 1,
		}

		public enum BoundaryFlip {
			None,
			FlipInside,
			FlipOutside,
		}

		static ComputeShader meshVectorFieldComputeShader;
		public static ComputeShader MeshVectorFieldComputeShader => meshVectorFieldComputeShader ? meshVectorFieldComputeShader : (meshVectorFieldComputeShader = Resources.Load<ComputeShader>("MeshVectorField"));

		// One instantiated copy shared by every dispatch — dispatches are serial on the main thread and every parameter is
		// set per dispatch, so sharing is safe and avoids per-component shader instantiation.
		static ComputeShader sharedShader;
		static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = UnityEngine.Object.Instantiate(MeshVectorFieldComputeShader));

		// Must match what's in the compute shader.
		const int threadsPerGroupX = 16;
		const int threadsPerGroupY = 16;
		const int EndpointStride = sizeof(float) * 2; // float2

		// Fills `target` (a valid ARGBFloat random-write texture of gridSize) with the segment field on the GPU.
		// `endpoints` are endpoint pairs in field-local plane space (seg i = endpoints[2i], endpoints[2i+1]); `segmentBuffer`
		// is the caller-owned GPU buffer they're uploaded into (created/grown here, released by the caller). `gridToPlane`
		// maps a grid cell to a field-local plane point. `hasInsideTest` should be false when the geometry contains open
		// (non-closed) contours, where the crossing parity is meaningless. Fewer than one segment writes a defined zero field.
		public static void Dispatch(RenderTexture target, ref ComputeBuffer segmentBuffer, Vector2Int gridSize,
			List<Vector2> endpoints, Matrix4x4 gridToPlane,
			Sides sides, BoundaryFlip boundaryFlip,
			float innerFalloff, float outerFalloff, float angle, float magnitude, bool hasInsideTest) {
			if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

			var shader = SharedShader;
			if (shader == null) return;

			// Only whole segments are drawn; a trailing unpaired endpoint (shouldn't happen) is ignored.
			int endpointCount = endpoints != null ? (endpoints.Count & ~1) : 0;
			int segmentCount = endpointCount / 2;
			EnsureSegmentBuffer(ref segmentBuffer, endpointCount);

			int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
			int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);

			shader.SetTexture(0, "Result", target);
			shader.SetInt("width", gridSize.x);
			shader.SetInt("height", gridSize.y);
			shader.SetBuffer(0, "Segments", segmentBuffer);

			if (segmentCount < 1) {
				// Nothing to draw — the shader writes a zero field and ignores the rest, so skip the upload/param set.
				shader.SetInt("segmentCount", 0);
				shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
				return;
			}

			segmentBuffer.SetData(endpoints, 0, 0, endpointCount);
			shader.SetInt("segmentCount", segmentCount);
			shader.SetMatrix("gridToPlane", gridToPlane);
			shader.SetInt("wantInside", (sides & Sides.Inside) != 0 ? 1 : 0);
			shader.SetInt("wantOutside", (sides & Sides.Outside) != 0 ? 1 : 0);
			shader.SetInt("boundaryFlip", (int)boundaryFlip);
			shader.SetInt("hasInsideTest", hasInsideTest ? 1 : 0);
			shader.SetFloat("innerFalloff", innerFalloff);
			shader.SetFloat("outerFalloff", outerFalloff);
			shader.SetFloat("angle", angle);
			shader.SetFloat("magnitude", magnitude);

			shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
		}

		// Ensures the buffer exists and holds at least `count` endpoints (StructuredBuffer can't be zero-length, so the
		// empty case still gets a 1-element dummy the shader never reads).
		static void EnsureSegmentBuffer(ref ComputeBuffer segmentBuffer, int count) {
			int needed = Mathf.Max(count, 1);
			if (segmentBuffer == null || segmentBuffer.count < needed) {
				segmentBuffer?.Release();
				segmentBuffer = new ComputeBuffer(needed, EndpointStride);
			}
		}
	}
}
