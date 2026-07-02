using UnityEngine;

// DEMO: a persistent directional "wind zone" — a steady patch of force that pushes agents in one direction (a draft, a
// conveyor, a current). Unlike the transient effects, this one does NOT fade: it's painted once and left, so the field
// should have NO VectorFieldDecay on it (or set `continuous` to re-stamp each frame so it survives a decaying field).
//
// Shows the directional Stamp overload: the Draw op paints `windDirection` into a soft round patch at this transform.
//
// Setup: a DrawableVectorFieldComponent (+ GridRenderer); this script on the wind-zone object with `field` assigned.
public class Demo_VectorFieldWind : MonoBehaviour {
    public DrawableVectorFieldComponent field;
    [Tooltip("Wind direction, degrees CCW from +X.")] public float windDirection = 0f;
    [Tooltip("Zone radius in world units.")] public float worldRadius = 6f;
    [Range(0f, 1f)] public float strength = 0.6f;
    [Range(0f, 1f)] [Tooltip("0 = hard-edged patch, 1 = very soft.")] public float softness = 0.8f;
    [Tooltip("Re-stamp every frame (needed if the field also has a decay/sim draining it).")]
    public bool continuous;

    void OnEnable() => Paint();

    void Update() {
        if (continuous) Paint();
    }

    void Paint() {
        if (field == null) return;
        float rad = windDirection * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        var brush = new VectorFieldBrush(BrushShape.Radial(softness),
                                         VectorFieldBrushOpRegistry.Draw, worldRadius, strength);
        field.Stamp(brush, transform.position, dir);
    }
}
