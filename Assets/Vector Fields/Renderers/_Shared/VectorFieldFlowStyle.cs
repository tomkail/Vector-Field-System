using UnityEngine;
using UnityEngine.Serialization;

namespace VectorFields {
    // Shared, script-authored styling for the flow visualizers (LIC, IBFV, Flow-Aligned). Held on each effect's component
    // and pushed to its material via a MaterialPropertyBlock (or straight onto the material for IBFV's present pass), so all
    // three expose the same controls and are driven from script rather than the material asset. Streaks are coloured by
    // flow amplitude via the gradient; the amplitude curve drives opacity. Bakes both to ramp textures on demand.
    //
    // Pairs with Shaders/VectorFieldFlowColor.cginc (the shader-side uniforms + helpers).
    [System.Serializable]
    public class VectorFieldFlowStyle {
        [Tooltip("Streak colour by flow amplitude (slow → fast). Pairs with the Amplitude Alpha curve below, which drives " +
            "opacity — so this gradient's ALPHA is ignored; only its RGB is used. " +
            "e.g. deep-blue→white (water), dark→red→orange→yellow (lava).")]
        [FormerlySerializedAs("colorGradient")]
        public Gradient amplitudeColor = BlackToWhite();

        [Tooltip("Streak opacity by flow amplitude (slow → fast) — the alpha companion to Amplitude Colour. Lets still " +
            "regions fade out. Default = fully opaque.")]
        public AnimationCurve amplitudeAlpha = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("Colour composited under the streaks. Alpha 0 = streaks over a transparent background (overlay the scene); " +
            "raise alpha for a solid backdrop (e.g. deep blue for water, dark rock for lava).")]
        public Color backgroundColor = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Contrast expansion of the streaks about mid-grey. 1 = none.")]
        [Range(0.1f, 6f)] public float contrast = 1f;

        [Tooltip("Gamma applied to the streaks. 1 = none.")]
        [Range(0.1f, 4f)] public float gamma = 1f;

        [Tooltip("Flow speed that maps to the top of the gradient / amplitude ramps.")]
        public float maxSpeed = 1f;

        [Tooltip("Overall opacity multiplier for the effect.")]
        [Range(0f, 1f)] public float opacity = 1f;

        const int RampResolution = 256;
        Texture2D colorRamp, amplitudeRamp;

        static readonly int ColorGradientId = Shader.PropertyToID("_ColorGradient");
        static readonly int AmplitudeRampId = Shader.PropertyToID("_AmplitudeRamp");
        static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
        static readonly int ContrastId  = Shader.PropertyToID("_Contrast");
        static readonly int GammaId     = Shader.PropertyToID("_Gamma");
        static readonly int MaxSpeedId  = Shader.PropertyToID("_MaxSpeed");
        static readonly int FlowAlphaId = Shader.PropertyToID("_FlowAlpha");

        // (Re)bake the gradient + curve into their ramp textures. Reuses the textures in place (only reallocates if missing).
        public void Bake() {
            if (amplitudeColor != null) VectorFieldUtils.CreateColorRampTextureFromGradient(amplitudeColor, RampResolution, ref colorRamp);
            if (amplitudeAlpha != null) VectorFieldUtils.CreateRampTextureFromAnimationCurve(amplitudeAlpha, RampResolution, ref amplitudeRamp);
        }

        // Push styling into a property block (LIC / Flow-Aligned, which are drawn straight on a mesh).
        public void Apply(MaterialPropertyBlock pb) {
            if (colorRamp == null || amplitudeRamp == null) Bake();
            if (colorRamp != null) pb.SetTexture(ColorGradientId, colorRamp);
            if (amplitudeRamp != null) pb.SetTexture(AmplitudeRampId, amplitudeRamp);
            WriteScalars(pb.SetFloat, pb.SetColor);
        }

        // Push styling straight onto a material (IBFV's present material, set once per frame alongside the buffer).
        public void Apply(Material m) {
            if (colorRamp == null || amplitudeRamp == null) Bake();
            if (colorRamp != null) m.SetTexture(ColorGradientId, colorRamp);
            if (amplitudeRamp != null) m.SetTexture(AmplitudeRampId, amplitudeRamp);
            WriteScalars(m.SetFloat, m.SetColor);
        }

        void WriteScalars(System.Action<int, float> setFloat, System.Action<int, Color> setColor) {
            setColor(BackgroundColorId, backgroundColor);
            setFloat(ContrastId, contrast);
            setFloat(GammaId, gamma);
            setFloat(MaxSpeedId, Mathf.Max(1e-4f, maxSpeed));
            setFloat(FlowAlphaId, opacity);
        }

        public void Dispose() {
            if (colorRamp != null) VectorFieldObjectUtils.DestroyAutomatic(colorRamp);
            if (amplitudeRamp != null) VectorFieldObjectUtils.DestroyAutomatic(amplitudeRamp);
            colorRamp = amplitudeRamp = null;
        }

        static Gradient BlackToWhite() {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
