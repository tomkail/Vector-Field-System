using UnityEngine;

namespace VectorFields {
    // Animates a source vector field with a travelling gust wave (wind-ripple), in world space. Feed a static field
    // (Noise, Drawable, Polygon, …) as the source; this outputs the same flow modulated by a gust that sweeps along the
    // flow direction over time, so any consumer — grass, particles, the debug arrows — sees the animated field. Mirrors
    // how SimulatedVectorFieldComponent takes an input field and re-renders each frame; here the "sim" is just a wave.
    [AddComponentMenu("Vector Fields/Wave Vector Field")]
    public class WaveVectorFieldComponent : VectorFieldComponent {
        [Tooltip("The (usually static) field to animate. Its pattern is sampled across this field's extent.")]
        public VectorFieldComponent sourceField;
        [Tooltip("Gust wave frequency: waves per world unit along the flow direction.")]
        public float waveScale = 0.15f;
        [Tooltip("How fast gusts travel along the flow.")]
        public float waveSpeed = 2f;
        [Range(0f, 1f)]
        [Tooltip("0 = steady pass-through of the source; 1 = fully gusting waves.")]
        public float waveAmount = 0.7f;
        [Tooltip("Animate in edit mode too. Otherwise the wave only advances in Play mode.")]
        public bool animateInEditMode = false;

        float waveTime;

        bool Animating => Application.isPlaying || animateInEditMode;

        // Re-render every frame while animating (so the wave advances), and also whenever the source itself changes so a
        // steady (non-animating) wave field still tracks edits to its source. Otherwise fall back to dirty-only behaviour.
        public override void Update() {
            if (isActiveAndEnabled) {
                if (Animating) {
                    waveTime += Application.isPlaying ? Time.deltaTime : (1f / 60f);
                    SetDirty();
                } else if (sourceField != null && sourceField.IsDirty) {
                    SetDirty();
                }
            }
            base.Update();
        }

        protected override void RenderInternal() {
            EnsureHasValidRenderTexture();
            if (sourceField == null || sourceField == this) return;   // nothing to animate
            sourceField.EnsureUpToDate();                              // pull the source current first (like a group)
            if (sourceField.renderTexture == null) return;
            WaveVectorField.Dispatch(renderTexture, sourceField.renderTexture, GridSize, GridToWorldMatrix,
                                     waveScale, waveSpeed, waveAmount, waveTime);
        }

        protected override void CollectParameters(ref System.HashCode hash) {
            base.CollectParameters(ref hash);
            hash.Add(sourceField != null ? sourceField.GetEntityId() : default);
            hash.Add(waveScale);
            hash.Add(waveSpeed);
            hash.Add(waveAmount);
            hash.Add(animateInEditMode);
        }
    }
}
