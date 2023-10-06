using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
