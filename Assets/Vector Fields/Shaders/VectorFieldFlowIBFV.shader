// PROTOTYPE — Image-Based Flow Visualization (van Wijk 2002), a *seamless* flowing-streak look.
//
// This is a DIFFERENT aesthetic from VectorFieldFlowVisualization (the sand-ripple shader). IBFV produces blurry,
// directional, LIC-like streaks — seam-free by construction, because it builds the image by ADVECTING a feedback
// buffer along the flow and blending in fresh noise each frame, never by orienting an anisotropic texture (which is
// what forces the seam there — see FLOW_VISUALIZATION_NOTES.md).
//
// This shader is just the per-frame update pass (a fullscreen blit). It needs a ping-pong feedback loop to do anything
// — drive it with VectorFieldFlowIBFV.cs.
Shader "Vector Fields/Vector Field Flow IBFV" {
    Properties {
        [HideInInspector] _MainTex ("Previous Accumulation", 2D) = "black" {} // blit source = previous frame's buffer
        _FieldTex ("Field (RG vector)", 2D) = "gray" {}
        _NoiseTex ("Injection Noise", 2D) = "white" {}
        _FlowStep ("Advection Step", Range(0,0.05)) = 0.008                    // how far to advect per frame (uv units)
        _NoiseAmount ("Noise Injection", Range(0,1)) = 0.08                    // fresh noise blended in each frame
        _NoiseScale ("Noise Scale", Float) = 8
        _NoisePhase ("Noise Phase (driver-set)", Vector) = (0,0,0,0)           // scrolled over time so streaks animate
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;   // previous accumulation (Graphics.Blit source)
            sampler2D _FieldTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
            sampler2D _NoiseTex;
            float _FlowStep;
            float _NoiseAmount;
            float _NoiseScale;
            float4 _NoisePhase;

            fixed4 frag (v2f_img i) : SV_Target {
                float2 uv = i.uv;

                // Decode the flow velocity (same sign convention as the sand shader: -(rg - 0.5)).
                float2 vel = -1.0 * (tex2D(_FieldTex, uv).rg - 0.5);

                // Advect: pull this pixel's value from where the flow carried it FROM last frame. Integrating the
                // feedback buffer this way is what makes the streaks follow the flow continuously — no global
                // coordinate, no curl obstruction, no seam.
                float2 prevUv = uv - vel * _FlowStep;
                float3 advected = tex2D(_MainTex, prevUv).rgb;

                // Inject a little fresh, time-scrolled noise. The advection smears it into streaks along the flow;
                // the constant injection keeps them alive and animating instead of converging to a static image.
                float3 noise = tex2D(_NoiseTex, uv * _NoiseScale + _NoisePhase.xy).rgb;

                return fixed4(lerp(advected, noise, _NoiseAmount), 1.0);
            }
            ENDCG
        }
    }
}
