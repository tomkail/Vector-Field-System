using UnityEngine;

// DEMO: paint a smooth flow trail into a drawable field as this object moves — e.g. a wake behind the player that
// pushes particles/agents along the path of travel. Uses the runtime stroke API, so the line is smooth and
// frame-rate independent (no dabbing).
//
// Setup: a DrawableVectorFieldComponent (+ GridRenderer) with a VectorFieldDecay on it (so the trail fades), and this
// script on the moving object with `field` pointing at it.
public class Demo_VectorFieldTrail : MonoBehaviour {
    public DrawableVectorFieldComponent field;
    [Tooltip("Brush radius in world units.")] public float worldRadius = 1.5f;
    [Range(0f, 1f)] public float strength = 1f;
    [Range(0f, 1f)] public float softness = 0.6f;

    VectorFieldStroke _stroke;

    void OnEnable() {
        if (field == null) return;
        VectorFieldDecay.WarnIfNoFadeStrategy(field, this);
        var brush = new VectorFieldBrush(VectorFieldBrushShape.Radial(softness),
                                         VectorFieldBrushOpRegistry.Draw, worldRadius, strength);
        _stroke = field.BeginStroke(brush);
    }

    void OnDisable() => _stroke?.End();

    void FixedUpdate() => _stroke?.To(transform.position);   // smooth, gap-free regardless of frame rate
}
