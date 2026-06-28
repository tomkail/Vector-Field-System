using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Creates a vector field from a polygon: every cell points toward (or away from) the nearest polygon edge.
// I'd be interested in having this work using Unity's spline system too.
[ExecuteAlways]
public class PolygonVectorField : VectorFieldComponent {
    public PolygonRenderer polygonRenderer;
    // public DrawableVectorFieldComponent vectorFieldComponent => GetComponent<>()

    // Which sides of the shape get a vector. Drawn as Inside/Outside toggle buttons; enable both for the whole grid.
    [EnumFlagsButtonGroup] public Sides sides = Sides.Outside;
    [System.Flags]
    public enum Sides {
        None = 0,
        Inside = 1 << 0,
        Outside = 1 << 1,
    }

    // By default inside and outside flow the same way (outward, away from the shape) — continuous across the
    // boundary. Reverse one side to make the field diverge from (FlipInside) or converge on (FlipOutside) the outline.
    public BoundaryFlip boundaryFlip = BoundaryFlip.None;
    public enum BoundaryFlip {
        None,
        FlipInside,
        FlipOutside,
    }

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
    Sides lastSides;
    BoundaryFlip lastBoundaryFlip;
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

        var polygon = polygonRenderer.polygon;
        bool wantInside = (sides & Sides.Inside) != 0;
        bool wantOutside = (sides & Sides.Outside) != 0;
        // Precompute the rotation (around the plane normal) applied to every vector.
        float rad = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

        foreach (var cell in vectorField) {
            var worldPoint = gridRenderer.cellCenter.GridToWorldPoint(cell.point);
            var polygonPoint = (Vector2)polygonRenderer.transform.InverseTransformPoint(worldPoint);

            // Restrict to the chosen side(s) of the shape; cells on an inactive side stay zeroed.
            bool inside = polygon.ContainsPoint(polygonPoint);
            if (inside ? !wantInside : !wantOutside) {
                vectorField[cell.index] = Vector2.zero;
                continue;
            }

            var closestPoint = polygon.FindClosestPointOnPolygon(polygonPoint);
            var toEdge = closestPoint - polygonPoint; // points toward the nearest edge
            float distance = toEdge.magnitude;
            // Outward (away from the shape) is continuous across the boundary: inside points toward its nearest edge,
            // outside points away from it, so both sides flow the same way by default.
            Vector2 outward = inside ? toEdge : -toEdge;
            Vector2 direction = distance > 1e-5f ? outward / distance : Vector2.zero;

            // Reverse one side to converge on / diverge from the outline.
            if ((inside && boundaryFlip == BoundaryFlip.FlipInside) || (!inside && boundaryFlip == BoundaryFlip.FlipOutside))
                direction = -direction;
            // Rotate around the plane normal (2D rotation in polygon space).
            if (angle != 0f) direction = new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);

            // Full strength at the edge, fading to zero `falloff` units away (0 = constant strength). Inside and
            // outside regions use their own falloff distance.
            float falloff = inside ? innerFalloff : outerFalloff;
            float strength = falloff > 0f ? Mathf.Clamp01(1f - distance / falloff) : 1f;
            var vector = direction * (strength * magnitude);

            var worldVector = polygonRenderer.transform.TransformVector(vector);
            var vectorFieldVector = transform.InverseTransformVector(worldVector);

            vectorField[cell.index] = vectorFieldVector;
        }

        // This field is computed on the CPU, but the draw path, GPU group blend, and shader visualizer all sample
        // renderTexture now (not the CPU vectorField), so a CPU-only component would render to nothing. Encode the
        // result into renderTexture so it participates everywhere.
        WriteVectorFieldToRenderTexture();
    }
}
