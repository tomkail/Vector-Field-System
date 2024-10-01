using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class VectorFieldCookie
{
    public Texture2D cookieTexture;
    public HeightMap cookieMap;
}

[ExecuteAlways, RequireComponent(typeof(GridRenderer))]
public abstract class VectorFieldComponent : MonoBehaviour
{
    protected GroupVectorFieldComponent group => this.GetComponentsX(ComponentX.ComponentSearchParams<GroupVectorFieldComponent>.AllAncestorsExcludingSelf(true)).FirstOrDefault();

    public GridRenderer gridRenderer { get; private set; }
    public Vector3 planeNormal => transform.forward;

    [Space]
    public Vector2Map vectorField;
    [AssetSaver] public Texture2D vectorFieldTexture;

    public float magnitude = 1;
    // public VectorFieldCookie cookie;

    public delegate void OnUpdateDelegate();
    public event OnUpdateDelegate OnRender;

    SerializableTransform lastTransform;

    protected virtual void OnEnable()
    {
        // This will leak?
        if (vectorFieldTexture != null) vectorFieldTexture = null;

        gridRenderer = GetComponent<GridRenderer>();
        SetDirty();
        Update();
    }

    protected virtual void OnDisable()
    {
        TryRenderGroup();
    }

    void OnDestroy()
    {
        ObjectX.DestroyAutomatic(vectorFieldTexture);
    }

    protected virtual void OnValidate()
    {
        gridRenderer = GetComponent<GridRenderer>();
        gridRenderer.modeModule = ScriptableObject.CreateInstance<GridRendererManhattanModeModule>();
        gridRenderer.scaleWithGridSize = false;
        if (gridRenderer.gridSize == Point.zero) gridRenderer.gridSize = new Point(64, 64);
        if (gridRenderer.gridSize.x < 1) gridRenderer.gridSize = new Point(1, gridRenderer.gridSize.y);
        if (gridRenderer.gridSize.y < 1) gridRenderer.gridSize = new Point(gridRenderer.gridSize.x, 1);
        // gridRenderer.showGizmos = true;
        lastTransform = new SerializableTransform(transform);
        if (!isActiveAndEnabled) return;
        SetDirty();
    }


    public virtual void Update()
    {
        var newSTransform = new SerializableTransform(transform);
        if (lastTransform != newSTransform)
        {
            lastTransform = newSTransform;
            SetDirty();
        }
    }

    public virtual void SetDirty()
    {
        Render();
        TryRenderGroup();
    }

    void TryRenderGroup()
    {
        if (group != null && group.isActiveAndEnabled) group.Render();
    }

    public void Render()
    {
        RenderInternal();

        if (GetType() != typeof(GroupVectorFieldComponent) && GetType() != typeof(NoiseVectorFieldComponent) && GetType() != typeof(StampVectorFieldComponent))
        {
            if (vectorFieldTexture == null || vectorFieldTexture.width != vectorField.size.x || vectorFieldTexture.height != vectorField.size.y)
            {
                if (vectorFieldTexture != null) DestroyImmediate(vectorFieldTexture);
                vectorFieldTexture = new Texture2D(vectorField.size.x, vectorField.size.y, TextureFormat.RGFloat, false, true)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            var colors = VectorFieldUtils.VectorsToColors(vectorField.values, 1);
            vectorFieldTexture.SetPixels(colors);
            vectorFieldTexture.Apply();
        }


        OnRender?.Invoke();
    }

    protected void CreateVectorFieldTexture() {
        if (vectorFieldTexture == null || vectorFieldTexture.width != gridRenderer.gridSize.x || vectorFieldTexture.height != gridRenderer.gridSize.y) {
            if (vectorFieldTexture != null) DestroyImmediate(vectorFieldTexture);
            
            vectorFieldTexture = new Texture2D(gridRenderer.gridSize.x,gridRenderer.gridSize.y, TextureFormat.RGFloat, false, true) {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
    
    protected abstract void RenderInternal();

    public Vector3 EvaluateWorldVector(Vector3 position)
    {
        return transform.TransformDirection(EvaluateVector(position));
    }

    public virtual Vector2 EvaluateVector(Vector3 position)
    {
        var gridPosition = gridRenderer.cellCenter.WorldToGridPosition(position);
        return vectorField.GetValueAtGridPosition(gridPosition) * magnitude;
    }

    public Quaternion EvaluateRotation(Vector3 position)
    {
        // return transform.rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value)
        return Quaternion.LookRotation(EvaluateWorldVector(position), planeNormal);
    }

    public Bounds GetBounds()
    {
        var bounds = gridRenderer.edge.NormalizedToWorldRect(new Rect(0, 0, 1, 1));
        return BoundsX.CreateEncapsulating(bounds);
    }

    public static Texture2D CreateRampTextureFromAnimationCurve(AnimationCurve curve, int textureWidth, ref Texture2D texture)
    {
        // if (curveTexture == null || curveTexture.width != textureWidth || curveTexture.height != 1 || curveTexture.format != TextureFormat.RFloat || curveTexture.wrapMode != TextureWrapMode.Clamp) {
        //     if (curveTexture != null) ObjectX.DestroyAutomatic(curveTexture);
        // }
        if (texture == null)
        {
            texture = new Texture2D(textureWidth, 1, TextureFormat.RFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp
            };
        }
        for (int i = 0; i < textureWidth; i++)
        {
            float t = i / (float)(textureWidth - 1);
            float value = curve.Evaluate(t);
            texture.SetPixel(i, 0, new Color(value, value, value, value));
        }
        texture.Apply();
        return texture;
    }
}