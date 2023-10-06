using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(ParticleSystemForceField))]
public class ParticleSystemVectorField : MonoBehaviour {
    [SerializeField]VectorFieldComponent vectorFieldComponent;
    ParticleSystemForceField forceField => GetComponent<ParticleSystemForceField>();
    Texture3D texture3D;

    void OnEnable() {
        Refresh();
        vectorFieldComponent.OnRender += Refresh;
    }
    
    void OnDisable() {
        vectorFieldComponent.OnRender -= Refresh;
    }
    
    void Refresh() {
        if(texture3D != null) ObjectX.DestroyAutomatic(texture3D);
        if (vectorFieldComponent == null) return;
        texture3D = VectorFieldUtils.CreateTexture3D(vectorFieldComponent.vectorField);
        // var pixerls = texture3D.GetPixels();

        forceField.shape = ParticleSystemForceFieldShape.Box;
        forceField.startRange = 0f;
        forceField.endRange = 0.5f;
        
        forceField.directionX = 0f;
        forceField.directionY = 0f;
        forceField.directionZ = 0f;
        
        forceField.gravity = 0f;
        forceField.gravityFocus = 0f;
        
        forceField.rotationAttraction = 0f;
        forceField.rotationRandomness = Vector2.zero;
        forceField.rotationSpeed = 0f;
        
        forceField.drag = 0f;
        
        forceField.vectorField = texture3D;
        forceField.vectorFieldSpeed = vectorFieldComponent.magnitude;
        
        // transform.position = vectorFieldComponent.transform.position;
        // transform.rotation = vectorFieldComponent.transform.rotation;
        // transform.localScale = vectorFieldComponent.transform.localScale;
    }
}
