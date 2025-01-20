using UnityEngine;

[System.Serializable]
public class VectorFieldCookieTextureCreatorSettings {
	public Vector2Int gridSize;

	public GenerationMode generationMode;
	public enum GenerationMode {
		Exponent,
		AnimationCurve
	}
	public float falloffSoftness = 0;
	public AnimationCurve animationCurve;
}
