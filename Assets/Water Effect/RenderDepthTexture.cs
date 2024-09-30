using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class RenderDepthTexture : MonoBehaviour {
    public DepthTextureMode depthTextureMode;

    void OnEnable() {
        GetComponent<Camera>().depthTextureMode = depthTextureMode;
    }
}