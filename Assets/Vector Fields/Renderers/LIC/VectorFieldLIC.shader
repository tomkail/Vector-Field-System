// Line Integral Convolution (Cabral & Leedom 1993): the gold-standard *static* picture of a vector field.
//
// For each pixel, walk a short streamline BOTH directions along the flow (Euler-integrating the field), sampling a
// white-noise texture at each step and averaging. Noise gets blurred ALONG the flow but stays sharp ACROSS it, so the
// result is dense hair-like streaks combed along the field lines. Unlike IBFV this is stateless — no feedback buffer,
// recomputed every frame — so it's crisp and never washes out, at the cost of a short raymarch per pixel.
//
// Animated by phase-shifting a periodic weight along the streamline (_Phase, driver- or _Time-driven), which makes the
// streaks appear to flow. Wiring: a VectorFieldTextureRenderer (use LICTextureRenderer to also drive the styling) binds
// the field to _MainTex; assign a tiling white-noise texture to _NoiseTex. Colour/contrast/background come from the
// shared VectorFieldFlowColor styling, pushed by LICTextureRenderer (see VectorFieldFlowStyle).
Shader "Vector Fields/Vector Field LIC" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by VectorFieldTextureRenderer
        _NoiseTex ("White Noise", 2D) = "white" {}
        // NOTE: keep _NoiseScale low — the noise must be a few px per texel. Too high tiles it sub-pixel, so every
        // pixel samples decorrelated noise and LIC can't comb anything (it just looks like static noise).
        _NoiseScale ("Noise Scale", Float) = 2
        _StepCount ("Steps Per Side", Range(1,64)) = 32
        _StepLength ("Step Length (uv)", Range(0.0005,0.02)) = 0.003
        _Phase ("Flow Phase (anim)", Float) = 0        // shifts the along-streamline weighting so streaks animate
        _AnimSpeed ("Anim Speed", Range(0,8)) = 2
        // Styling (driven from LICTextureRenderer via VectorFieldFlowStyle; these slots give sane defaults so the
        // material still renders if used without the component).
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "../_Shared/VectorFieldFlowColor.cginc"
            #include "LIC.cginc"   // core convolution: VectorFieldLIC(...)

            sampler2D _MainTex;   // the vector field render texture (RG = vector*0.5 + 0.5)
            sampler2D _NoiseTex;
            float _NoiseScale, _StepCount, _StepLength, _Phase, _AnimSpeed;
            sampler2D _ColorGradient;   // shared styling ramps (declared here; scalars live in the include)
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
                float phase = frac(_Phase + _Time.y * _AnimSpeed * 0.1);
                float lic = VectorFieldLIC(_MainTex, _NoiseTex, i.uv, (int)_StepCount, _StepLength, _NoiseScale, phase);

                // Shared styling: contrast/gamma the streak into coverage, colour by SPEED, composite over background.
                float coverage = FlowContrastGamma(lic);
                float2 fieldVec = (tex2D(_MainTex, i.uv).rg - 0.5) * 2.0;   // field speed (positive decode)
                float speed01 = FlowSpeed01(fieldVec);
                float3 col = tex2D(_ColorGradient, float2(speed01, 0.5)).rgb;
                float ampAlpha = tex2D(_AmplitudeRamp, float2(speed01, 0.5)).r;
                return FlowCompose(col, coverage, ampAlpha);
            }
            ENDCG
        }
    }
}
