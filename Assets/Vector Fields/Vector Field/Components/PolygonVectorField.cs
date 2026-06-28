using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Creates a vector field from a polygon
// This isn't finished or working very well. In theory it could create vectors from the edges of the polygon, and those vectors could be rotated. If it uses fill might be another param
// I'd be interested in having this work using Unity's spline system too.
[ExecuteAlways]
public class PolygonVectorField : VectorFieldComponent {
    public PolygonRenderer polygonRenderer;
    // public DrawableVectorFieldComponent vectorFieldComponent => GetComponent<>()

    // This field is driven by an external PolygonRenderer, so track its transform and polygon shape (neither of
    // which routes through this component's OnValidate). In-place edits the JSON snapshot can't see still need a
    // manual SetDirty.
    PolygonRenderer lastPolygonRenderer;
    SerializableTransform lastPolygonTransform;
    string lastPolygonJson;
    protected override bool ParametersChanged() {
        bool changed = base.ParametersChanged();
        if (lastPolygonRenderer != polygonRenderer) { lastPolygonRenderer = polygonRenderer; changed = true; }
        if (polygonRenderer != null) {
            var t = new SerializableTransform(polygonRenderer.transform);
            if (lastPolygonTransform != t) { lastPolygonTransform = t; changed = true; }
            string json = JsonUtility.ToJson(polygonRenderer.polygon);
            if (lastPolygonJson != json) { lastPolygonJson = json; changed = true; }
        }
        return changed;
    }

    protected override void RenderInternal() {
        vectorField = new Vector2Map(gridRenderer.gridSize);
        foreach (var cell in vectorField) {
            // Get SVF
            var worldPoint = gridRenderer.cellCenter.GridToWorldPoint(cell.point);
            var polygonPoint = (Vector2)polygonRenderer.transform.InverseTransformPoint(worldPoint);
            var closestPoint = polygonRenderer.polygon.FindClosestPointOnPolygon(polygonPoint);
            var vector = closestPoint - polygonPoint;
            var worldVector = polygonRenderer.transform.TransformVector(vector);
            var vectorFieldVector = transform.InverseTransformVector(worldVector);

            vectorField[cell.index] = vectorFieldVector;
        }
    }
}
