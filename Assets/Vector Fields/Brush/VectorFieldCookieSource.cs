using System;
using UnityEngine;
using Object = UnityEngine.Object;

// A cookie/falloff mask for vector field brushes. The common case is just a soft radial falloff, so that's the
// default and needs no texture — pick Falloff and set a softness. Curve gives an authored radial profile, and
// Texture takes an explicit mask for fully hand-painted shapes.
//
// Code-callable: Resolve(size) returns the mask texture for the given size (generating on the GPU for Falloff /
// Curve via the CircularBrushFalloff compute shader). Dispose() releases the generated texture and curve buffer.
[System.Serializable]
public class VectorFieldCookieSource : IDisposable {
	public enum Mode {
		// No mask — the field is left at full strength everywhere. The default, listed first.
		None,
		// Soft radial falloff from the centre, controlled by a single softness value. No texture needed.
		Falloff,
		// Radial falloff profile authored as a curve (distance-from-centre 0..1 -> strength).
		Curve,
		// An explicit mask texture (red channel = strength). For fully hand-painted shapes.
		Texture,
	}

	public Mode mode = Mode.None;

	// Whether this cookie actually masks anything (i.e. anything other than None).
	public bool Enabled => mode != Mode.None;

	// Falloff mode: 0 = hard-edged circle, higher = softer edge.
	[Range(0f, 1f)] public float falloffSoftness = 0.5f;

	// Curve mode: strength as a function of normalized distance from the centre (0 at centre, 1 at the edge).
	public AnimationCurve curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

	// Texture mode: explicit mask (samples the red channel).
	public Texture2D texture;

	// Flip the mask (1-x): full strength where it was empty and vice versa — rings, edge-weighted masks. Baked into
	// the mask Resolve() returns (all modes), so every consumer sees the effective mask.
	public bool invert;

	static ComputeShader circularBrushFalloffShader;
	static ComputeShader CircularBrushFalloffShader => circularBrushFalloffShader ? circularBrushFalloffShader : (circularBrushFalloffShader = Resources.Load<ComputeShader>("CircularBrushFalloff"));

	// One instantiated copy shared by every source — dispatches are serial on the main thread and every parameter is
	// set per dispatch, so sharing is safe and avoids per-source shader instantiation.
	static ComputeShader sharedShader;
	static ComputeShader SharedShader => sharedShader ? sharedShader : (sharedShader = Object.Instantiate(CircularBrushFalloffShader));

	static ComputeShader applyShader;
	static ComputeShader ApplyShader => applyShader ? applyShader : (applyShader = Resources.Load<ComputeShader>("ApplyVectorFieldCookie"));
	static ComputeShader sharedApplyShader;
	static ComputeShader SharedApplyShader => sharedApplyShader ? sharedApplyShader : (sharedApplyShader = Object.Instantiate(ApplyShader));

	// Generated mask for Falloff / Curve modes (Texture mode uses `texture` directly). Not serialized — rebuilt on
	// demand, like the field render textures.
	[NonSerialized] RenderTexture generated;
	[NonSerialized] ComputeBuffer curveBuffer;

	// Returns the mask texture for the given size. Regenerates on every call for Falloff / Curve (callers render only
	// when something changed, so there's no need to track dirtiness here); returns the assigned texture for Texture
	// mode (or a solid white mask when none is assigned, i.e. no masking).
	public Texture Resolve(Vector2Int size) {
		if (mode == Mode.None)
			return null;
		// Texture mode passes the assigned texture straight through — except when inverted, where the compute below
		// bakes 1-x into a generated copy so Resolve always returns the effective mask. An unassigned texture means
		// "no masking" (solid white), which inversion deliberately leaves alone.
		if (mode == Mode.Texture && (!invert || texture == null))
			return texture != null ? (Texture)texture : Texture2D.whiteTexture;
		if (size.x <= 0 || size.y <= 0)
			return Texture2D.whiteTexture;

		VectorFieldRenderTextureUtils.EnsureValid(ref generated, size);

		var shader = SharedShader;
		int kernel = shader.FindKernel("CSMain");

		// The kernel branches on maskMode rather than keyword variants (variant switching on the shared shader was
		// unreliable). CurvePoints is referenced unconditionally, so it must always be bound — the Falloff branch just
		// never reads it. Keep the buffer sized to the mask so Curve mode has enough sample points.
		int resolution = Mathf.Max(size.x, size.y, 2);
		if (curveBuffer == null || curveBuffer.count != resolution) {
			curveBuffer?.Release();
			curveBuffer = new ComputeBuffer(resolution, sizeof(float));
		}
		if (mode == Mode.Curve) {
			float[] curveData = new float[resolution];
			for (int i = 0; i < resolution; i++)
				curveData[i] = curve.Evaluate(i / (resolution - 1f));
			curveBuffer.SetData(curveData);
		}

		shader.SetInt("maskMode", mode == Mode.Texture ? 2 : mode == Mode.Curve ? 1 : 0);
		shader.SetInt("invert", invert ? 1 : 0);
		shader.SetFloat("falloffSoftness", falloffSoftness);
		shader.SetBuffer(kernel, "CurvePoints", curveBuffer);
		shader.SetInt("CurvePointCount", resolution);
		// Like CurvePoints, SourceTexture is referenced unconditionally so it must always be bound; only the
		// Texture branch (maskMode 2) ever samples it.
		shader.SetTexture(kernel, "SourceTexture", mode == Mode.Texture ? (Texture)texture : Texture2D.whiteTexture);

		shader.SetInt("textureWidth", size.x);
		shader.SetInt("textureHeight", size.y);
		shader.SetTexture(kernel, "ResultTexture", generated);

		int threadGroupsX = Mathf.CeilToInt(size.x / 8f);
		int threadGroupsY = Mathf.CeilToInt(size.y / 8f);
		shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

		return generated;
	}

