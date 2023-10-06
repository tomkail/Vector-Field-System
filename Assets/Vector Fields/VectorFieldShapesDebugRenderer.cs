/*
using UnityEngine;
using Shapes;

[ExecuteAlways]
public class VectorFieldShapesDebugRenderer : ImmediateModeShapeDrawer {
    public VectorFieldComponent vectorFieldManager;
    public bool colorize;
    public float maxMagnitude = 1;
    [Range(0,1)]
    public float opacity = 1;
    
    public override void DrawShapes( Camera cam ) {
        if(vectorFieldManager == null) return;
        if(opacity == 0) return;
        // if (cam != Camera.main) return;
        RandomX.BeginSeed(0);
        using (Draw.Command(cam)) {
            Draw.LineGeometry = LineGeometry.Billboard;
            foreach(var cell in vectorFieldManager.vectorField) {
                var worldPoint = vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(cell.point);
                Draw.Matrix = Matrix4x4.TRS(worldPoint,Quaternion.LookRotation(vectorFieldManager.planeNormal, (Vector3)cell.value), vectorFieldManager.gridRenderer.cellCenter.gridToWorldMatrix.lossyScale * cell.value.magnitude / maxMagnitude);

                if (colorize) {
                    float angle = 90-Vector2X.Degrees(cell.value);
                    Draw.Color = new HSLColor(angle, 1, 0.5f);
                    // Draw.Color = VectorFieldUtils.VectorToColor(cell.value.normalized, 1); 
                } else {
                    Draw.Color = Color.white;
                    
                }
                Draw.Opacity = Mathf.Clamp01(cell.value.magnitude / maxMagnitude) * opacity;
                DrawArrow(cell.value);
            }
        }
        RandomX.EndSeed();
    }

    void DrawArrow(Vector2 cellValue) {
        // Draw.Line(new Vector3(0,-0.5f, 0), new Vector3(0,0f, 0), 0.1f, LineEndCap.None);
        // Draw.Polygon(arrowPath);
        Draw.Cone(new Vector3(0,0, 0), Vector3.up, 0.175f, 0.5f);
        // Draw.Polygon(path, 1, PolylineJoins.Round, Color.white);
        // Draw.Line(new Vector3(0,0.45f, 0), new Vector3(-0.25f,0.125f,0), 0.1f);
        // Draw.Line(new Vector3(0,0.45f, 0), new Vector3(0.25f,0.125f,0), 0.1f);
    }
}
*/