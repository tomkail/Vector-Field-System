// Ping-pong flow map (Valve "Water Flow" / van Wijk): scrolls a texture along the vector field, per-pixel.
//
// The naive `uv += flow * time` smears infinitely wherever neighbouring cells disagree on direction. Instead we push
// the UVs by a *bounded* sawtooth phase (frac time) that resets periodically, and run TWO copies offset by half a
// cycle, cross-fading between them so each copy's reset is hidden behind the other's mid-cycle. The result flows
// forever without tearing.
//
// N-SPEED-TIERS: several water looks (texture + tiling + flow params) are keyed to positions on the normalised speed
// axis and packed into a Texture2DArray. Per pixel we find the two tiers bracketing the local speed (see
// VectorFieldSpeedTiers.cginc) and blend them — e.g. calm water where the flow is slow, choppy where it's fast.
//
// Wiring: use TieredFlowMapRenderer (drives the field texture, the tier array + params, and the shared Flow Style).
Shader "Vector Fields/Flow Map/Flow Map (Tiered)" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by the renderer
        [HideInInspector] _WaterArray ("Water Textures (per tier)", 2DArray) = "white" {}
        // Driven by TieredFlowMapRenderer via the property block every bind — editing them on the material does nothing.
        [HideInInspector] _DualScale ("Second Layer (breaks up tiling)", Float) = 1
        [HideInInspector] _DetailTiling ("Detail Tiling x", Float) = 2.17
        [HideInInspector] _DetailSpeed ("Detail Speed x", Float) = 1.7
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        // Shared styling (driven by TieredFlowMapRenderer via VectorFieldFlowStyle; defaults keep the water untouched).
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
        ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5           // Texture2DArray sampling
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldFlowColor.cginc"
            #include "../_Shared/VectorFieldSpeedTiers.cginc"
            #include "FlowMap.cginc"   // core ping-pong phase math (shared with the single-tier variant)

            sampler2D _MainTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
            UNITY_DECLARE_TEX2DARRAY(_WaterArray);   // one slice per speed tier
            float _DetailTiling, _DetailSpeed, _DualScale;
            fixed4 _Color;
            sampler2D _ColorGradient;   // shared styling ramps (scalars live in VectorFieldFlowColor.cginc)
            sampler2D _AmplitudeRamp;

            // Per-tier data, sorted ascending by speed; _TierCount valid entries (set by TieredFlowMapRenderer).
            float _TierSpeed[VF_MAX_TIERS];
            float _TierTiling[VF_MAX_TIERS];
            float _TierStrength[VF_MAX_TIERS];
            float _TierFlowSpeed[VF_MAX_TIERS];
            int _TierCount;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // One ping-pong flow-map sample of tier `slice`, using the shared FlowMapPhases core (Texture2DArray
            // variant — the array slice is why this can't call the sampler2D FlowMapSample directly).
            fixed4 flowSample (int slice, float2 vel, float2 tileUv, float flowStrength, float flowSpeed, float speedMul) {
                float p0, p1, blend;
                FlowMapPhases(flowSpeed * speedMul, _Time.y, p0, p1, blend);
                fixed4 c0 = UNITY_SAMPLE_TEX2DARRAY(_WaterArray, float3(tileUv - vel * flowStrength * p0, slice));
                fixed4 c1 = UNITY_SAMPLE_TEX2DARRAY(_WaterArray, float3(tileUv - vel * flowStrength * p1, slice));
                return lerp(c0, c1, blend);
            }

            // One tier's full water look: primary layer + the optional detail layer (shared detail scale/speed).
            fixed4 tierFlow (int slice, float2 uv, float2 vel, float tiling, float flowStrength, float flowSpeed) {
                fixed4 c = flowSample(slice, vel, uv * tiling, flowStrength, flowSpeed, 1.0);
                if (_DualScale > 0.5) {
                    fixed4 d = flowSample(slice, vel, uv * tiling * _DetailTiling, flowStrength, flowSpeed, _DetailSpeed);
                    c = (c + d) * 0.5;
                }
                return c;
            }

            fixed4 frag (v2f i) : SV_Target {
                // Decode the field. Positive sign so the water scrolls WITH the field on screen (a (1,0) cell flows +x).
                float2 vel = (tex2D(_MainTex, i.uv).rg - 0.5);
                float speed01 = FlowSpeed01(vel * 2.0);   // vel is the half-magnitude decode, so *2 = full magnitude

                // Find the two speed tiers bracketing this pixel and blend them (1 tier → lo==hi, w==0).
                int lo, hi; float w;
                FindTierBracket(speed01, _TierSpeed, _TierCount, lo, hi, w);
                fixed4 low  = tierFlow(lo, i.uv, vel, _TierTiling[lo], _TierStrength[lo], _TierFlowSpeed[lo]);
                fixed4 high = tierFlow(hi, i.uv, vel, _TierTiling[hi], _TierStrength[hi], _TierFlowSpeed[hi]);
                float3 rgb = lerp(low, high, w).rgb * _Color.rgb;

                // Shared styling: colourmap by SPEED (gradient multiplies the water — white ramp = no tint), then
                // contrast/gamma, then composite over the background at the shared opacity. Water fully covers → coverage 1.
                rgb *= tex2D(_ColorGradient, float2(speed01, 0.5)).rgb;
                rgb = saturate((rgb - 0.5) * _Contrast + 0.5);
                rgb = pow(saturate(rgb), _Gamma);
                float ampAlpha = tex2D(_AmplitudeRamp, float2(speed01, 0.5)).r;
                return FlowCompose(rgb, 1.0, ampAlpha);
            }
            ENDCG
        }
    }
}
