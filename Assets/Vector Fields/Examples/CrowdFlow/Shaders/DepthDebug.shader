// Minimal isolated test of the URP camera depth texture. Transparent, ZWrite-off quad that samples SceneDepth behind
// it and paints near->far as green->red. If this shows a gradient over the terrain, depth reading works; if it's a
// flat colour, the depth texture isn't sampleable. Nothing to do with the water shader — pure depth read.
Shader "CrowdFlow/DepthDebug" {
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            V vert(A v) {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(V i) : SV_Target {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(uv);
                float eye = LinearEyeDepth(rawDepth, _ZBufferParams);
                // Map 0..120 world units of eye depth to 0..1; near = green, far = red.
                float d = saturate(eye / 120.0);
                return half4(d, 1.0 - d, 0.0, 0.85);
            }
            ENDHLSL
        }
    }
}
