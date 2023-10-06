using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class VectorFieldCookie {
    public Texture2D cookieTexture;
    public HeightMap cookieMap;
}

[ExecuteAlways, RequireComponent(typeof(GridRenderer))]
public abstract class VectorFieldComponent : MonoBehaviour {
    protected GroupVectorFieldComponent group => this.GetComponentsX(ComponentX.ComponentSearchParams<GroupVectorFieldComponent>.AllAncestorsExcludingSelf(true)).FirstOrDefault();
    
    public GridRenderer gridRenderer { get; private set;  }
    public Vector3 planeNormal => transform.forward;

    [Space]
    public Vector2Map vectorField;
	
    public float magnitude = 1;
    // public VectorFieldCookie cookie;
    
    public delegate void OnUpdateDelegate ();
    public event OnUpdateDelegate OnRender;

    SerializableTransform lastTransform;

    protected virtual void OnEnable () {
        gridRenderer = GetComponent<GridRenderer>();
        SetDirty();
        Update();
    }
    
    protected void OnDisable () {
        group?.Render();
    }

    protected virtual void OnValidate () {
        gridRenderer = GetComponent<GridRenderer>();
        gridRenderer.modeModule = ScriptableObject.CreateInstance<GridRendererManhattanModeModule>();
        gridRenderer.scaleWithGridSize = false;
        if (gridRenderer.gridSize == Point.zero) gridRenderer.gridSize = new Point(64,64);
        if (gridRenderer.gridSize.x < 1) gridRenderer.gridSize = new Point(1, gridRenderer.gridSize.y);
        if (gridRenderer.gridSize.y < 1) gridRenderer.gridSize = new Point(gridRenderer.gridSize.x, 1);
        // gridRenderer.showGizmos = true;
        lastTransform = new SerializableTransform(transform);
        if (!isActiveAndEnabled) return;
        SetDirty();
    }


    public virtual void Update () {
        var newSTransform = new SerializableTransform(transform);
        if (lastTransform != newSTransform) {
            lastTransform = newSTransform;
            SetDirty();
        }
    }

    public virtual void SetDirty() {
        Render();
        group?.Render();
    }

    public void Render() {
        RenderInternal();
        OnRender?.Invoke();
    }
    protected abstract void RenderInternal();

    public Vector3 EvaluateWorldVector(Vector3 position) {
        return transform.TransformDirection(EvaluateVector(position));
    }

    public virtual Vector2 EvaluateVector (Vector3 position) {
        var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(position);
        return vectorField.GetValueAtGridPosition(gridPosition) * magnitude;
    }

    public Quaternion EvaluateRotation(Vector3 position) {
        // return transform.rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value)
        return Quaternion.LookRotation(EvaluateWorldVector(position), planeNormal);
    }
    
    public Bounds GetBounds() {
        var bounds = gridRenderer.edge.NormalizedToWorldRect(new Rect(0,0,1,1));
        return BoundsX.CreateEncapsulating(bounds);
    }
    
    public static Texture2D CreateRampTextureFromAnimationCurve(AnimationCurve curve, int textureWidth = 64) {
        // if (curveTexture == null || curveTexture.width != textureWidth || curveTexture.height != 1 || curveTexture.format != TextureFormat.RFloat || curveTexture.wrapMode != TextureWrapMode.Clamp) {
        //     if (curveTexture != null) ObjectX.DestroyAutomatic(curveTexture);
        // }
        var curveTexture = new Texture2D(textureWidth, 1, TextureFormat.RFloat, false) {
            wrapMode = TextureWrapMode.Clamp
        };
        for (int i = 0; i < textureWidth; i++) {
            float t = i / (float) (textureWidth - 1);
            float value = curve.Evaluate(t);
            curveTexture.SetPixel(i, 0, new Color(value, value, value, value));
        }
        curveTexture.Apply();
        return curveTexture;
    }
}