using UnityEngine;

[ExecuteAlways]
public class VectorFieldShaderVisualizer : MonoBehaviour {
	public VectorFieldComponent vectorField;
	public Material material;
	void Update() {
		if (material != null && vectorField != null && vectorField.renderTexture != null)
			material.SetTexture("_VectorField", vectorField.renderTexture);
	}
}
