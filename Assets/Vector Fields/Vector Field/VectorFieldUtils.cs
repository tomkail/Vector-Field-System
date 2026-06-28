using UnityEngine;
using Unity.Collections;

public static class VectorFieldUtils {

	// public static Color[] Vector2ArrayToColorArray(Vector2[] floatArray, float magnitude) {
	// 	var magnitudeReciprocal = 1f/magnitude;
	// 	Color[] colorArray = new Color[floatArray.Length];
	// 	for(int i = 0; i < floatArray.Length; i++){
	// 		var degrees = Vector2X.Degrees(floatArray[i]);
	// 		var lightness = floatArray[i].magnitude * magnitudeReciprocal * 0.5f;
	// 		colorArray[i] = new HSLColor(degrees, 1, lightness);
	// 	}
	// 	return colorArray;
	// }



	public static Texture2D VectorFieldToTexture(Vector2Map vectorField, float maxComponentReciprocal) {
		var colors = VectorFieldUtils.VectorsToColors(vectorField.values, maxComponentReciprocal);
        
		Texture2D texture = new Texture2D(vectorField.size.x, vectorField.size.y, TextureFormat.RGFloat, false);
		texture.filterMode = FilterMode.Point;
		texture.SetPixels(colors);
		texture.Apply();
        
		return texture;
	}

	public static Texture3D CreateTexture3D(Vector2Map vectorField) {
		Texture3D texture3D = null;
		FillTexture3D(vectorField, ref texture3D);
		return texture3D;
	}

	// Writes the field into a depth-1 Texture3D, reusing the existing texture when its dimensions already match.
	// This avoids destroying + reallocating the Texture3D (and its GPU storage) every time the field updates,
	// which previously happened on every render.
	public static void FillTexture3D(Vector2Map vectorField, ref Texture3D texture3D) {
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
				colorArray[y * width + x] = new Color(vector.x, vector.y, 0f, 1);
			}
		}
		texture3D.SetPixels(colorArray, 0);
		texture3D.Apply();
	}
	
	public static Color[] VectorsToColors (Vector2[] vectors, float maxComponentReciprocal) {
		Color[] colors = new Color[vectors.Length];
		for(int i = 0; i < vectors.Length; i++) {
			colors[i] = VectorToColor(vectors[i], maxComponentReciprocal);
		}
		return colors;
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