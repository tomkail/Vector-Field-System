using UnityEngine;

// DEMO — fade strategy 2: let a fluid SIMULATION carry and dissipate the effect. You paint brief impulses into a
// drawable "source" field; a SimulatedVectorFieldComponent uses it as its force input and its viscosity/advection
// spread and fade the motion naturally (vortices, wakes). You sample the SIM, not the source.
//
// Setup:
//  - A SimulatedVectorFieldComponent (the thing you sample/visualize). Lower viscosityDamp = faster fade.
//  - A DrawableVectorFieldComponent ("source") matching the sim's grid.
//  - This script with both assigned. It wires sim.forceField = source and stamps impulses into the source.
//
// The source is cleared one frame after each stamp so it acts as an impulse (a one-frame kick the sim absorbs), not
// a persistent force. For a steady effect (a fan/wind), don't clear — leave the source painted.
public class Demo_VectorFieldSimFade : MonoBehaviour {
    public SimulatedVectorFieldComponent sim;
    public DrawableVectorFieldComponent source;
    [Tooltip("Impulse radius in world units.")] public float worldRadius = 3f;
    [Range(0f, 1f)] public float strength = 1f;
    [Tooltip("Op id: repel (burst), attract (implosion), swirl (vortex), draw (directional).")] public string opId = "repel";

    bool _clearPending;

    void Start() {
        if (sim != null && source != null) {
            sim.forceField = source;     // the sim pulls continuous force from here each step
            sim.forceStrength = 1f;
        }
    }

    public void Burst() => Burst(transform.position);

    public void Burst(Vector3 worldPosition) {
        if (source == null) return;
        var brush = new VectorFieldBrush(VectorFieldBrushShape.Radial(0.5f),
                                         VectorFieldBrushOpRegistry.ById(opId), worldRadius, strength);
        source.Stamp(brush, worldPosition);
        _clearPending = true;
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.B)) Burst();
    }

    // Clear after the sim has consumed this frame's impulse, so it's a one-shot kick the sim then damps.
    void LateUpdate() {
        if (_clearPending) { source.Clear(); _clearPending = false; }
    }
}
