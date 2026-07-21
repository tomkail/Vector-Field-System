// IBFV present/display pass. The IBFV accumulation buffer is kept mean-preserving grey (see VectorFieldFlowIBFV.shader)
// so its feedback loop stays stable — all colouring happens HERE, at display time, so it never compounds through the
// loop. Samples the grey accumulation (_MainTex) plus the field (_FieldTex, for speed/direction) and runs both through
// the shared VectorFieldFlowColor styling. Driven by VectorFieldFlowIBFV via VectorFieldFlowStyle.
Shader "Vector Fields/IBFV/IBFV Present" {
    Properties {
        [HideInInspector] _MainTex ("Accumulation", 2D) = "black" {}
        [HideInInspector] _FieldTex ("Field (RG vector)", 2D) = "gray" {}
        [HideInInspector] _ColorGradient ("Colour Ramp", 2D) = "white" {}
        [HideInInspector] _AmplitudeRamp ("Amplitude Ramp", 2D) = "white" {}
        // Styling fallbacks (driven by VectorFieldFlowIBFV via VectorFieldFlowStyle; defaults keep it visible standalone).
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldFlowColor.cginc"

            sampler2D _MainTex;   // grey IBFV accumulation buffer
            sampler2D _FieldTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
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

            fixed4 frag (v2f i) : SV_Target {
                float streak = tex2D(_MainTex, i.uv).r;          // grey accumulation — this IS the flow pattern
                float coverage = FlowContrastGamma(streak);      // contrast/gamma-expanded pattern

                // IBFV's visualisation IS its pattern (the streaks exist everywhere, not just where the flow is fast),
                // so colour by the PATTERN (luminance) through the gradient, and render OPAQUE. Colouring by speed can't
                // show a uniform-speed field (it goes black), and a speed-gated transparent result washes out over the
                // scene — both made IBFV disappear. Speed-driven inputs (_AmplitudeRamp / _MaxSpeed) don't apply here.
                float3 col = tex2D(_ColorGradient, float2(coverage, 0.5)).rgb;
                float3 rgb = lerp(_BackgroundColor.rgb, col, coverage);
                return fixed4(rgb, _FlowAlpha);
            }
            ENDCG
        }
    }
}
