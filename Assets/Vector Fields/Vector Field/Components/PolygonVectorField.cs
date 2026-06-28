using UnityEngine;

// Editor-facing wrapper around the code-callable PolygonVectorFieldGenerator: every cell points toward (or away
// from) the nearest polygon edge. Holds the settings, detects changes, and feeds the grid/transform into the core.
// I'd be interested in having this work using Unity's spline system too.
[ExecuteAlways]
public class PolygonVectorField : VectorFieldComponent {
    public PolygonRenderer polygonRenderer;

    // Which sides of the shape get a vector. Drawn as Inside/Outside toggle buttons; enable both for the whole grid.
    [EnumFlagsButtonGroup] public PolygonVectorFieldGenerator.Sides sides = PolygonVectorFieldGenerator.Sides.Outside;

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
    PolygonRenderer lastPolygonRenderer;
    SerializableTransform lastPolygonTransform;
    string lastPolygonJson;
    PolygonVectorFieldGenerator.Sides lastSides;
    PolygonVectorFieldGenerator.BoundaryFlip lastBoundaryFlip;
    float lastInnerFalloff = float.NaN;
    float lastOuterFalloff = float.NaN;
    float lastAngle = float.NaN;
    protected override bool ParametersChanged() {
        bool changed = base.ParametersChanged();
        if (lastPolygonRenderer != polygonRenderer) { lastPolygonRenderer = polygonRenderer; changed = true; }
        if (polygonRenderer != null) {
            var t = new SerializableTransform(polygonRenderer.transform);
            if (lastPolygonTransform != t) { lastPolygonTransform = t; changed = true; }
            string json = JsonUtility.ToJson(polygonRenderer.polygon);
            if (lastPolygonJson != json) { lastPolygonJson = json; changed = true; }
        }
        if (lastSides != sides) { lastSides = sides; changed = true; }
        if (lastBoundaryFlip != boundaryFlip) { lastBoundaryFlip = boundaryFlip; changed = true; }
        if (lastInnerFalloff != innerFalloff) { lastInnerFalloff = innerFalloff; changed = true; }
        if (lastOuterFalloff != outerFalloff) { lastOuterFalloff = outerFalloff; changed = true; }
        if (lastAngle != angle) { lastAngle = angle; changed = true; }
        return changed;
    }

    protected override void RenderInternal() {
        vectorField = new Vector2Map(gridRenderer.gridSize);
        // Driven by an external PolygonRenderer that may be unassigned (or have no polygon yet); leave the field zeroed.
        if (polygonRenderer == null || polygonRenderer.polygon == null) return;

        PolygonVectorFieldGenerator.Generate(
            vectorField,
            p => gridRenderer.cellCenter.GridToWorldPoint(p),
            polygonRenderer.polygon,
            polygonRenderer.transform.worldToLocalMatrix,
            polygonRenderer.transform.localToWorldMatrix,
            transform.worldToLocalMatrix,
            sides, boundaryFlip, innerFalloff, outerFalloff, angle, magnitude);

        // This field is computed on the CPU, but the draw path, GPU group blend, and shader visualizer all sample
        // renderTexture now (not the CPU vectorField), so a CPU-only component would render to nothing. Encode the
        // result into renderTexture so it participates everywhere.
        WriteVectorFieldToRenderTexture();
    }
}
