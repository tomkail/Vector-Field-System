// Tiered variant of LIC: the same line-integral convolution, but the noise look (texture + scale + step length +
// animation speed) comes from N SPEED TIERS packed into a Texture2DArray. Per pixel the two tiers bracketing the local
// flow speed are each convolved and blended (see VectorFieldSpeedTiers.cginc) — e.g. fine short hairs where the flow is
// slow, long coarse streaks where it's fast. NOTE: this runs the LIC march once per bracketing tier, so it costs up to
// 2x the single-tier shader.
//
// Wiring: use TieredLICTextureRenderer (drives the field texture, the tier array + params, and the shared Flow Style).
Shader "Vector Fields/LIC/LIC (Tiered)" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by the renderer
        [HideInInspector] _NoiseArray ("Noise Textures (per tier)", 2DArray) = "white" {}
        // Driven by TieredLICTextureRenderer via the property block every bind — editing them on the material does nothing.
        [HideInInspector] _StepCount ("Steps Per Side", Range(1,64)) = 32
        [HideInInspector] _Phase ("Flow Phase (anim)", Float) = 0        // shifts the along-streamline weighting so streaks animate
        // Shared styling (driven by TieredLICTextureRenderer via VectorFieldFlowStyle; defaults keep it visible bare).
        [HideInInspector] _ColorGradient ("Colour Ramp", 2D) = "white" {}
        [HideInInspector] _AmplitudeRamp ("Amplitude Ramp", 2D) = "white" {}
        [HideInInspector] _BackgroundColor ("Background", Color) = (0,0,0,0)
        [HideInInspector] _Contrast ("Contrast", Float) = 1
        [HideInInspector] _Gamma ("Gamma", Float) = 1
        [HideInInspector] _MaxSpeed ("Max Speed", Float) = 1
        [HideInInspector] _FlowAlpha ("Opacity", Float) = 1
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5           // Texture2DArray sampling
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldFlowColor.cginc"
            #include "../_Shared/VectorFieldSpeedTiers.cginc"

            sampler2D _MainTex;   // the vector field render texture (RG = vector*0.5 + 0.5)
            UNITY_DECLARE_TEX2DARRAY(_NoiseArray);   // one noise slice per speed tier
            float _StepCount, _Phase;
            sampler2D _ColorGradient;   // shared styling ramps (scalars live in VectorFieldFlowColor.cginc)
            sampler2D _AmplitudeRamp;

            // Per-tier data, sorted ascending by speed; _TierCount valid entries (set by TieredLICTextureRenderer).
            float _TierSpeed[VF_MAX_TIERS];
            float _TierNoiseScale[VF_MAX_TIERS];
            float _TierStepLength[VF_MAX_TIERS];
            float _TierAnimSpeed[VF_MAX_TIERS];
            int _TierCount;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // One direction of the LIC march for tier `slice` — LIC.cginc's integrate, retargeted at the noise array
            // (the array slice is why this can't call the sampler2D core directly; the maths is identical).
            void TierIntegrate (int slice, float2 uv, float dir, int steps, float stepLen, float noiseScale, float phase,
                                inout float acc, inout float wsum) {
                float2 p = uv;
                for (int s = 1; s <= steps; s++) {
                    // Signed flow, negated so streaks animate WITH the field on screen (convention shared with the water map).
                    // Explicit-LOD samples throughout: the march runs inside per-pixel (non-uniform) control flow, where
                    // implicit derivatives are undefined. Both sources are mip-less render textures, so LOD 0 is exact.
                    float2 v = -1.0 * (tex2Dlod(_MainTex, float4(p, 0, 0)).rg - 0.5);
                    float len = max(length(v), 1e-5);
                    p += dir * (v / len) * stepLen;                                     // unit-speed march → uniform spacing
                    float t = (float)s / steps;
                    float w = (0.5 + 0.5 * cos(6.2831853 * (t - dir * phase))) * (1.0 - t); // moving bump, tapered with distance
                    acc  += w * UNITY_SAMPLE_TEX2DARRAY_LOD(_NoiseArray, float3(p * noiseScale, slice), 0).r;
                    wsum += w;
                }
            }

            // Full LIC streak value at `uv` for one tier (both directions + the centre sample), using that tier's
            // noise slice, scale, step length, and animation speed.
            float TierLIC (int slice, float2 uv, int steps) {
                float noiseScale = _TierNoiseScale[slice];
                float stepLen = _TierStepLength[slice];
                float phase = frac(_Phase + _Time.y * _TierAnimSpeed[slice] * 0.1);
                float acc = UNITY_SAMPLE_TEX2DARRAY_LOD(_NoiseArray, float3(uv * noiseScale, slice), 0).r;   // centre sample
                float wsum = 1.0;
                TierIntegrate(slice, uv, +1.0, steps, stepLen, noiseScale, phase, acc, wsum);
                TierIntegrate(slice, uv, -1.0, steps, stepLen, noiseScale, phase, acc, wsum);
                return acc / max(wsum, 1e-5);
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 fieldVec = (tex2D(_MainTex, i.uv).rg - 0.5) * 2.0;   // field speed (positive decode)
                float speed01 = FlowSpeed01(fieldVec);

                // Find the two speed tiers bracketing this pixel and blend their convolutions (1 tier → lo==hi, w==0).
                int lo, hi; float w;
                FindTierBracket(speed01, _TierSpeed, _TierCount, lo, hi, w);
                int steps = (int)_StepCount;
                float lic = TierLIC(lo, i.uv, steps);
                if (hi > lo) lic = lerp(lic, TierLIC(hi, i.uv, steps), w);

                // Shared styling: contrast/gamma the streak into coverage, colour by SPEED, composite over background.
                float coverage = FlowContrastGamma(lic);
                float3 col = tex2D(_ColorGradient, float2(speed01, 0.5)).rgb;
                float ampAlpha = tex2D(_AmplitudeRamp, float2(speed01, 0.5)).r;
                return FlowCompose(col, coverage, ampAlpha);
            }
            ENDCG
        }
    }
}
