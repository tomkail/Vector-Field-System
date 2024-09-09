using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GroupVectorFieldComponent : VectorFieldComponent {
    [System.Serializable]
    public class VectorFieldLayer {
        public VectorFieldComponent component;
        
        [Range(0,1)]
        public float strength = 1;
    
        public BlendMode blendMode = BlendMode.Add;
        public enum BlendMode {
            // Add to current value
            Add,
            // Lerp between current and new value based on brush alpha
            Blend
        }
	
        [EnumFlagsButtonGroup] public Component components = Component.All;
        [Flags]
        public enum Component {
            None = 0,
            All = ~0,
            // Add to current value
            Magnitude = 1 << 0,
            // Lerp between current and new value based on brush alpha
            Direction = 1 << 1,
        }
        
        public Texture2D texture;
    }

    public List<VectorFieldLayer> layers = new List<VectorFieldLayer>();
    IEnumerable<VectorFieldComponent> childComponents => this.GetComponentsX(ComponentX.ComponentSearchParams<VectorFieldComponent>.AllDescendentsExcludingSelf(true));

    void RefreshLayers() {
        layers.RemoveAll(x => x.component == null);
        List<VectorFieldComponent> added = new List<VectorFieldComponent>();
        List<VectorFieldComponent> removed = new List<VectorFieldComponent>();
        IEnumerableX.GetChanges(childComponents, layers.Select(x => x.component), out added, out removed);
        foreach (var component in added) {
            layers.Add(new VectorFieldLayer() {
                component = component
            });
        }
        foreach (var component in removed) {
            layers.RemoveAll(x => x.component == component);
        }
        layers = layers.OrderBy(x => x.component.transform.GetHeirarchyIndex()).ToList();
    }

    protected override void RenderInternal() {
        RefreshLayers();
        
        // For performance we should iterate layers first, then iterate points.
        // For each layer we should first determine the points on both canvases that are in the overlap.
        // var points = gridRenderer.GetPointsInWorldBounds(child.transform.GetBounds());

        RenderInternalCPU();
        // RenderInternalGPU();
    }

    
    public ComputeShader computeShader;
    [PreviewTexture] public RenderTexture vectorFieldTexture;
    private ComputeBuffer layersDataBuffer;
    
    // Must match what's in the compute shader
    const int threadsPerGroupX = 8;
    const int threadsPerGroupY = 8;

    void InitializeTextures() {
        vectorFieldTexture = new RenderTexture(gridRenderer.gridSize.x, gridRenderer.gridSize.y, 0, RenderTextureFormat.RGFloat);
        vectorFieldTexture.enableRandomWrite = true;
        vectorFieldTexture.Create();
    }
    
    void InitializeBuffers()
    {
        LayerData[] layerDataArray = new LayerData[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            layerDataArray[i] = new LayerData
            {
                strength = layers[i].strength,
                blendMode = (int)layers[i].blendMode,
                components = (int)layers[i].components
            };
        }

        layersDataBuffer = new ComputeBuffer(layers.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(LayerData)));
        layersDataBuffer.SetData(layerDataArray);
    }
    
    void OnDestroy() {
        if (layersDataBuffer != null) {
            layersDataBuffer.Release();
            layersDataBuffer = null;
        }
    }

    struct LayerData
    {
        public float strength;
        public int blendMode;
        public int components;
    }
    void RenderInternalGPU()
    {
        if(vectorFieldTexture == null) InitializeTextures();
        if (layersDataBuffer != null) {
            layersDataBuffer.Release();
            layersDataBuffer = null;
        }
        if(layersDataBuffer == null) InitializeBuffers();
        
        int kernelHandle = computeShader.FindKernel("CSMain");

        computeShader.SetTexture(kernelHandle, "Result", vectorFieldTexture);

        // Set the parameters for each layer
        var validLayers = layers.Where(layer => layer.component.isActiveAndEnabled && layer.strength > 0).ToList();
        int index = 0;
        foreach(var layer in validLayers) {
            if (!layer.component.isActiveAndEnabled) continue;
            if (layer.strength <= 0) continue;
            
            if (layer.texture == null || layer.texture.width != layer.component.vectorField.size.x || layer.texture.height != layer.component.vectorField.size.y) {
                layer.texture = new Texture2D(layer.component.vectorField.size.x, layer.component.vectorField.size.y, TextureFormat.RGFloat, false);
                layer.texture.filterMode = FilterMode.Bilinear;
            }
            // float calculatedScale = VectorFieldScriptableObject.GetMaxAbsComponent(child.component.vectorField.values);
            // 1f/calculatedScale
            var colors = VectorFieldUtils.VectorsToColors(layer.component.vectorField.values, 1);
        
            // Color[] colors = new Color[vectorField.vectorField.values.Length];
            // for(int i = 0; i < vectorField.vectorField.values.Length; i++) {
            //     colors[i] = new Color(vectorField.vectorField.values[i].x, vectorField.vectorField.values[i].y, 0);
            // }
        
            layer.texture.SetPixels(colors);
            layer.texture.Apply();
            
            // RenderTexture childTexture = child.component.GetVectorFieldTexture();
            computeShader.SetTexture(kernelHandle, "ChildTextures[" + index + "]", layer.texture);
            index++;
        }
        computeShader.SetBuffer(kernelHandle, "LayersDataBuffer", layersDataBuffer);
        computeShader.SetInt("numLayers", validLayers.Count);

        int threadGroupsX = Mathf.CeilToInt((float)vectorField.size.x / threadsPerGroupX);
        int threadGroupsY = Mathf.CeilToInt((float)vectorField.size.y / threadsPerGroupY);
        computeShader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, 1);
        
        
        
        // Create a Texture2D with the same dimensions as the RenderTexture
        var tempTexture = new Texture2D(vectorFieldTexture.width, vectorFieldTexture.height, TextureFormat.RGFloat, false);

        // Read the data from the RenderTexture into the Texture2D
        RenderTexture.active = vectorFieldTexture;
        tempTexture.ReadPixels(new Rect(0, 0, vectorFieldTexture.width, vectorFieldTexture.height), 0, 0);
        tempTexture.Apply();
        RenderTexture.active = null;

        // Get the pixel data from the Texture2D
        Color[] pixelData = tempTexture.GetPixels();

        vectorField = new Vector2Map(gridRenderer.gridSize);
        // Convert the Color array to a float array
        for (int i = 0; i < pixelData.Length; i++) {
            vectorField[i] = VectorFieldUtils.ColorToVector(pixelData[i], 1);
        }
        
        ObjectX.DestroyAutomatic(tempTexture);

        // Now you have the vector field data in vectorFieldData array
        Debug.Log("Vector field data read successfully.");
    }

    void RenderInternalCPU() {
        vectorField = new Vector2Map(gridRenderer.gridSize, Vector2.zero);
        // var points = vectorField.Points();

        var validLayers = layers.Where(layer => layer.component.isActiveAndEnabled && layer.strength > 0).ToList();
        foreach (var layer in validLayers) {
            var points = gridRenderer.GetPointsInWorldBounds(layer.component.GetBounds());
            foreach (var point in points) {
                Vector2 vectorFieldForce = vectorField.GetValueAtGridPoint(point);
                
                var pointWorldPosition = gridRenderer.cellCenter.GridToWorldPoint(point);
                Vector2 affectorForce = transform.InverseTransformDirection(layer.component.EvaluateWorldVector(pointWorldPosition));
                Vector2 finalForce = Vector2.zero;
                
                if (layer.blendMode == VectorFieldLayer.BlendMode.Add) {
                    if (layer.components.HasFlag(VectorFieldLayer.Component.All)) finalForce = vectorFieldForce + affectorForce * layer.strength;
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Direction)) finalForce = affectorForce + vectorFieldForce.magnitude * affectorForce.normalized * layer.strength;
                    else if (layer.components.HasFlag(VectorFieldLayer.Component.Magnitude)) finalForce = vectorFieldForce + vectorFieldForce.normalized * affectorForce.magnitude * layer.strength;
                }

                if (layer.blendMode == VectorFieldLayer.BlendMode.Blend) {
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