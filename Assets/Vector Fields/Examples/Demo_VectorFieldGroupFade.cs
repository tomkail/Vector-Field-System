using UnityEngine;

// DEMO — fade strategy 3: per-effect fade via GROUP LAYERS. Each effect gets its own drawable field; the group blends
// them, and you fade each effect's layer strength on its own timeline/curve, then recycle it. Gives precise,
// independent fades (e.g. a beam that fades over exactly 0.5s) at the cost of one blended layer per live effect.
//
// Setup:
//  - A GroupVectorFieldComponent (the thing you sample/visualize).
//  - A small POOL of DrawableVectorFieldComponent children under the group (they auto-register as layers). Assign
//    them to `effectFields` (e.g. 4–8). All should share the group's grid.
//  - This script with `group` and `effectFields` assigned.
public class Demo_VectorFieldGroupFade : MonoBehaviour {
    public GroupVectorFieldComponent group;
    public DrawableVectorFieldComponent[] effectFields;   // pooled children of the group
    [Tooltip("Seconds for an effect to fade from full to gone.")] public float lifetime = 1f;
    [Tooltip("Strength over normalized lifetime (1 = just spawned, 0 = expired).")]
    public AnimationCurve fadeCurve = AnimationCurve.Linear(0, 1, 1, 0);
    public float worldRadius = 4f;
    [Range(0f, 1f)] public float strength = 1f;
    public string opId = "repel";

    float[] _age;                                        // per pooled field; <0 = free
    GroupVectorFieldComponent.VectorFieldLayer[] _layer; // slot -> its group layer, resolved once (null if not a layer)

    void Awake() {
        int n = effectFields != null ? effectFields.Length : 0;
        _age = new float[n];
        _layer = new GroupVectorFieldComponent.VectorFieldLayer[n];
        for (int i = 0; i < n; i++) {
            _age[i] = -1f;
            _layer[i] = group != null && effectFields[i] != null
                ? group.layers.Find(l => l.component == effectFields[i]) : null;   // resolved once, not per frame
            SetLayerStrength(i, 0f);
        }
    }

    public void Burst() => Burst(transform.position);

    public void Burst(Vector3 worldPosition) {
        int slot = FreeSlot();
        if (slot < 0) return;   // pool exhausted — grow effectFields or raise lifetime headroom
        var f = effectFields[slot];
        f.Clear();
        var brush = new VectorFieldBrush(BrushShape.Radial(0.5f),
                                         VectorFieldBrushOpRegistry.ById(opId), worldRadius, strength);
        f.Stamp(brush, worldPosition);
        _age[slot] = 0f;
        SetLayerStrength(slot, fadeCurve.Evaluate(0f));
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.B)) Burst();

        for (int i = 0; i < _age.Length; i++) {
            if (_age[i] < 0f) continue;
            _age[i] += Time.deltaTime;
            float k = Mathf.Clamp01(_age[i] / Mathf.Max(0.01f, lifetime));
            if (k >= 1f) {                       // expired: recycle the slot
                SetLayerStrength(i, 0f);
                effectFields[i].Clear();
                _age[i] = -1f;
            } else {
                SetLayerStrength(i, fadeCurve.Evaluate(k));
            }
        }
        if (group != null) group.SetDirty();      // re-blend with the new layer strengths
    }

    int FreeSlot() {
        for (int i = 0; i < _age.Length; i++) if (_age[i] < 0f) return i;
        return -1;
    }

    void SetLayerStrength(int slot, float s) {
        if (_layer[slot] != null) _layer[slot].strength = s;
    }
}
