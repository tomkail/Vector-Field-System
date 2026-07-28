using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VectorFields {
	// Code-callable max-magnitude query for an encoded vector field render texture (color = vector * 0.5 + 0.5, the
	// project-wide convention). A compute kernel reduces each 16x16 block to one squared max, so only a tiny per-group
	// buffer crosses back to the CPU — never the whole field. The readback is asynchronous when the platform supports it
	// (no pipeline stall; the callback fires a frame or two later), synchronous otherwise.
	//
	// Because the readback is enqueued immediately after the reduction dispatch and all dispatches are serial on the
	// main thread, one shared GroupMaxes buffer serves every caller: a later dispatch can't overwrite the buffer before
	// an earlier readback has captured it in the command stream.
	public static class VectorFieldMaxMagnitude {
		static ComputeShader maxMagnitudeComputeShader;
		static ComputeShader MaxMagnitudeComputeShader => maxMagnitudeComputeShader ? maxMagnitudeComputeShader : (maxMagnitudeComputeShader = Resources.Load<ComputeShader>("VectorFieldMaxMagnitude"));

		// One instantiated copy shared by every dispatch — same rationale as NoiseVectorField.SharedShader.
		static ComputeShader sharedShader;
		static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = UnityEngine.Object.Instantiate(MaxMagnitudeComputeShader));

		// Must match what's in the compute shader
		const int threadsPerGroupX = 16;
		const int threadsPerGroupY = 16;

		// Grown as needed and reused across every request; released on quit / assembly reload so it never leaks a
		// warning. Sized in floats (one squared max per thread group).
		static ComputeBuffer groupMaxes;
		static bool releaseHooked;

		// Computes the max vector length in `source` and invokes onComplete with it. Skipped entirely (no callback) if
		// the inputs are invalid or the readback errors — callers keep whatever value they had.
		public static void Request(RenderTexture source, Vector2Int gridSize, Action<float> onComplete) {
			if (source == null || gridSize.x <= 0 || gridSize.y <= 0 || onComplete == null) return;
			var shader = SharedShader;
			if (shader == null) return;

			int groupsX = Mathf.CeilToInt((float)gridSize.x / threadsPerGroupX);
			int groupsY = Mathf.CeilToInt((float)gridSize.y / threadsPerGroupY);
			int groupCount = groupsX * groupsY;
			EnsureBuffer(groupCount);

			shader.SetTexture(0, "Source", source);
			shader.SetInt("width", gridSize.x);
			shader.SetInt("height", gridSize.y);
			shader.SetInt("groupsX", groupsX);
			shader.SetBuffer(0, "GroupMaxes", groupMaxes);
			shader.Dispatch(0, groupsX, groupsY, 1);

			if (SystemInfo.supportsAsyncGPUReadback) {
				// Read back only the groups this dispatch wrote (the buffer may be larger from a previous, bigger field).
				AsyncGPUReadback.Request(groupMaxes, groupCount * sizeof(float), 0, request => {
					if (request.hasError) return;
					onComplete(MaxOf(request.GetData<float>(), groupCount));
				});
			} else {
				// Fallback: synchronous readback (stalls, but only ever runs when the field actually re-rendered).
				var results = new float[groupCount];
				groupMaxes.GetData(results, 0, 0, groupCount);
				float max = 0;
				for (int i = 0; i < groupCount; i++) max = Mathf.Max(max, results[i]);
				onComplete(Mathf.Sqrt(max));
			}
		}

		static float MaxOf(Unity.Collections.NativeArray<float> squaredMaxes, int count) {
			float max = 0;
			count = Mathf.Min(count, squaredMaxes.Length);
			for (int i = 0; i < count; i++) max = Mathf.Max(max, squaredMaxes[i]);
			return Mathf.Sqrt(max);
		}

		static void EnsureBuffer(int groupCount) {
			if (groupMaxes != null && groupMaxes.count >= groupCount) return;
			groupMaxes?.Release();
			groupMaxes = new ComputeBuffer(groupCount, sizeof(float));
			if (!releaseHooked) {
				releaseHooked = true;
				Application.quitting += ReleaseBuffer;
	#if UNITY_EDITOR
				UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffer;
	#endif
			}
		}

		static void ReleaseBuffer() {
			groupMaxes?.Release();
			groupMaxes = null;
		}
	}
}
