
using UnityEngine;
using UnityEngine.Rendering;

public class VectorFieldDebugRenderer : System.IDisposable
{
    static Mesh _quad;
    static Mesh quad {
        get {
            if(_quad == null)
                _quad = CreateQuad();
            return _quad;
        }
    }
    static Texture2D _arrowTexture;
    static Texture2D arrowTexture {
        get {
            if(_arrowTexture == null)
                _arrowTexture = Resources.Load<Texture2D>("VectorFieldDebugRendererArrow");
            return _arrowTexture;
        }
    }
    static Shader arrowShader => Shader.Find("VectorField/InstanceDebugRenderer");
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int DataBuffer = Shader.PropertyToID("dataBuffer");

    [Range(0,1)]
    public float opacity = 1;
    public float maxMagnitude = 1;

    Material arrowMaterial;
    ComputeBuffer dataBuffer;
    ComputeBuffer argsBuffer;
    DataStruct[] dataArray;
    int bufferInstanceCount = -1;

    private bool disposed = false;

    public VectorFieldDebugRenderer() {
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct DataStruct
    {
        // Position in field-grid space (0..gridSize-1), so a sampled/decimated grid can place arrows anywhere.
        public Vector2 cellPoint;
        public Vector2 value;
        // LOD cross-fade weight: 1 for arrows that survive into the next-coarser octave, fading to 0 for the rest.
        public float alpha;
    }

    /// <summary>
    /// Draws the field as arrows. When <paramref name="variableResolution"/> is set, the arrow grid is decimated so
    /// on-screen spacing stays roughly constant as you zoom: <paramref name="targetSpacingPixels"/> is the desired
    /// screen-space gap between arrows, and <paramref name="maxArrows"/> caps how many arrows the long axis can show
    /// (it never supersamples past the field's native resolution).
    /// </summary>
    public void Draw(VectorFieldComponent vectorFieldComponent, float opacity, Camera camera, bool variableResolution, float targetSpacingPixels, int maxArrows) {
        var field = vectorFieldComponent.vectorField;
        if (field == null || field.values.Length == 0) return;

        var gridSize = vectorFieldComponent.gridRenderer.gridSize;
        var gridToWorldMatrix = vectorFieldComponent.gridRenderer.cellCenter.gridToWorldMatrix;

        // Quantize the arrow grid to power-of-two strides (every cell, every 2nd, every 4th...). Each level is a
        // strict subset of the finer one, so an arrow's position is stable as you cross between levels.
        int baseStride = 1;
        float arrowScale = 1f;
        float detailFade = 1f; // alpha for the "extra" arrows that the finer octave adds; fades out as we coarsen
        if (variableResolution && camera != null) {
            float stride = ComputeStride(gridSize, gridToWorldMatrix, camera, targetSpacingPixels, maxArrows);
            int octave = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(stride, 2)), 0, 16); // clamp guards the 1<<octave shift
            baseStride = 1 << octave;
            detailFade = 1f - Mathf.Clamp01(stride / baseStride - 1f); // position within the octave -> cross-fade
            arrowScale = stride; // size grows continuously with zoom, not in steps
        }

        // Sample on every baseStride-th cell. Integer steps keep levels nested (coarse = the even-indexed subset).
        int displayWidth = (gridSize.x - 1) / baseStride + 1;
        int displayHeight = (gridSize.y - 1) / baseStride + 1;
        int instanceCount = displayWidth * displayHeight;

        if (dataArray == null || dataArray.Length != instanceCount) dataArray = new DataStruct[instanceCount];

        // (Re)allocate GPU buffers only when the arrow count changes.
        if (dataBuffer == null || bufferInstanceCount != instanceCount) {
            dataBuffer?.Dispose();
            argsBuffer?.Dispose();
            dataBuffer = new ComputeBuffer(instanceCount, 5 * sizeof(float));
            argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(new uint[5] { quad.GetIndexCount(0), (uint)instanceCount, 0, 0, 0 });
            bufferInstanceCount = instanceCount;
        }

        for (int y = 0; y < displayHeight; y++) {
            for (int x = 0; x < displayWidth; x++) {
                int gridX = x * baseStride;
                int gridY = y * baseStride;
                int index = y * displayWidth + x;
                dataArray[index].cellPoint = new Vector2(gridX, gridY);
                dataArray[index].value = field.GetValueAtGridPoint(gridX, gridY);
                // Arrows shared with the next-coarser octave (even index on both axes) stay solid; the rest fade.
                bool survivesToCoarser = (x & 1) == 0 && (y & 1) == 0;
                dataArray[index].alpha = survivesToCoarser ? 1f : detailFade;
            }
        }
        dataBuffer.SetData(dataArray);

