using UnityEngine;
using VectorFields;

namespace CrowdFlow {
    /// <summary>
    /// Scrolls a <see cref="NoiseVectorFieldComponent"/>'s sample position over time so the noise field flows — the
    /// basis of the wind. The noise component re-renders whenever its sample position changes, so a particle system
    /// (or any consumer) driven by the field sees a moving current.
    /// </summary>
    [RequireComponent(typeof(NoiseVectorFieldComponent))]
    public class ScrollNoiseField : MonoBehaviour {
        [Tooltip("How fast the noise sample position drifts (units/sec) — the apparent wind velocity.")]
        public Vector3 scrollSpeed = new Vector3(0.6f, 0f, 0.2f);

        NoiseVectorFieldComponent _field;

        void Awake() => _field = GetComponent<NoiseVectorFieldComponent>();

        void Update() {
            if (_field == null) return;
            _field.noiseSampler.position += scrollSpeed * Time.deltaTime;
        }
    }
}
