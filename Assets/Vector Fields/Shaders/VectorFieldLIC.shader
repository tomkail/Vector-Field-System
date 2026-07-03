// Line Integral Convolution (Cabral & Leedom 1993): the gold-standard *static* picture of a vector field.
//
// For each pixel, walk a short streamline BOTH directions along the flow (Euler-integrating the field), sampling a
// white-noise texture at each step and averaging. Noise gets blurred ALONG the flow but stays sharp ACROSS it, so the
// result is dense hair-like streaks combed along the field lines. Unlike IBFV this is stateless — no feedback buffer,
// recomputed every frame — so it's crisp and never washes out, at the cost of a short raymarch per pixel.
//
// Animated by phase-shifting a periodic weight along the streamline (_Phase, driver- or _Time-driven), which makes the
// streaks appear to flow. Wiring: same as the other visualizers — a VectorFieldTextureRenderer binds the field to
// _MainTex; assign a tiling white-noise texture to _NoiseTex.
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
        _Contrast ("Contrast", Range(1,6)) = 3
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;   // the vector field render texture (RG = vector*0.5 + 0.5)
            float4 _MainTex_TexelSize;
            sampler2D _NoiseTex;
            float _NoiseScale, _StepCount, _StepLength, _Phase, _AnimSpeed, _Contrast;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Decode the field at a UV, signed, matching the other visualizers' convention.
            float2 sampleFlow (float2 uv) {
                return -1.0 * (tex2D(_MainTex, uv).rg - 0.5);
            }

            // Integrate one direction along the flow, accumulating weighted noise samples. `dir` = +1 forward, -1 back.
            void integrate (float2 uv, float dir, float phase, inout float acc, inout float wsum) {
                float2 p = uv;
                int steps = (int)_StepCount;
                for (int s = 1; s <= steps; s++) {
                    float2 v = sampleFlow(p);
                    float len = max(length(v), 1e-5);
                    p += dir * (v / len) * _StepLength;               // unit-speed march so spacing is uniform
                    // Periodic along-streamline weight: a moving bump makes the streaks flow when `phase` advances.
                    float t = (float)s / steps;
                    float w = 0.5 + 0.5 * cos(6.2831853 * (t - dir * phase));
                    w *= 1.0 - t;                                      // taper with distance (kernel falloff)
                    acc  += w * tex2D(_NoiseTex, p * _NoiseScale).r;
                    wsum += w;
                }
            }

            fixed4 frag (v2f i) : SV_Target {
                float phase = frac(_Phase + _Time.y * _AnimSpeed * 0.1);
                float acc = tex2D(_NoiseTex, i.uv * _NoiseScale).r;    // centre sample
                float wsum = 1.0;
                integrate(i.uv, +1.0, phase, acc, wsum);
                integrate(i.uv, -1.0, phase, acc, wsum);
                float lic = acc / max(wsum, 1e-5);
                // Expand contrast around the mid-grey mean so the combed streaks read clearly.
                lic = saturate((lic - 0.5) * _Contrast + 0.5);
                return fixed4(lic.xxx, 1.0) * _Color;
            }
            ENDCG
        }
    }
}
