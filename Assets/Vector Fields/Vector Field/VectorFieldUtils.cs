using UnityEngine;
using Unity.Collections;

public static class VectorFieldUtils {

	// Bakes an AnimationCurve into a 1×width RFloat ramp texture (curve sampled across t in [0,1]), reusing `texture`
	// when it already matches the requested width and recreating it otherwise. Used for GPU-side curve lookups
	// (e.g. the group's alignment ramp).
	public static Texture2D CreateRampTextureFromAnimationCurve(AnimationCurve curve, int textureWidth, ref Texture2D texture) {
		if (texture == null || texture.width != textureWidth) {
			if (texture != null) VectorFieldObjectUtils.DestroyAutomatic(texture);
			texture = new Texture2D(textureWidth, 1, TextureFormat.RFloat, false, true) {
				wrapMode = TextureWrapMode.Clamp
			};
		}
		for (int i = 0; i < textureWidth; i++) {
			float t = i / (float)(textureWidth - 1);
			float value = curve.Evaluate(t);
			texture.SetPixel(i, 0, new Color(value, value, value, value));
		}
		texture.Apply();
		return texture;
	}

	// Bakes a Gradient into a 1×width RGBA ramp texture (evaluated across t in [0,1]), reusing `texture` when it already
	// matches the requested width and recreating it otherwise. Used for GPU-side gradient lookups (e.g. the flow
	// visualization's recolor gradient). Authored in sRGB (linear:false) so colors read as designed in the inspector.
	public static Texture2D CreateColorRampTextureFromGradient(Gradient gradient, int textureWidth, ref Texture2D texture) {
		if (texture == null || texture.width != textureWidth) {
			if (texture != null) VectorFieldObjectUtils.DestroyAutomatic(texture);
			texture = new Texture2D(textureWidth, 1, TextureFormat.RGBA32, false, false) {
				wrapMode = TextureWrapMode.Clamp
			};
		}
		for (int i = 0; i < textureWidth; i++) {
			float t = i / (float)(textureWidth - 1);
			texture.SetPixel(i, 0, gradient.Evaluate(t));
		}
		texture.Apply();
		return texture;
	}

	public static Texture2D VectorFieldToTexture(Vector2Map vectorField, float maxComponentReciprocal) {
		var colors = VectorFieldUtils.VectorsToColors(vectorField.values, maxComponentReciprocal);
        
		Texture2D texture = new Texture2D(vectorField.size.x, vectorField.size.y, TextureFormat.RGFloat, false);
		texture.filterMode = FilterMode.Point;
		texture.SetPixels(colors);
		texture.Apply();
        
		return texture;
	}

	public static Texture3D CreateTexture3D(Vector2Map vectorField, float[] amplitudeLut = null) {
		Texture3D texture3D = null;
		FillTexture3D(vectorField, ref texture3D, amplitudeLut);
		return texture3D;
	}

	// Writes the field into a depth-1 Texture3D, reusing the existing texture when its dimensions already match.
	// This avoids destroying + reallocating the Texture3D (and its GPU storage) every time the field updates,
	// which previously happened on every render.
	//
	// amplitudeLut (optional): a precomputed magnitude-response curve, indexed by flow magnitude (0..1) and giving the
	// remapped magnitude. Baking the AnimationCurve into a LUT keeps this hot loop a cheap lookup + lerp rather than a
	// per-voxel AnimationCurve.Evaluate. Pass null (or an identity curve) for the unmodified field.
	public static void FillTexture3D(Vector2Map vectorField, ref Texture3D texture3D, float[] amplitudeLut = null) {
		const int depth = 1;
		int width = vectorField.size.x;
		int height = vectorField.size.y;

		if (texture3D == null || texture3D.width != width || texture3D.height != height || texture3D.depth != depth) {
			if (texture3D != null) {
				if (Application.isPlaying) Object.Destroy(texture3D);
				else Object.DestroyImmediate(texture3D);
			}
			texture3D = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false) {
				filterMode = FilterMode.Bilinear
			};
		}

