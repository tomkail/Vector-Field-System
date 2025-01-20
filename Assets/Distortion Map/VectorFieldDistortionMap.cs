using System;
using UnityEngine;

[ExecuteAlways]
public class VectorFieldDistortionMap : MonoBehaviour {
	public VectorFieldComponent vectorField;
	public Material distortionMaterial;
	void Update() {
		if (distortionMaterial != null && vectorField != null && vectorField.renderTexture != null)
			distortionMaterial.SetTexture("_NormalMap", vectorField.renderTexture);
	}
}