	// Applies a field's OUTPUT transform to an already-rendered vector field (the encoded ARGBFloat render texture),
	// in place: multiplies its strength by `strength` (the field's magnitude) and by this cookie's mask. `size` is the
	// field's full grid size. Optionally limited to a sub-rect via `region` (in grid coords, origin bottom-left) so a
	// caller that rewrote only part of the texture re-transforms only those texels; null transforms the whole grid.
	// No-op when there's nothing to do (strength ≈ 1 and the cookie is None). Code-callable: any field producer can run
	// its output through this, not just the components.
	public void Apply(RenderTexture target, Vector2Int size, float strength = 1f, RectInt? region = null) {
		if (target == null || size.x <= 0 || size.y <= 0) return;
		if (!Enabled && Mathf.Approximately(strength, 1f)) return;   // nothing to scale and nothing to mask

		// Enabled cookies contribute their mask; magnitude-only passes use a solid-white mask (multiply by 1).
		var mask = Enabled ? Resolve(size) : Texture2D.whiteTexture;
		if (mask == null) mask = Texture2D.whiteTexture;

		// Clamp the region to the grid; a whole-grid pass covers [0,0]..[size].
		int x0 = 0, y0 = 0, w = size.x, h = size.y;
		if (region.HasValue) {
			var r = region.Value;
			x0 = Mathf.Clamp(r.xMin, 0, size.x);
			y0 = Mathf.Clamp(r.yMin, 0, size.y);
			w = Mathf.Clamp(r.xMax, 0, size.x) - x0;
			h = Mathf.Clamp(r.yMax, 0, size.y) - y0;
		}
		if (w <= 0 || h <= 0) return;

		var shader = SharedApplyShader;
		int kernel = shader.FindKernel("CSMain");
		shader.SetInt("width", size.x);
		shader.SetInt("height", size.y);
		shader.SetInt("offsetX", x0);
		shader.SetInt("offsetY", y0);
		shader.SetInt("regionWidth", w);
		shader.SetInt("regionHeight", h);
		shader.SetFloat("strength", strength);
		shader.SetTexture(kernel, "Result", target);
		shader.SetTexture(kernel, "Cookie", mask);

		int threadGroupsX = Mathf.CeilToInt(w / 8f);
		int threadGroupsY = Mathf.CeilToInt(h / 8f);
		shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
	}

	// Allocation-free content hash for change detection (callers re-render when it changes). Cheaper than serializing
	// to JSON every tick: hashes the curve via the indexer (no Keyframe[] alloc) and the texture by reference.
	public int GetContentHash() {
		var hash = new HashCode();
		hash.Add((int)mode);
		hash.Add(invert);
		hash.Add(falloffSoftness);
		if (curve != null) {
			hash.Add(curve.length);
			for (int i = 0; i < curve.length; i++) {
				var key = curve[i];
				hash.Add(key.time);
				hash.Add(key.value);
			}
		}
		if (texture != null) hash.Add(texture.GetEntityId());
		return hash.ToHashCode();
	}

	public void Dispose() {
		curveBuffer?.Release();
		curveBuffer = null;
		if (generated != null) {
			if (RenderTexture.active == generated) RenderTexture.active = null;
			generated.Release();
			if (Application.isPlaying) Object.Destroy(generated);
			else Object.DestroyImmediate(generated);
			generated = null;
		}
	}
}
