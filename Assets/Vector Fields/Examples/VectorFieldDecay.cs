using UnityEngine;

// Fades a drawable vector field toward zero over time — multiplicative decay-in-place. Attach to the same GameObject
// as a DrawableVectorFieldComponent. The cheapest, most general fade: many transient effects can paint into one
// decaying field and it quietly drains.
//
// Alternatives for other needs:
//  - Natural dissipation (spread + swirl): feed a drawable field into a SimulatedVectorFieldComponent.forceField and
//    let its viscosityDamp/advection carry and decay it.
//  - Per-effect fade curves: give each effect its own drawable field as a GroupVectorFieldComponent layer and animate
//    that layer's `strength` down over the effect's lifetime.
[RequireComponent(typeof(DrawableVectorFieldComponent))]
public class VectorFieldDecay : MonoBehaviour {
    [Tooltip("Fraction of the field's strength remaining after one second. 0 = fades instantly, 1 = never fades.")]
    [Range(0f, 1f)] public float retainPerSecond = 0.1f;

    [Tooltip("Cell magnitudes below this snap to zero so the field goes fully quiet instead of trailing forever.")]
    public float zeroThreshold = 0.001f;

    DrawableVectorFieldComponent _field;

    void Awake() => _field = GetComponent<DrawableVectorFieldComponent>();

    void Update() {
        var values = _field.PaintField.values;
        float k = Mathf.Pow(Mathf.Clamp01(retainPerSecond), Time.deltaTime);
        float zeroSqr = zeroThreshold * zeroThreshold;

        bool changed = false;
        for (int i = 0; i < values.Length; i++) {
            if (values[i].sqrMagnitude == 0f) continue;          // already quiet
            Vector2 next = values[i] * k;
            if (next.sqrMagnitude < zeroSqr) next = Vector2.zero;
            values[i] = next;
            changed = true;
        }
        if (changed) _field.SetDirty();                          // whole field changed -> re-upload
    }
}