        if (arrowMaterial == null) {
            arrowMaterial = new Material(arrowShader);
            arrowMaterial.SetTexture(MainTex, arrowTexture);
        }
        // These change per frame as you zoom / pan, so set them every draw.
        arrowMaterial.SetMatrix("gridToWorldMatrix", gridToWorldMatrix);
        arrowMaterial.SetVector("scaleFactor", Vector3.one * arrowScale);
        arrowMaterial.SetFloat("maxMagnitude", maxMagnitude);
        arrowMaterial.SetFloat("_Opacity", opacity);
        arrowMaterial.SetBuffer(DataBuffer, dataBuffer);

        Graphics.DrawMeshInstancedIndirect(quad, 0, arrowMaterial, new Bounds(Vector3.zero, new Vector3(100000000, 100000000, 100000000)), argsBuffer, 0, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
    }

    // Continuous cells-per-arrow needed to keep arrows ~targetSpacingPixels apart on screen, never below 1
    // (native res) and never finer than the maxArrows cap on the long axis.
    static float ComputeStride(Point gridSize, Matrix4x4 gridToWorldMatrix, Camera camera, float targetSpacingPixels, int maxArrows) {
        var centre = new Vector3((gridSize.x - 1) * 0.5f, (gridSize.y - 1) * 0.5f, 0);
        Vector3 worldOrigin = gridToWorldMatrix.MultiplyPoint3x4(centre);
        float pixelsPerCellX = ScreenDistance(camera, worldOrigin, gridToWorldMatrix.MultiplyPoint3x4(centre + Vector3.right));
        float pixelsPerCellY = ScreenDistance(camera, worldOrigin, gridToWorldMatrix.MultiplyPoint3x4(centre + Vector3.up));
        float pixelsPerCell = Mathf.Min(pixelsPerCellX, pixelsPerCellY);

        float zoomStride = pixelsPerCell > 1e-4f ? targetSpacingPixels / pixelsPerCell : 1f;
        int longAxis = Mathf.Max(gridSize.x, gridSize.y);
        float capStride = maxArrows > 0 ? (float)longAxis / maxArrows : 1f;
        return Mathf.Max(1f, zoomStride, capStride);
    }

    static float ScreenDistance(Camera camera, Vector3 worldA, Vector3 worldB) {
        Vector3 a = camera.WorldToScreenPoint(worldA);
        Vector3 b = camera.WorldToScreenPoint(worldB);
        return new Vector2(a.x - b.x, a.y - b.y).magnitude;
    }

