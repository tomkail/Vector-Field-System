#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using UnityEngine;
using UnityEngine.Rendering;

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
    public struct MyStruct
    {
        public Vector2Int coord;
        public Vector2 value;
    }

    void OnDrawGizmosSelected() {
        Draw(vectorFieldComponent, opacity, maxMagnitude, Camera.current);
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
        
        MyStruct[] structs = new MyStruct[instanceCount];
        // Matrix4x4[] matrices = new Matrix4x4[instanceCount];
        // Color[] colors = new Color[instanceCount];
        var gridToWorldMatrix = vectorFieldComponent.gridRenderer.cellCenter.gridToWorldMatrix;
        var scaleFactor = Vector3.one / maxMagnitude;
        // var rotation = vectorFieldComponent.transform.rotation;

        for (var index = 0; index < vectorFieldComponent.vectorField.values.Length; index++) {
            var value = vectorFieldComponent.vectorField.values[index];
            structs[index].value = value;
            // matrices[cell.index] = Matrix4x4.TRS(gridToWorldMatrix.MultiplyPoint3x4(cell.point), rotation * Quaternion.LookRotation(Vector3.forward, (Vector3) cell.value), scaleFactor * cell.value.magnitude);
            // float angle = 90 - Vector2.SignedAngle(cell.value, Vector2.up);
            // colors[cell.index] = new HSLColor(angle, 1, 0.5f, Mathf.Clamp01(cell.value.magnitude / maxMagnitude) * opacity).ToRGBA();
        }

        // matrixBuffer.SetData(matrices);
        // colorBuffer.SetData(colors);
        dataBuffer.SetData(structs);

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