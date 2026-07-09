// PROTOTYPE — Image-Based Flow Visualization (van Wijk 2002), a *seamless* flowing-streak look.
//
// This is a DIFFERENT aesthetic from VectorFieldFlowAligned (the sand-ripple look). IBFV produces blurry,
// directional, LIC-like streaks — seam-free by construction, because it builds the image by ADVECTING a feedback
// buffer along the flow and blending in fresh noise each frame, never by orienting an anisotropic texture (which is
// what forces the seam there — see FLOW_ALIGNED_NOTES.md).
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
        _NoiseScale ("Noise Scale", Float) = 6
        _NoiseRate ("Noise Twinkle Rate", Float) = 1.5                         // per-texel pulse cycles/sec
        _NoisePhase ("Time (driver-set)", Vector) = (0,0,0,0)                  // .x = elapsed seconds, drives the twinkle
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
            #include "IBFV.cginc"   // core feedback step: VectorFieldIBFVStep(...)

            sampler2D _MainTex;   // previous accumulation (Graphics.Blit source)
            sampler2D _FieldTex;  // the vector field render texture (RG = vector*0.5 + 0.5)
            sampler2D _NoiseTex;
            float _FlowStep;
            float _NoiseAmount;
            float _NoiseScale;
            float _NoiseRate;
            float4 _NoisePhase;   // .x = elapsed seconds

            fixed4 frag (v2f_img i) : SV_Target {
                // One IBFV feedback step: advect the previous accumulation (_MainTex) along the field and inject noise.
                float3 acc = VectorFieldIBFVStep(_MainTex, _FieldTex, _NoiseTex, i.uv,
                                                 _FlowStep, _NoiseAmount, _NoiseScale, _NoiseRate, _NoisePhase.x);
                return fixed4(acc, 1.0);
            }
            ENDCG
        }
    }
}
