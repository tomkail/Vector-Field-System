using UnityEngine;

[System.Serializable]
public class VectorFieldBrush {
    public AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0,1,1,0);
    
    public enum ForceEmitterType {
        Directional,
        Spot
    }
    public ForceEmitterType forceType = ForceEmitterType.Directional;

    public float directionalAngle;
    public float vortexAngle;

    public static AnimationCurve CreateCurveWithHardness(float brushHardness) {
        return AnimationCurve.Linear(1f-brushHardness, 1, 0, 0);
    }

    public static Vector2Map CreateVectorField(VectorFieldBrush brush, Point size) {
        var vectorField = new Vector2Map(size);
        
        // var sizeReciprocal = new Vector2(1f / (size.x-1f), 1f / (size.y-1f));
        var sizeReciprocal = new Vector2(1f / (size.x), 1f / (size.y));
        var half = Vector2.one * 0.5f;
        foreach(TypeMapCellInfo<Vector2> cellInfo in vectorField) {
            var normalizedPosition = ((Vector2) cellInfo.point + Vector2.one * 0.5f) * sizeReciprocal;
            // var normalizedPosition = (Vector2) cellInfo.point * sizeReciprocal;
            Vector2 force = Vector2.zero;

            float strength = brush.falloffCurve.Evaluate(Mathf.InverseLerp(0,0.5f, (normalizedPosition - half).magnitude));
            if(brush.forceType == ForceEmitterType.Directional) {
                force = strength * new Vector2(Mathf.Sin(brush.directionalAngle * Mathf.Deg2Rad), Mathf.Cos(brush.directionalAngle * Mathf.Deg2Rad));
            } else if(brush.forceType == ForceEmitterType.Spot) {
                Vector2 direction = (normalizedPosition - half).normalized;
                force = direction.normalized * strength;
                if(brush.vortexAngle != 0) force = Rotate(force, brush.vortexAngle);
            } else {
                Debug.LogWarning("Unknown force type: " + brush.forceType);
            }
            vectorField[cellInfo.index] = force;
        }

        return vectorField;
        
        static Vector2 Rotate (Vector2 v, float degrees) {
            degrees *= Mathf.Deg2Rad;
            float sin = Mathf.Sin( -degrees );
            float cos = Mathf.Cos( -degrees );
            float tx = v.x;
            float ty = v.y;
            v.x = (cos * tx) - (sin * ty);
            v.y = (cos * ty) + (sin * tx);
            return v;
        }
    }
}

