using UnityEngine;

public class VectorFieldStampAffector : VectorFieldAffector {
    // TODO - allow stamping from brush params, another vector field, or a vector field loaded from a texture
    public VectorFieldBrush brushParams;
    [AssetSaver, PreviewTexture(100)] public Texture2D texture;
    
    public Vector2Map brush;
    
    public int brushSize = 64;

    void OnValidate () {
        if(texture != null) ObjectX.DestroyAutomatic(texture);
        brush = VectorFieldBrush.CreateVectorField(brushParams, new Point(brushSize,brushSize));
        texture = VectorFieldUtils.VectorFieldToTexture(brush, 1);
    }

    public override Vector2 Evaluate(Vector3 position) {
        var localPosition = transform.InverseTransformPoint(position);
        if(localPosition.x < -1 || localPosition.x > 1 || localPosition.y < -1 || localPosition.y > 1) return Vector2.zero;
        var normalizedPosition = new Vector2((localPosition.x + 0.5f),( localPosition.y + 0.5f));
        var vector = brush.GetValueAtNormalizedPosition(normalizedPosition);
        var angle = Vector3X.SignedDegreesAgainstDirection(transform.up, vectorFieldManager.transform.up, vectorFieldManager.planeNormal);
        vector = Vector2X.Rotate(vector, angle);
        return vector * magnitude;
    }
	
    public override void UpdateVectorField (float deltaTime) {
        var points = vectorFieldManager.gridRenderer.GetPointsInWorldBounds(transform.GetBounds());
        foreach(var point in points) {
            var pointWorldPosition = vectorFieldManager.gridRenderer.cellCenter.GridToWorldPoint(point);
            Vector2 force = Evaluate(pointWorldPosition);
            vectorFieldManager.vectorField.SetValueAtGridPoint(point, vectorFieldManager.vectorField.GetValueAtGridPoint(point) + force);
        }
    }
	
    void OnDrawGizmos () {
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        GizmosX.DrawWireCircle(Vector3.zero, Quaternion.identity, 0.5f);
        Gizmos.matrix = m;
    }
}