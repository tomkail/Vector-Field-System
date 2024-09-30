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
