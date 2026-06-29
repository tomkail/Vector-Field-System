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
		if (mode == Mode.Texture)
			return texture != null ? (Texture)texture : Texture2D.whiteTexture;
		if (size.x <= 0 || size.y <= 0)
			return Texture2D.whiteTexture;

		VectorFieldRenderTextureUtils.EnsureValid(ref generated, size);

		var shader = SharedShader;
		int kernel = shader.FindKernel("CSMain");

		if (mode == Mode.Falloff) {
			shader.EnableKeyword("FalloffSoftness");
			shader.DisableKeyword("CurvePoints");
			shader.SetFloat("falloffSoftness", falloffSoftness);
		} else { // Curve
			shader.EnableKeyword("CurvePoints");
			shader.DisableKeyword("FalloffSoftness");

			int resolution = Mathf.Max(size.x, size.y);
			if (curveBuffer == null || curveBuffer.count != resolution) {
				curveBuffer?.Release();
				curveBuffer = new ComputeBuffer(resolution, sizeof(float));
			}
			float[] curveData = new float[resolution];
			for (int i = 0; i < resolution; i++)
				curveData[i] = curve.Evaluate(i / (resolution - 1f));
			curveBuffer.SetData(curveData);

			shader.SetBuffer(kernel, "CurvePoints", curveBuffer);
			shader.SetInt("CurvePointCount", resolution);
		}

		shader.SetInt("textureWidth", size.x);
		shader.SetInt("textureHeight", size.y);
		shader.SetTexture(kernel, "ResultTexture", generated);

		int threadGroupsX = Mathf.CeilToInt(size.x / 8f);
		int threadGroupsY = Mathf.CeilToInt(size.y / 8f);
		shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

		return generated;
	}

	// Multiplies the strength of an already-rendered vector field (the encoded ARGBFloat render texture) by this
	// cookie's mask, in place. No-op when the cookie is None. `size` is the field's grid size. Code-callable: any
	// field producer can mask its output through this, not just the components.
	public void Apply(RenderTexture target, Vector2Int size) {
		if (!Enabled || target == null || size.x <= 0 || size.y <= 0) return;
		var mask = Resolve(size);
		if (mask == null) return;

		var shader = SharedApplyShader;
		int kernel = shader.FindKernel("CSMain");
		shader.SetInt("width", size.x);
		shader.SetInt("height", size.y);
		shader.SetTexture(kernel, "Result", target);
		shader.SetTexture(kernel, "Cookie", mask);

		int threadGroupsX = Mathf.CeilToInt(size.x / 8f);
		int threadGroupsY = Mathf.CeilToInt(size.y / 8f);
		shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
	}

	// Allocation-free content hash for change detection (callers re-render when it changes). Cheaper than serializing
	// to JSON every tick: hashes the curve via the indexer (no Keyframe[] alloc) and the texture by reference.
	public int GetContentHash() {
		var hash = new HashCode();
		hash.Add((int)mode);
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
