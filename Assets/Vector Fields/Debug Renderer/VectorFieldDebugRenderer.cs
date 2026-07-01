
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
    static readonly int FieldTex = Shader.PropertyToID("_FieldTex");
    static readonly int GridToWorldMatrix = Shader.PropertyToID("gridToWorldMatrix");
    static readonly int ScaleFactor = Shader.PropertyToID("scaleFactor");
    static readonly int MaxMagnitudeProp = Shader.PropertyToID("maxMagnitude");
    static readonly int OpacityProp = Shader.PropertyToID("_Opacity");
    static readonly int FieldSize = Shader.PropertyToID("fieldSize");
    static readonly int DisplayWidth = Shader.PropertyToID("displayWidth");
    static readonly int BaseStride = Shader.PropertyToID("baseStride");
    static readonly int DetailFade = Shader.PropertyToID("detailFade");

    [Range(0,1)]
    public float opacity = 1;
    public float maxMagnitude = 1;

    Material arrowMaterial;
    GraphicsBuffer argsBuffer;
    int bufferInstanceCount = -1;

    private bool disposed = false;

    public VectorFieldDebugRenderer() {
    }

    /// <summary>
    /// Draws the field as arrows. When <paramref name="variableResolution"/> is set, the arrow grid is decimated so
    /// on-screen spacing stays roughly constant as you zoom: <paramref name="targetSpacingPixels"/> is the desired
    /// screen-space gap between arrows, and <paramref name="maxArrows"/> caps how many arrows the long axis can show
    /// (it never supersamples past the field's native resolution).
    /// </summary>
    public void Draw(VectorFieldComponent vectorFieldComponent, float opacity, Camera camera, bool variableResolution, float targetSpacingPixels, int maxArrows) {
        // Sample the field straight off the GPU. No CPU readback / value buffer is needed, so the arrows always
        // reflect the live render texture without any CPU consumer registered.
        var fieldTexture = vectorFieldComponent.renderTexture;
        if (fieldTexture == null) return; // nothing has been rendered yet

        var gridSize = vectorFieldComponent.gridRenderer.gridSize;
        var gridToWorldMatrix = vectorFieldComponent.gridRenderer.cellCenter.gridToWorldMatrix;

        // Quantize the arrow grid to power-of-two strides (every cell, every 2nd, every 4th...). detailFade fades the
        // arrows that the finer octave adds on top of the coarser one, cross-fading as you move between levels.
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

        // Arrow counts per axis. The grid is strided by baseStride and anchored on the field's centre cell (kept fixed
        // across octaves, so coarser levels are exact subsets of finer ones — shared arrows never move as you zoom).
        // The shader reconstructs the same anchor from fieldSize + baseStride; here we only need the counts. Centring
        // also spreads coverage symmetrically so both edges are reached as closely as integer stride positions allow,
        // rather than always hugging the bottom-left and skipping the top-right.
        int anchorX = (gridSize.x - 1) / 2;
        int anchorY = (gridSize.y - 1) / 2;
        int displayWidth = anchorX / baseStride + (gridSize.x - 1 - anchorX) / baseStride + 1;
        int displayHeight = anchorY / baseStride + (gridSize.y - 1 - anchorY) / baseStride + 1;
        int instanceCount = displayWidth * displayHeight;

        // (Re)allocate the indirect-args buffer only when the arrow count changes.
        if (argsBuffer == null || bufferInstanceCount != instanceCount) {
            argsBuffer?.Dispose();
            argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                                            GraphicsBuffer.IndirectDrawIndexedArgs.size);
            argsBuffer.SetData(new[] { new GraphicsBuffer.IndirectDrawIndexedArgs {
                indexCountPerInstance = quad.GetIndexCount(0), instanceCount = (uint)instanceCount } });
            bufferInstanceCount = instanceCount;
        }

        if (arrowMaterial == null) {
            arrowMaterial = new Material(arrowShader);
            arrowMaterial.SetTexture(MainTex, arrowTexture);
        }
        // The vertex shader derives each arrow's cell, value, and LOD fade from these uniforms + the instance id.
        arrowMaterial.SetTexture(FieldTex, fieldTexture);
        arrowMaterial.SetMatrix(GridToWorldMatrix, gridToWorldMatrix);
        arrowMaterial.SetVector(ScaleFactor, Vector3.one * arrowScale);
        arrowMaterial.SetFloat(MaxMagnitudeProp, maxMagnitude);
        arrowMaterial.SetFloat(OpacityProp, opacity);
        arrowMaterial.SetVector(FieldSize, new Vector2(gridSize.x, gridSize.y));
        arrowMaterial.SetFloat(DisplayWidth, displayWidth);
        arrowMaterial.SetFloat(BaseStride, baseStride);
        arrowMaterial.SetFloat(DetailFade, detailFade);

        // Modern SRP-native indirect draw (replaces Graphics.DrawMeshInstancedIndirect). Still a one-frame persistent
        // draw targeting the given camera, so it's re-issued each render from RenderPipelineManager.beginCameraRendering.
        var rp = new RenderParams(arrowMaterial) {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e8f),
            camera = camera,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            lightProbeUsage = LightProbeUsage.Off,
        };
        Graphics.RenderMeshIndirect(rp, quad, argsBuffer);
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
