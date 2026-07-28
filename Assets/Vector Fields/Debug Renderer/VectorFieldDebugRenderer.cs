
using UnityEngine;
using UnityEngine.Rendering;

namespace VectorFields {
    // How the arrow grid picks its density.
    //   Native  — one arrow per field cell (finest; count follows the field resolution).
    //   Fixed   — a fixed number of arrows along the long axis, independent of the camera (set via fixedResolution).
    //   Adaptive — decimated so on-screen spacing stays ~constant as the camera moves (uses targetSpacingPixels + maxArrows).
    public enum VectorFieldArrowResolutionMode { Native, Fixed, Adaptive }

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
                    _arrowTexture = Resources.Load<Texture2D>("Debug Arrow 5");
                return _arrowTexture;
            }
        }
        static Shader arrowShader => Shader.Find("Vector Fields/Debug/Instance Debug Renderer");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int FieldTex = Shader.PropertyToID("_FieldTex");
        static readonly int GridToWorldMatrix = Shader.PropertyToID("gridToWorldMatrix");
        static readonly int ScaleFactor = Shader.PropertyToID("scaleFactor");
        static readonly int MaxMagnitudeProp = Shader.PropertyToID("maxMagnitude");
        static readonly int OpacityProp = Shader.PropertyToID("_Opacity");
        static readonly int FieldSize = Shader.PropertyToID("fieldSize");
        static readonly int DisplayWidth = Shader.PropertyToID("displayWidth");
        static readonly int ArrowSpacing = Shader.PropertyToID("arrowSpacing");
        static readonly int DetailFade = Shader.PropertyToID("detailFade");
        static readonly int ColorModeProp = Shader.PropertyToID("colorMode");
        static readonly int FixedColorProp = Shader.PropertyToID("fixedColor");
        static readonly int LowColorProp = Shader.PropertyToID("lowColor");
        static readonly int HighColorProp = Shader.PropertyToID("highColor");
        static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");

        Material arrowMaterial;
        GraphicsBuffer argsBuffer;
        int bufferInstanceCount = -1;

        private bool disposed = false;

        public VectorFieldDebugRenderer() {
        }

        /// <summary>
        /// Draws the field as arrows. <paramref name="resolutionMode"/> picks the arrow density: Native (one per cell),
        /// Fixed (<paramref name="fixedResolution"/> arrows along the long axis, camera-independent), or Adaptive — decimated
        /// so on-screen spacing stays roughly constant as you zoom, where <paramref name="targetSpacingPixels"/> is the
        /// desired screen-space gap between arrows and <paramref name="maxArrows"/> caps how many arrows the long axis can
        /// show. In Adaptive the grid is laid out edge-to-edge with a power-of-two number of intervals (decoupled from the
        /// field cells, which it samples bilinearly), so coverage stays centred and balanced at every zoom level; the finest
        /// level lands at roughly the field's native resolution.
        ///
        /// <paramref name="gridToWorldOverride"/> replaces the field's own grid->world matrix — pass one to draw the arrows
        /// somewhere other than the field's placement (e.g. relative to a different transform). Leave null to follow the
        /// field. It drives both the arrow positions and the on-screen spacing (LOD), so the two stay consistent.
        /// </summary>
        public void Draw(VectorFieldComponent vectorFieldComponent, Camera camera, VectorFieldDebugAppearance appearance, VectorFieldArrowResolutionMode resolutionMode, float targetSpacingPixels, int maxArrows, int fixedResolution, Matrix4x4? gridToWorldOverride = null) {
            appearance ??= new VectorFieldDebugAppearance();
            // Sample the field straight off the GPU. No CPU readback / value buffer is needed, so the arrows always
            // reflect the live render texture without any CPU consumer registered.
            var fieldTexture = vectorFieldComponent.renderTexture;
            if (fieldTexture == null) return; // nothing has been rendered yet

            var gridSize = vectorFieldComponent.GridSize;
            var gridToWorldMatrix = gridToWorldOverride ?? vectorFieldComponent.GridToWorldMatrix;

            // The arrow grid is laid out edge-to-edge and decoupled from the field cells: per axis we draw a power-of-two
            // number of *intervals* spanning cell 0 to the far-edge cell, sampling the field (bilinearly) at each arrow.
            // Because the count is a power of two, the field span always divides evenly — first/last arrows sit exactly on
            // the two edges with balanced margins, and each coarser level is the exact even-index subset of the finer one
            // (so arrows never move as you zoom). This works for any field size, not just 2^k+1, at the cost of arrows no
            // longer sitting precisely on cell centres. detailFade cross-fades the odd-index "extra" arrows per axis.
            float arrowScale = 1f;
            int intervalsX, intervalsY;
            var detailFade = Vector2.one; // per-axis alpha for the extra (odd-index) arrows the finer level adds
            if (resolutionMode == VectorFieldArrowResolutionMode.Adaptive && camera != null) {
                float stride = ComputeStride(gridSize, gridToWorldMatrix, camera, targetSpacingPixels, maxArrows);
                arrowScale = stride; // size grows continuously with zoom, not in steps
                AxisLod(gridSize.x - 1, stride, out intervalsX, out detailFade.x);
                AxisLod(gridSize.y - 1, stride, out intervalsY, out detailFade.y);
            } else if (resolutionMode == VectorFieldArrowResolutionMode.Fixed) {
                // Fixed count along the long axis, camera-independent. The short axis uses the same cells-per-arrow stride so
                // arrows stay evenly spaced (square) regardless of field aspect. No LOD cross-fade (detailFade stays 1).
                int count = Mathf.Max(2, fixedResolution);
                int longSpan = Mathf.Max(1, Mathf.Max(gridSize.x, gridSize.y) - 1);
                float stride = (float)longSpan / (count - 1);   // cells per arrow
                arrowScale = stride;                            // size arrows to their spacing, like Adaptive
                intervalsX = gridSize.x > 1 ? Mathf.Max(1, Mathf.RoundToInt((gridSize.x - 1) / stride)) : 0;
                intervalsY = gridSize.y > 1 ? Mathf.Max(1, Mathf.RoundToInt((gridSize.y - 1) / stride)) : 0;
            } else {
                // Native: one arrow per cell, spanning the whole field. (Also the fallback for Adaptive with no camera.)
                intervalsX = Mathf.Max(0, gridSize.x - 1);
                intervalsY = Mathf.Max(0, gridSize.y - 1);
            }

            int displayWidth = intervalsX + 1;
            int displayHeight = intervalsY + 1;
            int instanceCount = displayWidth * displayHeight;
            // Edge-to-edge spacing in cells: index 0 -> cell 0, index `intervals` -> the far-edge cell.
            var arrowSpacing = new Vector2(
                intervalsX > 0 ? (float)(gridSize.x - 1) / intervalsX : 0f,
                intervalsY > 0 ? (float)(gridSize.y - 1) / intervalsY : 0f);

            // (Re)allocate the indirect-args buffer only when the arrow count changes.
            if (argsBuffer == null || bufferInstanceCount != instanceCount) {
                argsBuffer?.Dispose();
                argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                                                GraphicsBuffer.IndirectDrawIndexedArgs.size);
                argsBuffer.SetData(new[] { new GraphicsBuffer.IndirectDrawIndexedArgs {
                    indexCountPerInstance = quad.GetIndexCount(0), instanceCount = (uint)instanceCount } });
                bufferInstanceCount = instanceCount;
            }

            if (arrowMaterial == null) arrowMaterial = new Material(arrowShader);

            // The vertex shader derives each arrow's cell, value, and LOD fade from these uniforms + the instance id;
            // the fragment shader tints them per the appearance settings.
            arrowMaterial.SetTexture(MainTex, appearance.arrowTexture != null ? appearance.arrowTexture : arrowTexture);
            arrowMaterial.SetTexture(FieldTex, fieldTexture);
            arrowMaterial.SetMatrix(GridToWorldMatrix, gridToWorldMatrix);
            arrowMaterial.SetVector(ScaleFactor, Vector3.one * arrowScale);
            arrowMaterial.SetFloat(MaxMagnitudeProp, appearance.maxMagnitude);
            arrowMaterial.SetFloat(OpacityProp, appearance.opacity);
            arrowMaterial.SetFloat(ColorModeProp, (float)appearance.colorMode);
            arrowMaterial.SetColor(FixedColorProp, appearance.fixedColor);
            arrowMaterial.SetColor(LowColorProp, appearance.lowColor);
            arrowMaterial.SetColor(HighColorProp, appearance.highColor);
            // Invert Background mode composites as a destination invert (OneMinusDstColor) with premultiplied coverage from
            // the shader; every other mode is straight alpha-over. Blend state is fixed-function, so switch it here.
            bool invert = appearance.colorMode == VectorFieldDebugColorMode.InvertBackground;
            arrowMaterial.SetFloat(SrcBlendProp, (float)(invert ? BlendMode.OneMinusDstColor : BlendMode.SrcAlpha));
            arrowMaterial.SetFloat(DstBlendProp, (float)BlendMode.OneMinusSrcAlpha);
            arrowMaterial.SetVector(FieldSize, new Vector2(gridSize.x, gridSize.y));
            arrowMaterial.SetFloat(DisplayWidth, displayWidth);
            arrowMaterial.SetVector(ArrowSpacing, arrowSpacing);
            arrowMaterial.SetVector(DetailFade, detailFade);

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

        // Picks a power-of-two interval count for one axis at the current zoom, plus the cross-fade weight for the extra
        // (odd-index) arrows. The drawn count is the finer of the two power-of-two levels bracketing the target density;
        // its odd arrows fade out as the target drops toward the coarser level, at which point the coarser level (the exact
        // even-index subset) takes over with no movement. The finest level is the power of two closest to the native cell
        // count, so fully zoomed in the density is ~native (within ~1.4x) rather than a hard cap.
        static void AxisLod(int span, float stride, out int intervals, out float detailFade) {
            detailFade = 1f;
            if (span < 2) { intervals = Mathf.Max(0, span); return; }

            int maxIntervals = 1 << Mathf.RoundToInt(Mathf.Log(span, 2));        // finest level ~ native resolution
            float target = Mathf.Clamp(span / stride, 1f, maxIntervals);         // desired intervals at this zoom
            int coarse = 1 << Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(target, 2)), 0, 30);
            intervals = Mathf.Min(coarse * 2, maxIntervals);                     // draw the finer bracket
            if (intervals > coarse) detailFade = Mathf.Clamp01(target / coarse - 1f); // fade its odd "extra" arrows
        }

        // Continuous cells-per-arrow needed to keep arrows ~targetSpacingPixels apart on screen, never below 1
        // (native res) and never finer than the maxArrows cap on the long axis.
        static float ComputeStride(Vector2Int gridSize, Matrix4x4 gridToWorldMatrix, Camera camera, float targetSpacingPixels, int maxArrows) {
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
}
