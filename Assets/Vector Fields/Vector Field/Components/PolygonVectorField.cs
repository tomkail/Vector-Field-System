using UnityEngine;

// Editor-facing wrapper around the code-callable PolygonVectorFieldGenerator: every cell points toward (or away
// from) the nearest polygon edge. Holds the settings, detects changes, and feeds the grid/transform into the core.
// I'd be interested in having this work using Unity's spline system too.
[ExecuteAlways]
[AddComponentMenu("Vector Fields/Polygon Vector Field")]
public class PolygonVectorField : VectorFieldComponent {
    public PolygonRenderer polygonRenderer;

    // Which sides of the shape get a vector. Drawn as Inside/Outside toggle buttons (by the custom inspector); enable both for the whole grid.
    public PolygonVectorFieldGenerator.Sides sides = PolygonVectorFieldGenerator.Sides.Outside;

    // By default inside and outside flow the same way (outward, away from the shape) — continuous across the
    // boundary. Reverse one side to make the field diverge from (FlipInside) or converge on (FlipOutside) the outline.
    public PolygonVectorFieldGenerator.BoundaryFlip boundaryFlip = PolygonVectorFieldGenerator.BoundaryFlip.None;

    // Distance from the edge (in polygon-local units) over which the vector fades from full strength (at the
    // edge) to zero. Inner controls the inside region, outer the outside region. 0 = no falloff, constant
    // strength throughout that region.
    [Min(0)] public float innerFalloff = 1f;
    [Min(0)] public float outerFalloff = 1f;

    // Rotates each vector around the plane normal, like NoiseVectorFieldComponent.vortexAngle. 0 points straight
    // toward the nearest edge; 90 makes the field circulate around the shape; 180 points it away from the edge.
    public float angle = 0f;

    // This field is driven by an external PolygonRenderer, so track its transform and polygon shape (neither of
    // which routes through this component's OnValidate). In-place edits the JSON snapshot can't see still need a
    // manual SetDirty.
    protected override void CollectParameters(ref System.HashCode hash) {
        base.CollectParameters(ref hash);
        hash.Add(polygonRenderer != null ? polygonRenderer.GetHashCode() : 0);
        if (polygonRenderer != null) {
            hash.Add(polygonRenderer.transform.localToWorldMatrix);
            hash.Add(JsonUtility.ToJson(polygonRenderer.polygon));
        }
        hash.Add((int)sides);
        hash.Add((int)boundaryFlip);
        hash.Add(innerFalloff);
        hash.Add(outerFalloff);
        hash.Add(angle);
    }

    // GPU buffer holding the polygon's vertices for the compute dispatch. Owned here (created/grown by the generator,
    // released on disable) so its lifetime is explicit, like the base render texture.
    ComputeBuffer vertexBuffer;

    protected override void RenderInternal() {
        EnsureHasValidRenderTexture();

        // Driven by an external PolygonRenderer that may be unassigned (or have no polygon yet); a null vertex array
        // makes the generator write a defined zero field.
        var polygon = polygonRenderer ? polygonRenderer.polygon : null;
        var vertices = polygon?.vertices;

        // gridToPolygonLocal: grid cell -> world -> polygon-local point. polygonToFieldVector: polygon-local vector ->
        // world -> this field's local space (rotation/scale part). Folded on the CPU so the shader does no redundant
        // per-cell matrix work. Only meaningful when there's a polygon; identity otherwise (unused on the zero path).
        Matrix4x4 gridToPolygonLocal = Matrix4x4.identity, polygonToFieldVector = Matrix4x4.identity;
        if (vertices != null) {
            gridToPolygonLocal = polygonRenderer.transform.worldToLocalMatrix * GridToWorldMatrix;
            polygonToFieldVector = transform.worldToLocalMatrix * polygonRenderer.transform.localToWorldMatrix;
        }

        // Unit strength: the base applies `magnitude` (and cookie) as an output transform in Render(), so passing
        // `magnitude` here would double-apply it.
        PolygonVectorFieldGenerator.Dispatch(renderTexture, ref vertexBuffer, GridSize, vertices,
            gridToPolygonLocal, polygonToFieldVector,
            sides, boundaryFlip, innerFalloff, outerFalloff, angle, 1f);
    }

    protected override void OnDisable() {
        base.OnDisable();
        // Render textures aren't GC'd and ComputeBuffers must be released explicitly; rebuilt on the next dispatch.
        vertexBuffer?.Release();
        vertexBuffer = null;
    }
}
