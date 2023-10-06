using UnityEngine;

public class ShipSettings : ScriptableObject {
    public DragParams dragParams;
    public AnimationCurve thrustAccelerationOverSpeed;
    public float thrustAcceleration = 2;
    [Space]
    public float angleChangeSpeed = 45;
}