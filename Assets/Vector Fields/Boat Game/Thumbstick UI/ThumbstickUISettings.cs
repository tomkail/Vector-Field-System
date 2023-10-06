using UnityEngine;

public class ThumbstickUISettings : ScriptableObject {
    public float showingAlphaFadeSpeed = 2f;
    public float thumbstickAlphaFadeInSpeed = 8;
    public float thumbstickAlphaFadeOutSpeed = 14;
    
    [Space]
    public float unheldScale = 0.9f;
    public float heldScaleSpringConstant = 20;
    
    [Space]
    public float thumbstickShowMagnitude = 0.2f;
    public float thumbstickMagnitudeSmoothTime = 0.2f;
    public float thumbstickAngleSmoothTime = 0.2f;

    [Space]
    public bool positionWithPivot = false;
    public float minPivot = 0.25f;
    public float maxPivot = 0f;

    [Space]
    public float snapbackSmoothTime;
    public float snapbackMaxSpeed;
}