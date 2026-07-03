// Ping-pong flow map (Valve "Water Flow" / van Wijk): scrolls a texture along the vector field, per-pixel.
//
// The naive `uv += flow * time` smears infinitely wherever neighbouring cells disagree on direction. Instead we push
// the UVs by a *bounded* sawtooth phase (frac time) that resets periodically, and run TWO copies offset by half a
// cycle, cross-fading between them so each copy's reset is hidden behind the other's mid-cycle. The result flows
// forever without tearing.
//
// Drop-in wiring: put this on a material, add a VectorFieldTextureRenderer to the same quad and point it at a field.
// That component binds the field's live render texture to _MainTex (RG = vector*0.5 + 0.5), so the flow arrives for
// free — assign your water image to _WaterTex and go. No custom C# needed.
Shader "Vector Fields/Water Flow Map" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by VectorFieldTextureRenderer
        _WaterTex ("Water Texture", 2D) = "white" {}
        _Tiling ("Water Tiling", Float) = 4
        _FlowStrength ("Flow Strength", Range(0,2)) = 0.3   // how far UVs push per cycle (apparent turbulence)
        _FlowSpeed ("Flow Speed", Range(0,4)) = 1           // how fast the ping-pong cycle runs
        [Space][Toggle(_DUAL_SCALE)] _DualScale ("Second Layer (breaks up tiling)", Float) = 1
        _DetailTiling ("  Detail Tiling x", Float) = 2.17
        _DetailSpeed ("  Detail Speed x", Float) = 1.7
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DUAL_SCALE
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
            sampler2D _WaterTex;
            float _Tiling, _FlowStrength, _FlowSpeed, _DetailTiling, _DetailSpeed;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // One ping-pong flow-map layer: sample _WaterTex twice along `vel`, offset by half a cycle, and cross-fade
            // with a triangle wave so the periodic snap-back never pops.
            fixed4 flowLayer (float2 vel, float2 tileUv, float speed) {
                float t = _Time.y * _FlowSpeed * speed;
                float phase0 = frac(t);
                float phase1 = frac(t + 0.5);
                fixed4 c0 = tex2D(_WaterTex, tileUv - vel * _FlowStrength * phase0);
                fixed4 c1 = tex2D(_WaterTex, tileUv - vel * _FlowStrength * phase1);
                float blend = abs(1.0 - 2.0 * phase0); // 0 at phase 0, 1 at phase 0.5, 0 at phase 1
                return lerp(c0, c1, blend);
            }

            fixed4 frag (v2f i) : SV_Target {
                // Decode the field and match the sign convention of the other flow visualizers (IBFV, sand): -(rg-0.5).
                float2 vel = -1.0 * (tex2D(_MainTex, i.uv).rg - 0.5);

                fixed4 col = flowLayer(vel, i.uv * _Tiling, 1.0);
                #ifdef _DUAL_SCALE
                    // A second layer at a different scale/speed hides the obvious repetition of a single tiling.
                    fixed4 detail = flowLayer(vel, i.uv * _Tiling * _DetailTiling, _DetailSpeed);
                    col = (col + detail) * 0.5;
                #endif
                return col * _Color;
            }
            ENDCG
        }
    }
}
