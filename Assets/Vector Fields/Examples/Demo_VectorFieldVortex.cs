using UnityEngine;

// DEMO: a moving vortex — a whirlpool of circulating force that follows this object, so agents/particles get caught and
// swept around it (a spinning hazard, a tornado, a black-hole eddy). Continuously stamps the Swirl op at the object's
// position; pair with a VectorFieldDecay so the swirl trails and dissipates behind the mover instead of accumulating.
//
// Setup: a DrawableVectorFieldComponent (+ GridRenderer) with a VectorFieldDecay on it; this script on the moving
// vortex object with `field` assigned. Swap opId to "attract" for a pure sink, or "repel" for an outward fountain.
public class Demo_VectorFieldVortex : MonoBehaviour {
    public DrawableVectorFieldComponent field;
    [Tooltip("Vortex radius in world units.")] public float worldRadius = 5f;
    [Range(0f, 1f)] public float strength = 1f;
    [Tooltip("Op id: swirl (vortex), attract (sink), repel (fountain).")] public string opId = "swirl";

    VectorFieldBrush _brush;

    void OnEnable() {
        _brush = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.7f),
                                      VectorFieldBrushOpRegistry.ById(opId), worldRadius, strength);
        VectorFieldDecay.WarnIfNoFadeStrategy(field, this);
    }

    void FixedUpdate() {
        if (field == null) return;
        field.Stamp(_brush, transform.position);   // radial op: direction comes from the stamp centre
    }
}
