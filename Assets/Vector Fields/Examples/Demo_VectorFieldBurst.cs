using UnityEngine;

// DEMO: stamp an outward burst into a drawable field — e.g. an explosion that shoves everything away from the impact
// point, then fades. Call Burst() from gameplay (on impact, on death, etc.).
//
// Setup: a DrawableVectorFieldComponent (+ GridRenderer) with a VectorFieldDecay on it; this script anywhere with
// `field` pointing at it. Uses the Repel op (direction radiates from the brush centre). Swap opId to "attract" for an
// implosion / vacuum, or "swirl" for a vortex.
public class Demo_VectorFieldBurst : MonoBehaviour {
    public DrawableVectorFieldComponent field;
    [Tooltip("Burst radius in world units.")] public float worldRadius = 4f;
    [Range(0f, 1f)] public float strength = 1f;
    [Tooltip("Op id: repel (burst), attract (implosion), or swirl (vortex).")] public string opId = "repel";

    public void Burst() => Burst(transform.position);

    public void Burst(Vector3 worldPosition) {
        if (field == null) return;
        var brush = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.5f),
                                         VectorFieldBrushOpRegistry.ById(opId), worldRadius, strength);
        field.Stamp(brush, worldPosition);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.B)) Burst();   // demo trigger; replace with your gameplay event
    }
}
