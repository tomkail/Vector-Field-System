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

    int _impulseFrame = -1;   // frame the current impulse was painted; -1 = none pending

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
        _impulseFrame = Time.frameCount;
    }

    void Update() {
        // Clear the impulse once we're in a LATER frame than it was painted, so it stays up for a full frame no matter
        // whether the sim's Update (or an external Burst caller) runs before or after this one. Keying off the frame
        // number — rather than a same-frame LateUpdate — means the sim reliably reads the impulse at least once, and a
        // fresh Burst earlier in this same frame isn't wiped by mistake.
        // Caveat to validate in-editor: the sim advances by a time accumulator, so a frame with several sub-steps
        // absorbs the impulse more than once — scale `strength` to taste, or gate this on the sim actually stepping.
        if (_impulseFrame >= 0 && Time.frameCount > _impulseFrame) { source.Clear(); _impulseFrame = -1; }

        if (Input.GetKeyDown(KeyCode.B)) Burst();
    }
}
