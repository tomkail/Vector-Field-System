using UnityEngine;

// DEMO: a directional beam attack that traces a swept line of force from an origin to a target, then detonates an
// outward burst at the far end — the "trace a line then explode" case from Brush/RUNTIME_PAINTING_SPEC.md. The beam
// uses PaintLine (a smooth swept line whose painted direction follows the path), the blast uses a Repel Stamp.
//
// Setup: a DrawableVectorFieldComponent (+ GridRenderer) with a VectorFieldDecay on it (so the beam/blast fade); this
// script anywhere with `field`, `origin`, and `target` assigned. Call Fire() from gameplay, or press F in play mode.
public class Demo_VectorFieldBeam : MonoBehaviour {
    public DrawableVectorFieldComponent field;
    public Transform origin;
    public Transform target;
    [Tooltip("Beam width in world units.")] public float beamWidth = 1.5f;
    [Tooltip("Blast radius in world units.")] public float blastRadius = 4f;
    [Range(0f, 1f)] public float strength = 1f;

    void Start() => VectorFieldDecay.WarnIfNoFadeStrategy(field, this);

    public void Fire() {
        if (field == null || origin == null || target == null) return;

        // Beam: Draw sets the field toward the swept path direction (origin -> target), so it shoves along the beam.
        var beam = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.5f),
                                        VectorFieldBrushOpRegistry.Draw, beamWidth, strength);
        field.PaintLine(beam, origin.position, target.position);

        // Blast: Repel radiates outward from the impact point.
        var blast = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.5f),
                                         VectorFieldBrushOpRegistry.Repel, blastRadius, strength);
        field.Stamp(blast, target.position);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.F)) Fire();   // demo trigger; replace with your gameplay event
    }
}