		Color[] colorArray = new Color[width * height * depth];
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) {
				Vector2 vector = vectorField.GetValueAtGridPoint(x, y);
				if (amplitudeLut != null) vector = ApplyAmplitude(vector, amplitudeLut);
				colorArray[y * width + x] = new Color(vector.x, vector.y, 0f, 1);
			}
		}
		texture3D.SetPixels(colorArray, 0);
		texture3D.Apply();
	}

	// Rescales a vector so its magnitude is remapped through the LUT, preserving direction. The LUT is indexed by the
	// input magnitude clamped to 0..1 (the field's normalized flow strength) and returns the desired output magnitude.
	static Vector2 ApplyAmplitude(Vector2 vector, float[] lut) {
		float mag = vector.magnitude;
		if (mag < 1e-6f) return vector;
		float remapped = SampleLut(lut, Mathf.Clamp01(mag));
		return vector * (remapped / mag);
	}

	// Linearly-interpolated lookup into a 0..1-domain LUT.
	static float SampleLut(float[] lut, float t) {
		float f = t * (lut.Length - 1);
		int i = (int)f;
		if (i >= lut.Length - 1) return lut[lut.Length - 1];
		return Mathf.Lerp(lut[i], lut[i + 1], f - i);
	}
	
	public static Color[] VectorsToColors (Vector2[] vectors, float maxComponentReciprocal) {
		Color[] colors = new Color[vectors.Length];
		for(int i = 0; i < vectors.Length; i++) {
			colors[i] = VectorToColor(vectors[i], maxComponentReciprocal);
		}
		return colors;
	}

	// In-place variant: encodes straight into a caller-owned array so repeated uploads can reuse one buffer instead
	// of allocating a fresh Color[] every call. Writes vectors.Length entries; results must be at least that long.
	public static void VectorsToColors (Vector2[] vectors, float maxComponentReciprocal, Color[] results) {
		for(int i = 0; i < vectors.Length; i++) {
			results[i] = VectorToColor(vectors[i], maxComponentReciprocal);
		}
	}
	
	public static Color VectorToColor (Vector2 vector, float maxComponentReciprocal) {
		return new Color(VectorComponentToColorComponent(vector.x, maxComponentReciprocal), VectorComponentToColorComponent(vector.y, maxComponentReciprocal), 0);
	}

	private static float VectorComponentToColorComponent (float vectorComponent, float maxComponentReciprocal) {
		return ((vectorComponent * maxComponentReciprocal) * 0.5f) + 0.5f;
	}


	public static Vector2[] ColorsToVectors (NativeArray<Color> colors, float maxComponent) {
		Vector2[] vectors = new Vector2[colors.Length];
		for(int i = 0; i < colors.Length; i++) {
			vectors[i] = ColorToVector(colors[i], maxComponent);
		}
		return vectors;
	}

	// In-place variant: decodes straight into a caller-owned array so the readback can reuse its backing
	// Vector2Map instead of allocating a fresh array + map on every frame.
	public static void ColorsToVectors (NativeArray<Color> colors, float maxComponent, Vector2[] results) {
		for(int i = 0; i < colors.Length; i++) {
			results[i] = ColorToVector(colors[i], maxComponent);
		}
	}
	
	public static Vector2[] ColorsToVectors (Color[] colors, float maxComponent) {
		Vector2[] vectors = new Vector2[colors.Length];
		for(int i = 0; i < colors.Length; i++) {
			vectors[i] = ColorToVector(colors[i], maxComponent);
		}
		return vectors;
	}

	public static Vector2 ColorToVector (Color color, float maxComponent) {
		return new Vector2(ColorComponentToVectorComponent(color.r, maxComponent), ColorComponentToVectorComponent(color.g, maxComponent));
	}

	private static float ColorComponentToVectorComponent (float colorComponent, float maxComponent) {
		return (colorComponent - 0.5f) * maxComponent * 2f;
	}
}