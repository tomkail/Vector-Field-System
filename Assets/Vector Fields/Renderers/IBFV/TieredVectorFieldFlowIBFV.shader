// Tiered variant of the IBFV update pass: the same advect-and-inject feedback step, but the injection noise (texture +
// scale + amount) comes from N SPEED TIERS packed into a Texture2DArray, blended per pixel by the local flow speed
// (see VectorFieldSpeedTiers.cginc) — e.g. faint fine twinkle where the flow is slow, strong coarse twinkle where it's
// fast. Like the base update shader this needs a ping-pong feedback loop — drive it with TieredVectorFieldFlowIBFV.
// The present/colour pass is unchanged (VectorFieldFlowIBFVPresent.shader).
Shader "Vector Fields/IBFV/IBFV (Tiered)" {
    Properties {
        // Everything is set by TieredVectorFieldFlowIBFV on the blit material every frame — editing a material does nothing.
        [HideInInspector] _MainTex ("Previous Accumulation", 2D) = "black" {} // blit source = previous frame's buffer
        [HideInInspector] _FieldTex ("Field (RG vector)", 2D) = "gray" {}
        [HideInInspector] _NoiseArray ("Injection Noise (per tier)", 2DArray) = "white" {}
        [HideInInspector] _FlowStep ("Advection Step", Range(0,0.05)) = 0.008  // how far to advect per frame (uv units)
        [HideInInspector] _NoiseRate ("Noise Twinkle Rate", Float) = 1.5       // per-texel pulse cycles/sec
        [HideInInspector] _NoisePhase ("Time (driver-set)", Vector) = (0,0,0,0) // .x = elapsed seconds, drives the twinkle
        [HideInInspector] _MaxSpeed ("Max Speed", Float) = 1                   // top of the tier axis (driver-set)
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5           // Texture2DArray sampling
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldSpeedTiers.cginc"

            sampler2D _MainTex;   // previous accumulation (Graphics.Blit source)
            sampler2D _FieldTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
            UNITY_DECLARE_TEX2DARRAY(_NoiseArray);   // one noise slice per speed tier (R = value, G = twinkle phase)
            float _FlowStep;
            float _NoiseRate;
            float4 _NoisePhase;   // .x = elapsed seconds
            float _MaxSpeed;

            // Per-tier data, sorted ascending by speed; _TierCount valid entries (set by TieredVectorFieldFlowIBFV).
            float _TierSpeed[VF_MAX_TIERS];
            float _TierNoiseScale[VF_MAX_TIERS];
            float _TierNoiseAmount[VF_MAX_TIERS];
            int _TierCount;

            // One tier's twinkling noise value at `uv` (see IBFV.cginc for the twinkle rationale). Explicit LOD — the
            // slice index is per-pixel (non-uniform), where implicit derivatives are undefined; the array is mip-less.
            float TierNoise (int slice, float2 uv, float time) {
                float2 nz = UNITY_SAMPLE_TEX2DARRAY_LOD(_NoiseArray, float3(uv * _TierNoiseScale[slice], slice), 0).rg;
                float pulse = 0.5 + 0.5 * sin(6.2831853 * (time * _NoiseRate + nz.g));
                return 0.5 + (nz.r - 0.5) * pulse;
            }

            fixed4 frag (v2f_img i) : SV_Target {
                // Flow velocity. NOTE: +(rg-0.5) — OPPOSITE the static-texture visualizers (see IBFV.cginc).
                float2 vel = (tex2D(_FieldTex, i.uv).rg - 0.5);

                // Advect: pull this pixel from where the flow carried it FROM last frame (no global coordinate → no seam).
                float3 advected = tex2D(_MainTex, i.uv - vel * _FlowStep).rgb;

                // Find the two speed tiers bracketing this pixel and blend their injection noise + amount.
                float speed01 = saturate(length(vel) * 2.0 / max(_MaxSpeed, 1e-5));
                int lo, hi; float w;
                FindTierBracket(speed01, _TierSpeed, _TierCount, lo, hi, w);
                float n = TierNoise(lo, i.uv, _NoisePhase.x);
                float amount = _TierNoiseAmount[lo];
                if (hi > lo) {
                    n = lerp(n, TierNoise(hi, i.uv, _NoisePhase.x), w);
                    amount = lerp(amount, _TierNoiseAmount[hi], w);
                }

                return fixed4(lerp(advected, n.xxx, amount), 1.0);
            }
            ENDCG
        }
    }
}
