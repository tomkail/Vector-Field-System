// A stylised terrain shader for the Crowd Flow demo, tuned to read like Super Mario 3D World: grass on non-steep
// ground (swapping between two grass tiles by world height), a striped rock/cliff "wall" on steep faces (slope >
// _WallSlopeAngle), and warm sand near the waterline. Smooth high-key lighting (soft lambert with a raised shade
// floor + generous ambient) plus a fresnel rim. No toon banding.
//
// Drop onto a Unity Terrain via Terrain.materialTemplate with drawInstanced=false (this is a plain URP object
// shader, not a terrain-instancing one).
Shader "CrowdFlow/MarioGrass" {
    Properties {
        [Header(Grass  swapped by height)]
        _GrassTex        ("Grass (low)", 2D) = "white" {}
        _GrassTex2       ("Grass (high)", 2D) = "white" {}
        _GrassTexScale   ("Grass Tile (world units)", Float) = 14
        _GrassTexTint    ("Grass Tint", Color) = (1, 1, 1, 1)
        _GrassHeight     ("Grass Swap Height (world Y)", Float) = 14
        _GrassHeightBlend("Grass Swap Blend (world units)", Float) = 6

        [Header(Wall  steep faces)]
        _WallTex         ("Wall / cliff (striped)", 2D) = "white" {}
        _WallTexScale    ("Wall Tile (world units)", Float) = 12
        _WallSlopeAngle  ("Wall Slope Angle (slope > X = wall)", Range(0,90)) = 42
        _SlopeBlend      ("Slope Blend", Range(0.001,0.35)) = 0.07

        [Header(Sand  near water)]
        _SandTex         ("Sand", 2D) = "white" {}
        _SandTexScale    ("Sand Tile (world units)", Float) = 10
        _WaterLevel      ("Water Level (world Y)", Float) = 6
        _SandBand        ("Sand Band (world units)", Float) = 3

        [Header(Seabed  underwater)]
        _SeabedColor     ("Seabed Colour", Color) = (0.58, 0.60, 0.48, 1)
        _SeabedFade      ("Seabed Fade (world units)", Float) = 6

        [Header(Fresnel rim)]
        _FresnelColor    ("Fresnel Color", Color) = (0.75, 1.0, 0.55, 1)
        _FresnelPower    ("Fresnel Power",    Range(0.5, 10)) = 3.5
        _FresnelStrength ("Fresnel Strength", Range(0, 2)) = 0.5

        [Header(Lighting)]
        _ShadeFloor      ("Shade Floor",  Range(0,1)) = 0.55
        _AmbientBoost    ("Ambient Boost",Range(0,3)) = 1.2
        _ShadowStrength  ("Shadow Strength", Range(0,1)) = 0.35
    }

    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassTex);   SAMPLER(sampler_GrassTex);
            TEXTURE2D(_GrassTex2);  SAMPLER(sampler_GrassTex2);
            TEXTURE2D(_WallTex);    SAMPLER(sampler_WallTex);
            TEXTURE2D(_SandTex);    SAMPLER(sampler_SandTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassTexTint, _FresnelColor, _SeabedColor;
                float _GrassTexScale, _GrassHeight, _GrassHeightBlend;
                float _WallTexScale, _WallSlopeAngle, _SlopeBlend;
                float _SandTexScale, _WaterLevel, _SandBand, _SeabedFade;
                float _FresnelPower, _FresnelStrength;
                float _ShadeFloor, _AmbientBoost, _ShadowStrength;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes v) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor  = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                float3 N = normalize(i.normalWS);
                float upness = saturate(N.y);   // 1 = flat/up, 0 = vertical

                // Grass: swap between the two grass tiles purely by world height.
                float2 grassUV = i.positionWS.xz / max(0.001, _GrassTexScale);
                float3 grassLow  = SAMPLE_TEXTURE2D(_GrassTex,  sampler_GrassTex,  grassUV).rgb;
                float3 grassHigh = SAMPLE_TEXTURE2D(_GrassTex2, sampler_GrassTex2, grassUV).rgb;
                float hMix = smoothstep(_GrassHeight - _GrassHeightBlend, _GrassHeight + _GrassHeightBlend, i.positionWS.y);
                float3 grass = lerp(grassLow, grassHigh, hMix) * _GrassTexTint.rgb;

                // Sand: planar XZ, in a band just above the waterline.
                float3 sand = SAMPLE_TEXTURE2D(_SandTex, sampler_SandTex, i.positionWS.xz / max(0.001, _SandTexScale)).rgb;

                // Wall: project so the horizontal strata run along world Y (V = height), U along a horizontal axis.
                float2 wallUV = float2(dot(i.positionWS.xz, float2(0.7071, 0.7071)), i.positionWS.y) / max(0.001, _WallTexScale);
                float3 wall = SAMPLE_TEXTURE2D(_WallTex, sampler_WallTex, wallUV).rgb;

                // Slope: wall on steep faces (slope > _WallSlopeAngle), grass otherwise.
                float cosWall = cos(radians(_WallSlopeAngle));
                float wallMask = 1.0 - smoothstep(cosWall - _SlopeBlend, cosWall + _SlopeBlend, upness);

                float3 albedo = grass;
                float sandMask = (1.0 - smoothstep(_WaterLevel, _WaterLevel + _SandBand, i.positionWS.y)) * (1.0 - wallMask);
                albedo = lerp(albedo, sand, saturate(sandMask));
                albedo = lerp(albedo, wall, wallMask);   // steep faces win

                // Underwater: fade to a calm flat seabed colour so the transparent sea shows smooth ground rather than
                // tiled texture noise (the deeper below the waterline, the flatter).
                float below = saturate((_WaterLevel - i.positionWS.y) / max(0.25, _SeabedFade));
                albedo = lerp(albedo, _SeabedColor.rgb, below);

                // --- smooth high-key lighting (no toon banding) ----------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(N, mainLight.direction));
                float atten = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                float diffuse = lerp(_ShadeFloor, 1.0, saturate(ndl * atten));

                float3 ambient = SampleSH(N) * _AmbientBoost;
                float3 lit = albedo * (ambient + mainLight.color * diffuse);

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                lit += _FresnelColor.rgb * (fres * _FresnelStrength);

                lit = MixFog(lit, i.fogFactor);
                return half4(lit, 1);
            }
            ENDHLSL
        }

        // Shadow casting so the crowd/attractions/obstacles cast onto the ground and hills self-shadow.
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vertShadow(A v) {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 fragShadow(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vertDepth(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 fragDepth(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vertDN(A v) {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(normalize(i.normalWS) * 0.5 + 0.5, 0); }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
