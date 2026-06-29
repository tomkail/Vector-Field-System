using System;
using UnityEngine;

// Code-callable polygon vector field generator (GPU). For each grid cell it points toward (or away from) the nearest
// polygon edge, restricted to the chosen side(s) and shaped by a distance falloff. MonoBehaviour-free: give it the
// target render texture, the polygon vertices (polygon-local), the grid->polygon and polygon->field transforms, and
// the parameters; it dispatches PolygonVectorField.compute. The caller owns the vertex ComputeBuffer (passed by ref)
// so its lifetime is explicit, like the render-texture ref the rest of the system uses.
public static class PolygonVectorFieldGenerator {
	// Which sides of the shape get a vector. Enable both for the whole grid.
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

	static ComputeShader polygonVectorFieldComputeShader;
	public static ComputeShader PolygonVectorFieldComputeShader => polygonVectorFieldComputeShader ? polygonVectorFieldComputeShader : (polygonVectorFieldComputeShader = Resources.Load<ComputeShader>("PolygonVectorField"));

	// One instantiated copy shared by every dispatch — dispatches are serial on the main thread and every parameter
	// is set per dispatch, so sharing is safe and avoids per-component shader instantiation.
	static ComputeShader sharedShader;
	static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = UnityEngine.Object.Instantiate(PolygonVectorFieldComputeShader));

	// Must match what's in the compute shader.
	const int threadsPerGroupX = 16;
	const int threadsPerGroupY = 16;
	const int VertexStride = sizeof(float) * 2; // float2

	// Fills `target` (a valid ARGBFloat random-write texture of gridSize) with the polygon field on the GPU.
	// `vertices` are in polygon-local space; `vertexBuffer` is the caller-owned GPU buffer they're uploaded into
	// (created/grown here, released by the caller). `gridToPolygonLocal` maps a grid cell to a polygon-local point;
	// `polygonToFieldVector` rotates/scales a polygon-local vector into the destination field's local space. Fewer
	// than two vertices writes a defined zero field.
	public static void Dispatch(RenderTexture target, ref ComputeBuffer vertexBuffer, Vector2Int gridSize, Vector2[] vertices,
		Matrix4x4 gridToPolygonLocal, Matrix4x4 polygonToFieldVector,
		Sides sides, BoundaryFlip boundaryFlip, float innerFalloff, float outerFalloff, float angle, float magnitude) {
		if (target == null || gridSize.x <= 0 || gridSize.y <= 0) return;

		var shader = SharedShader;
		if (shader == null) return;

		int vertexCount = vertices != null ? vertices.Length : 0;
		EnsureVertexBuffer(ref vertexBuffer, vertexCount);

		int threadGroupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
		int threadGroupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);

		shader.SetTexture(0, "Result", target);
		shader.SetInt("width", gridSize.x);
		shader.SetInt("height", gridSize.y);
		shader.SetBuffer(0, "Vertices", vertexBuffer);

		if (vertexCount < 2) {
			// Nothing to draw — the shader writes a zero field and ignores the rest, so skip the upload/param set.
			shader.SetInt("vertexCount", 0);
			shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
			return;
		}

		vertexBuffer.SetData(vertices, 0, 0, vertexCount);
		shader.SetInt("vertexCount", vertexCount);
		shader.SetMatrix("gridToPolygonLocal", gridToPolygonLocal);
		shader.SetMatrix("polygonToFieldVector", polygonToFieldVector);
		shader.SetInt("wantInside", (sides & Sides.Inside) != 0 ? 1 : 0);
		shader.SetInt("wantOutside", (sides & Sides.Outside) != 0 ? 1 : 0);
		shader.SetInt("boundaryFlip", (int)boundaryFlip);
		shader.SetFloat("innerFalloff", innerFalloff);
		shader.SetFloat("outerFalloff", outerFalloff);
		shader.SetFloat("angle", angle);
		shader.SetFloat("magnitude", magnitude);

		shader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
	}

	// Ensures the buffer exists and holds at least `count` vertices (StructuredBuffer can't be zero-length, so the
	// empty-polygon case still gets a 1-element dummy that the shader never reads).
	static void EnsureVertexBuffer(ref ComputeBuffer vertexBuffer, int count) {
		int needed = Mathf.Max(count, 1);
		if (vertexBuffer == null || vertexBuffer.count < needed) {
			vertexBuffer?.Release();
			vertexBuffer = new ComputeBuffer(needed, VertexStride);
		}
	}
}
