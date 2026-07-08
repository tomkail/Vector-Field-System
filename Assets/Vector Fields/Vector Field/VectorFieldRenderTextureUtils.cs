using UnityEngine;

// Shared, code-callable render-texture lifecycle for vector field textures. Vector fields are stored in an
// ARGBFloat render texture with random write enabled (compute shaders write into it) and bilinear filtering
// (sampling between cells). Centralising this logic here gives one definition to reason about — and lets the
// core manage these textures without a MonoBehaviour.
public static class VectorFieldRenderTextureUtils {
	// The descriptor every vector field render texture uses.
	public static RenderTextureDescriptor Descriptor(int width, int height) {
		return new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBFloat, 0) {
			enableRandomWrite = true,
		};
	}

	// Ensures `renderTexture` exists and matches the vector-field descriptor for the given size, (re)creating it in
	// place when it doesn't. Preserves the existing filter mode on recreate; new textures default to Bilinear.
	public static void EnsureValid(ref RenderTexture renderTexture, int width, int height) {
		var descriptor = Descriptor(width, height);
		if (renderTexture == null) {
			renderTexture = new RenderTexture(descriptor) {
				filterMode = FilterMode.Bilinear
			};
		} else if (!DescriptorsMatch(renderTexture.descriptor, descriptor)) {
			var rtFilterMode = renderTexture.filterMode;

			if (RenderTexture.active == renderTexture) RenderTexture.active = null;
			renderTexture.Release();

			renderTexture.descriptor = descriptor;
			renderTexture.Create();
			renderTexture.filterMode = rtFilterMode;
		}
	}

	public static void EnsureValid(ref RenderTexture renderTexture, Vector2Int size) => EnsureValid(ref renderTexture, size.x, size.y);

	// Releases and destroys `renderTexture`, clearing it to null. RenderTextures aren't garbage-collected, so callers
	// that own one must destroy it explicitly (e.g. on disable/teardown) to avoid leaking GPU memory.
	public static void Destroy(ref RenderTexture renderTexture) {
		if (renderTexture == null) return;
		if (RenderTexture.active == renderTexture) RenderTexture.active = null;
		renderTexture.Release();
		if (Application.isPlaying) Object.Destroy(renderTexture);
		else Object.DestroyImmediate(renderTexture);
		renderTexture = null;
	}

	public static bool DescriptorsMatch(RenderTextureDescriptor a, RenderTextureDescriptor b) {
		if (a.depthBufferBits != b.depthBufferBits) return false;
		if (a.width != b.width) return false;
		if (a.height != b.height) return false;
		if (a.depthStencilFormat != b.depthStencilFormat) return false;
		if (a.enableRandomWrite != b.enableRandomWrite) return false;
		if (a.colorFormat != b.colorFormat) return false;
		if (a.dimension != b.dimension) return false;
		return true;
	}
}
