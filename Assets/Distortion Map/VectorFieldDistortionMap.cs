using System;
using UnityEngine;

[ExecuteAlways]
public class VectorFieldDistortionMap : MonoBehaviour
{
    public VectorFieldComponent vectorField;
    public Material distortionMaterial;
    void Update()
    {
        distortionMaterial.SetTexture("_NormalMap", vectorField.vectorFieldTexture);
    }
}