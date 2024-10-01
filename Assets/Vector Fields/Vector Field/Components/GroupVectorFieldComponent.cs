using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GroupVectorFieldComponent : VectorFieldComponent
{
    [System.Serializable]
    public class VectorFieldLayer
    {
        public VectorFieldComponent component;

        [Range(0, 1)]
        public float strength = 1;

        public BlendMode blendMode = BlendMode.Add;
        public enum BlendMode
        {
            // Add to current value
            Add,
            // Lerp between current and new value based on brush alpha
            Blend
        }

        [EnumFlagsButtonGroup] public Component components = Component.All;
        [Flags]
        public enum Component
        {
            None = 0,
            All = ~0,
            // Add to current value
            Magnitude = 1 << 0,
            // Lerp between current and new value based on brush alpha
            Direction = 1 << 1,
        }

        // public Texture2D texture;
    }

    public List<VectorFieldLayer> layers = new List<VectorFieldLayer>();
    IEnumerable<VectorFieldComponent> childComponents => this.GetComponentsX(ComponentX.ComponentSearchParams<VectorFieldComponent>.AllDescendentsExcludingSelf(false));

    public Mode mode = Mode.CPU;
    public enum Mode
    {
        CPU,
        GPU
    }
    void RefreshLayers()
    {
        layers.RemoveAll(x => x.component == null);
        List<VectorFieldComponent> added = new List<VectorFieldComponent>();
        List<VectorFieldComponent> removed = new List<VectorFieldComponent>();
        IEnumerableX.GetChanges(childComponents, layers.Select(x => x.component), out added, out removed);
        foreach (var component in added)
        {
            layers.Add(new VectorFieldLayer()
            {
                component = component
            });
        }
        foreach (var component in removed)
        {
            layers.RemoveAll(x => x.component == component);
        }
        layers = layers.OrderBy(x => x.component.transform.GetHeirarchyIndex()).ToList();
    }

    protected override void RenderInternal()
    {
        RefreshLayers();

        // For performance we should iterate layers first, then iterate points.
        // For each layer we should first determine the points on both canvases that are in the overlap.
        // var points = gridRenderer.GetPointsInWorldBounds(child.transform.GetBounds());

        if (mode == Mode.CPU)
            RenderInternalCPU();
        else
            RenderInternalGPU();
    }


    public override void Update()
    {
        base.Update();
        SetDirty();
    }

    public Material combineShaderMaterial;

    void RenderInternalGPU()
    {
        if (layers.Count == 0 || combineShaderMaterial == null)
            return;

        // Create two temporary RenderTextures
        RenderTexture currentRT = RenderTexture.GetTemporary(gridRenderer.gridSize.x, gridRenderer.gridSize.y, 0, RenderTextureFormat.ARGBFloat);
        RenderTexture nextRT = RenderTexture.GetTemporary(gridRenderer.gridSize.x, gridRenderer.gridSize.y, 0, RenderTextureFormat.ARGBFloat);
        
        RenderTexture.active = currentRT;
        GL.Clear(true, true, new Color(0.5f, 0.5f, 0, 1));
        RenderTexture.active = null;

        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] == null || layers[i].component.vectorFieldTexture == null) continue;
            if(layers[i].components == VectorFieldLayer.Component.None) continue;
            
            // Adjust for UV coordinate space (translate UV to object space)
            Matrix4x4 UVtoObj = Matrix4x4.Translate(new Vector3(-0.5f, -0.5f, 0));
            // Adjust back from object space to UV space after transformations
            Matrix4x4 ObjToUV = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
            // Compute the matrix that transforms from t1's UV space to t2's UV space
            Matrix4x4 M = ObjToUV * layers[i].component.transform.worldToLocalMatrix * transform.localToWorldMatrix * UVtoObj;
            
            // GetRelativeTransform(layers[i].component.transform, transform)
            combineShaderMaterial.SetTexture("_VectorField", layers[i].component.vectorFieldTexture);
            combineShaderMaterial.SetMatrix("_RelativeTransform", M);
            combineShaderMaterial.SetVector("_TextureSize", new Vector4(layers[i].component.vectorFieldTexture.width, layers[i].component.vectorFieldTexture.height, 0, 0));
            combineShaderMaterial.SetFloat("_Strength", layers[i].strength);
            combineShaderMaterial.SetFloat("_BlendMode", (int)layers[i].blendMode);
            if(layers[i].components == VectorFieldLayer.Component.All) combineShaderMaterial.SetFloat("_Components", 0);
            else if(layers[i].components == VectorFieldLayer.Component.Magnitude) combineShaderMaterial.SetFloat("_Components", 1);
            else if(layers[i].components == VectorFieldLayer.Component.Direction) combineShaderMaterial.SetFloat("_Components", 2);

            RenderTexture.active = nextRT;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = null;
            
            Graphics.Blit(currentRT, nextRT, combineShaderMaterial);

            // Swap render textures
            (currentRT, nextRT) = (nextRT, currentRT);
        }

        CreateVectorFieldTexture();

        // Convert RenderTexture to Texture2D
        RenderTexture.active = currentRT;
        vectorFieldTexture.ReadPixels(new Rect(0, 0, vectorFieldTexture.width, vectorFieldTexture.height), 0, 0);
        vectorFieldTexture.Apply();
        RenderTexture.active = null;

        // Release temporary render textures
        RenderTexture.ReleaseTemporary(currentRT);
        RenderTexture.ReleaseTemporary(nextRT);

        // Update the vectorField data
        Color[] colors = vectorFieldTexture.GetPixels();
        Vector2[] vectors = VectorFieldUtils.ColorsToVectors(colors, 1);
        vectorField = new Vector2Map(new Point(vectorFieldTexture.width, vectorFieldTexture.height), vectors);
    }

    void RenderInternalCPU()
    {
        vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
        // var points = vectorField.Points();

        var validLayers = layers.Where(layer => layer.component.isActiveAndEnabled && layer.strength > 0).ToList();
        foreach (var layer in validLayers)
        {
            var points = gridRenderer.GetPointsInWorldBounds(layer.component.GetBounds());
            foreach (var point in points)
            {
                Vector2 vectorFieldForce = vectorField.GetValueAtGridPoint(point);

                var pointWorldPosition = gridRenderer.cellCenter.GridToWorldPoint(point);
                Vector2 affectorForce = transform.InverseTransformDirection(layer.component.EvaluateWorldVector(pointWorldPosition));
                Vector2 finalForce = Vector2.zero;

                if (layer.blendMode == VectorFieldLayer.BlendMode.Add)
                {
                    if (layer.components.HasFlag(VectorFieldLayer.Component.All)) finalForce = vectorFieldForce + affectorForce * layer.strength;
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Direction)) finalForce = affectorForce + vectorFieldForce.magnitude * affectorForce.normalized * layer.strength;
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Magnitude)) finalForce = vectorFieldForce + vectorFieldForce.normalized * affectorForce.magnitude * layer.strength;
                }

                if (layer.blendMode == VectorFieldLayer.BlendMode.Blend)
                {
                    if (layer.components.HasFlag(VectorFieldLayer.Component.All)) finalForce = Vector2.Lerp(vectorFieldForce, affectorForce, layer.strength);
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Direction)) finalForce = affectorForce.normalized * layer.strength;
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Magnitude)) finalForce = vectorFieldForce.normalized * layer.strength;
                }

                vectorFieldForce = finalForce;
                vectorField.SetValueAtGridPoint(point, vectorFieldForce);
            }
        }
    }
}