    static Mesh CreateQuad() {
        Mesh mesh = new Mesh();

        Vector3[] vertices = {
            new(-0.5f, -0.5f, 0),
            new(0.5f, -0.5f, 0),
            new(-0.5f, 0.5f, 0),
            new(0.5f, 0.5f, 0)
        };

        int[] triangles = { 0, 2, 1, 2, 3, 1 };

        Vector2[] uvs = {
            new(0, 0),
            new(1, 0),
            new(0, 1),
            new(1, 1)
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        return mesh;
    }

    public void Dispose()
    {
        if (disposed) return;
        dataBuffer?.Dispose();
        dataBuffer = null;
        argsBuffer?.Dispose();
        argsBuffer = null;

        if (arrowMaterial != null) {
            if (Application.isPlaying) Object.Destroy(arrowMaterial);
            else Object.DestroyImmediate(arrowMaterial);
            arrowMaterial = null;
        }
        disposed = true;
    }
}


/*
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

// Draws arrows for each vector in the vector field
[ExecuteAlways, RequireComponent(typeof(VectorFieldComponent))]
public class VectorFieldDebugRenderer : MonoBehaviour {
    static Mesh _quad;
    static Mesh quad {
        get {
            if(_quad == null)
                _quad = CreateQuad();
            return _quad;
        }
    }
    static Texture2D _arrowTexture;
    static Texture2D arrowTexture {
        get {
            if(_arrowTexture == null)
                _arrowTexture = Resources.Load<Texture2D>("VectorFieldDebugRendererArrow");
            return _arrowTexture;
        }
    }
    static Shader arrowShader => Shader.Find("VectorField/InstanceDebugRenderer");
    static readonly int MainTex = Shader.PropertyToID("_MainTex");
    static readonly int MatrixBuffer = Shader.PropertyToID("matrixBuffer");
    static readonly int ColorBuffer = Shader.PropertyToID("colorBuffer");
    static readonly int DataBuffer = Shader.PropertyToID("dataBuffer");

    VectorFieldComponent vectorFieldComponent;

    [Range(0,1)]
    public float opacity = 1;
    public float maxMagnitude = 1;
    Material arrowMaterial;
    ComputeBuffer matrixBuffer;
    ComputeBuffer colorBuffer;
    ComputeBuffer argsBuffer;

    void Init() {
        CleanUp();

        if (vectorFieldComponent == null) {
            vectorFieldComponent = GetComponent<VectorFieldComponent>();
        }
        vectorFieldComponent.OnRender += VectorFieldComponentOnOnRender;

        ResetBuffers();


        arrowMaterial = new Material(arrowShader);
        // arrowMaterial.SetTexture(MainTex, arrowTexture);
        // arrowMaterial.SetBuffer(MatrixBuffer, matrixBuffer);
        // arrowMaterial.SetBuffer(ColorBuffer, colorBuffer);

        SetData((uint)vectorFieldComponent.vectorField.values.Length);
    }

    void VectorFieldComponentOnOnRender() {
        // if (BuffersInvalid())
        //     ResetBuffers();
        // SetData((uint)vectorFieldComponent.vectorField.values.Length);
    }

    bool BuffersInvalid() {
        if (matrixBuffer.count != vectorFieldComponent.vectorField.values.Length) return true;
        else if (colorBuffer.count != vectorFieldComponent.vectorField.values.Length) return true;
        else if (argsBuffer.count != 1) return true;
        return false;
    }

    void CleanUp() {
        if(vectorFieldComponent != null)
            vectorFieldComponent.OnRender -= VectorFieldComponentOnOnRender;

        matrixBuffer?.Dispose();
        matrixBuffer = null;
        colorBuffer?.Dispose();
        colorBuffer = null;
        argsBuffer?.Dispose();
        argsBuffer = null;

        if (Application.isPlaying) Destroy(arrowMaterial);
        else DestroyImmediate(arrowMaterial);
        arrowMaterial = null;
    }

    void ResetBuffers() {
        matrixBuffer?.Dispose();
        colorBuffer?.Dispose();
        argsBuffer?.Dispose();

        uint instanceCount = (uint)vectorFieldComponent.vectorField.values.Length;
        matrixBuffer = new ComputeBuffer((int) instanceCount, 16 * sizeof(float));
        colorBuffer = new ComputeBuffer((int) instanceCount, 4 * sizeof(float));
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { quad.GetIndexCount(0), instanceCount, 0, 0, 0 };
        argsBuffer.SetData(args);
    }

    void Update() {
        if (matrixBuffer == null || matrixBuffer.count == 0) Init();

        arrowMaterial.SetTexture(MainTex, arrowTexture);
        arrowMaterial.SetBuffer(MatrixBuffer, matrixBuffer);
        arrowMaterial.SetBuffer(ColorBuffer, colorBuffer);

        // I don't think I'm supposed to zero out the bounds but it moves if I don't!
        // Graphics.DrawMeshInstancedIndirect(quad, 0, arrowMaterial, new Bounds(Vector3.zero, vectorFieldComponent.GetBounds().size), argsBuffer);
        // Graphics.DrawMeshInstancedIndirect(quad, 0, arrowMaterial, new Bounds(Vector3.zero, new Vector3(100000000,100000000,100000000)), argsBuffer, 0, null, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);

        Draw(vectorFieldComponent, opacity, maxMagnitude, Camera.main);
    }

    void SetData(uint instanceCount) {
        Matrix4x4[] matrices = new Matrix4x4[instanceCount];
        Color[] colors = new Color[instanceCount];
        var scaleFactor = vectorFieldComponent.gridRenderer.cellCenter.gridToWorldMatrix.lossyScale / maxMagnitude;
        var rotation = vectorFieldComponent.transform.rotation;
        foreach (var cell in vectorFieldComponent.vectorField) {
            matrices[cell.index] = Matrix4x4.TRS(vectorFieldComponent.gridRenderer.cellCenter.GridToWorldPoint(cell.point), rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value), scaleFactor * cell.value.magnitude);
            float angle = 90 - Vector2.SignedAngle(cell.value, Vector2.up);
            colors[cell.index] = new HSLColor(angle, 1, 0.5f, Mathf.Clamp01(cell.value.magnitude / maxMagnitude) * opacity).ToRGBA();
        }

        matrixBuffer.SetData(matrices);
        colorBuffer.SetData(colors);


        arrowMaterial.SetTexture(MainTex, arrowTexture);
        arrowMaterial.SetBuffer(MatrixBuffer, matrixBuffer);
        arrowMaterial.SetBuffer(ColorBuffer, colorBuffer);
    }

    void Awake() {
        Init();
    }
    void OnValidate() {
        if(!isActiveAndEnabled) return;
        Init();
    }
    void OnEnable() {
        Init();
    }
    void OnDisable() {
        CleanUp();
    }
    // void OnDestroy() {
    //     CleanUp();
    // }

    void OnDrawGizmosSelected() {
        Draw(vectorFieldComponent, opacity, maxMagnitude, Camera.current);
    }


    static Mesh CreateQuad() {
        Mesh mesh = new Mesh();

        Vector3[] vertices = {
            new(-0.5f, -0.5f, 0),
            new(0.5f, -0.5f, 0),
            new(-0.5f, 0.5f, 0),
            new(0.5f, 0.5f, 0)
        };

        int[] triangles = { 0, 2, 1, 2, 3, 1 };

        Vector2[] uvs = {
            new(0, 0),
            new(1, 0),
            new(0, 1),
            new(1, 1)
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        return mesh;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct DataStruct
    {
        public Vector2Int coord;
        public Vector2 value;
    }
    public static void Draw(VectorFieldComponent vectorFieldComponent, float opacity = 1, float maxMagnitude = 1, Camera camera = null) {
        uint instanceCount = (uint)vectorFieldComponent.vectorField.values.Length;
        if (instanceCount == 0) return;

        var dataBuffer = new ComputeBuffer((int) instanceCount, 2 * sizeof(int) + 2 * sizeof(float));
        // var matrixBuffer = new ComputeBuffer((int) instanceCount, 16 * sizeof(float));
        // var colorBuffer = new ComputeBuffer((int) instanceCount, 4 * sizeof(float));
        var argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5] { quad.GetIndexCount(0), instanceCount, 0, 0, 0 };
        argsBuffer.SetData(args);

        DataStruct[] data = new DataStruct[instanceCount];
        // Matrix4x4[] matrices = new Matrix4x4[instanceCount];
        // Color[] colors = new Color[instanceCount];
        var gridToWorldMatrix = vectorFieldComponent.gridRenderer.cellCenter.gridToWorldMatrix;
        var scaleFactor = Vector3.one / maxMagnitude;
        // var rotation = vectorFieldComponent.transform.rotation;

        for (var index = 0; index < vectorFieldComponent.vectorField.values.Length; index++) {
            var value = vectorFieldComponent.vectorField.values[index];
            data[index].value = value;
            // matrices[cell.index] = Matrix4x4.TRS(gridToWorldMatrix.MultiplyPoint3x4(cell.point), rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value), scaleFactor * cell.value.magnitude);
            // float angle = 90 - Vector2.SignedAngle(cell.value, Vector2.up);
            // colors[cell.index] = new HSLColor(angle, 1, 0.5f, Mathf.Clamp01(cell.value.magnitude / maxMagnitude) * opacity).ToRGBA();
        }

        // matrixBuffer.SetData(matrices);
        // colorBuffer.SetData(colors);
        dataBuffer.SetData(data);

        var arrowMaterial = new Material(arrowShader);
        arrowMaterial.SetTexture(MainTex, arrowTexture);
        arrowMaterial.SetMatrix("gridToWorldMatrix", gridToWorldMatrix);
        arrowMaterial.SetVector("scaleFactor", scaleFactor);
        arrowMaterial.SetInt("gridWidth", vectorFieldComponent.gridRenderer.gridSize.x);
        arrowMaterial.SetFloat("maxMagnitude", maxMagnitude);
        arrowMaterial.SetFloat("_Opacity", opacity);
        // arrowMaterial.SetBuffer(MatrixBuffer, matrixBuffer);
        // arrowMaterial.SetBuffer(ColorBuffer, colorBuffer);
        arrowMaterial.SetBuffer(DataBuffer, dataBuffer);

        Graphics.DrawMeshInstancedIndirect(quad, 0, arrowMaterial, new Bounds(Vector3.zero, new Vector3(100000000,100000000,100000000)), argsBuffer, 0, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);

        EditorApplication.delayCall += () => {
            // matrixBuffer?.Dispose();
            // colorBuffer?.Dispose();
            dataBuffer?.Dispose();
            argsBuffer?.Dispose();

            if (Application.isPlaying) Destroy(arrowMaterial);
            else DestroyImmediate(arrowMaterial);
            arrowMaterial = null;
        };
    }
}
*/
