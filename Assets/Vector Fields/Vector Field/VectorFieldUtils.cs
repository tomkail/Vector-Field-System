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
		var depth = 1;
		Color[] colorArray = new Color[vectorField.size.x * vectorField.size.y * depth];
		for (int z = 0; z < depth; z++) {
			for (int y = 0; y < vectorField.size.y; y++) {
				for (int x = 0; x < vectorField.size.x; x++) {
					Vector2 vector = vectorField.GetValueAtGridPoint(x, y);
					Color color = new Color(vector.x, vector.y, 0f, 1); // Using 0.5f as neutral value for z
					int index = z * (vectorField.size.x * vectorField.size.y) + y * vectorField.size.x + x;
					colorArray[index] = color;
				}
			}
		}

		var texture3D = new Texture3D(vectorField.size.x, vectorField.size.y, depth, TextureFormat.RGBAHalf, false);
		texture3D.filterMode = FilterMode.Bilinear;
		texture3D.SetPixels(colorArray, 0);
		texture3D.Apply();
		return texture3D;
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