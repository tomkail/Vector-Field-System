// A cheap additive "pillar of light" for the Crowd Flow beacons: a vertical cylinder that glows brightest at its
// base and fades out toward the top, additively blended so overlapping faces build up a soft volumetric beam. Cull
// Off + additive means the near and far walls both contribute, so the centre reads brighter than the silhouette.
// Meant for a tall thin cylinder placed at an attraction, tinted that attraction's colour (use an HDR colour for bloom).
Shader "CrowdFlow/LightPillar" {
    Properties {
        [HDR] _Color   ("Colour", Color) = (0.5, 0.8, 1, 1)
        _Falloff       ("Vertical Falloff", Range(0.2, 6)) = 1.6
        _Intensity     ("Intensity", Range(0, 8)) = 2.2
        _BaseBoost     ("Base Boost", Range(0, 4)) = 1.5
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass {
            Name "Beam"
            Tags { "LightMode"="UniversalForward" }
            Blend One One            // additive
            ZWrite Off
            Cull Off
            ZTest LEqual             // occluded by terrain/props in front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Falloff, _Intensity, _BaseBoost;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A v) {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(V i) : SV_Target {
                // Cylinder side UV.y runs 0 (base) -> 1 (top). Bright at the base, fading up; extra punch at the very base.
                float up = saturate(i.uv.y);
                float vertical = pow(saturate(1.0 - up), _Falloff);
                float baseGlow = pow(saturate(1.0 - up), 6.0) * _BaseBoost;
                float3 rgb = _Color.rgb * _Intensity * (vertical + baseGlow);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
