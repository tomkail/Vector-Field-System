// Single-texture "Flow Map" — the minimal case, built straight on the core FlowMapSample + the shared
// VectorFieldFlowColor styling. This (and FlowMapRenderer) exercise the shared base with the simplest
// possible effect; the tiered variant (FlowMap.shader) layers N speed tiers on the same core. Driven by
// FlowMapRenderer.
Shader "Vector Fields/Flow Map" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by the renderer
        _WaterTex ("Water Texture", 2D) = "white" {}
        _Tiling ("Water Tiling", Float) = 4
        _FlowStrength ("Flow Strength", Range(0,2)) = 0.3
        _FlowSpeed ("Flow Speed", Range(0,4)) = 1
        _DualScale ("Second Layer (breaks up tiling)", Float) = 1
        _DetailTiling ("Detail Tiling x", Float) = 2.17
        _DetailSpeed ("Detail Speed x", Float) = 1.7
        _Color ("Tint", Color) = (1,1,1,1)
        // Shared styling (driven by FlowMapRenderer via VectorFieldFlowStyle; defaults keep water untouched).
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldFlowColor.cginc"
            #include "FlowMap.cginc"   // core ping-pong sampler

            sampler2D _MainTex;
            sampler2D _WaterTex;
            float _Tiling, _FlowStrength, _FlowSpeed, _DetailTiling, _DetailSpeed, _DualScale;
            fixed4 _Color;
            sampler2D _ColorGradient;
            sampler2D _AmplitudeRamp;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Primary layer + the optional detail layer (a second sample at a different scale/speed).
            fixed4 water (float2 uv, float2 vel) {
                fixed4 c = FlowMapSample(_WaterTex, vel, uv * _Tiling, _FlowStrength, _FlowSpeed, _Time.y);
                if (_DualScale > 0.5) {
                    fixed4 d = FlowMapSample(_WaterTex, vel, uv * _Tiling * _DetailTiling, _FlowStrength, _FlowSpeed * _DetailSpeed, _Time.y);
                    c = (c + d) * 0.5;
                }
                return c;
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 vel = (tex2D(_MainTex, i.uv).rg - 0.5);   // positive decode → flows WITH the field
                float speed01 = FlowSpeed01(vel * 2.0);

                float3 rgb = water(i.uv, vel).rgb * _Color.rgb;
                // Shared styling: speed colourmap (white ramp = no tint), contrast/gamma, composite over background.
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
