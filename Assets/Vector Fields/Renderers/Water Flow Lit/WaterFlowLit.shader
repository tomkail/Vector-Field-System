// Lit normal-flow water (URP): the same ping-pong flow map as FlowMap, but it flows a HEIGHTFIELD (the water
// texture's luminance), derives a normal from it, and lights it with the SCENE's lights via URP's ForwardLit pass — so
// moving specular/shading that ride the field respond to the Directional Light (and point/spot lights) you add, with
// shadows. URP-only (this project is URP; Built-in is not a target).
//
// Wiring is identical to the other visualizers: a VectorFieldTextureRenderer binds the field to _MainTex; assign a
// (tiling, ideally smooth/wavy) water texture to _WaterTex.
Shader "Vector Fields/Water Flow Lit" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by VectorFieldTextureRenderer
        _WaterTex ("Water Height/Albedo", 2D) = "gray" {}
        _Tiling ("Water Tiling", Float) = 4
        _FlowStrength ("Flow Strength", Range(0,2)) = 0.3
        _FlowSpeed ("Flow Speed", Range(0,4)) = 1
        [Space][Toggle(_DUAL_SCALE)] _DualScale ("Second Layer", Float) = 1
        _DetailTiling ("  Detail Tiling x", Float) = 2.17
        _DetailSpeed ("  Detail Speed x", Float) = 1.7
        [Header(Surface)]
        _NormalStrength ("Normal Strength", Range(0,8)) = 3
        _SampleDist ("Normal Sample Dist (uv)", Range(0.0005,0.02)) = 0.003
        [Header(Water color)]
        _DeepColor ("Deep Color", Color) = (0.02,0.15,0.25,1)
        _ShallowColor ("Shallow Color", Color) = (0.15,0.45,0.55,1)
        [Header(Specular and fresnel)]
        _Specular ("Specular Intensity", Range(0,8)) = 2.5
        _Shininess ("Shininess", Range(1,256)) = 60
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 4
        _FresnelStrength ("Fresnel Strength", Range(0,1)) = 0.25
    }

    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // Scene lighting keywords so the main light, additional lights, and shadows are actually fed in.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma shader_feature_local _DUAL_SCALE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_WaterTex); SAMPLER(sampler_WaterTex);

            CBUFFER_START(UnityPerMaterial)
                float _Tiling, _FlowStrength, _FlowSpeed, _DetailTiling, _DetailSpeed;
                float _NormalStrength, _SampleDist, _Specular, _Shininess, _FresnelPower, _FresnelStrength;
                float4 _DeepColor, _ShallowColor;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
            };

            Varyings vert (Attributes input) {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionHCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = n.tangentWS;
                o.bitangentWS = n.bitangentWS;
                o.uv = input.uv;
                return o;
            }

            float SampleHeight (float2 uv) {
                float3 c = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, uv).rgb;
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            // One ping-pong flow layer -> scalar height (see FlowMap for the ping-pong rationale).
            float FlowHeightLayer (float2 vel, float2 tileUv, float speed) {
                float t = _Time.y * _FlowSpeed * speed;
                float p0 = frac(t), p1 = frac(t + 0.5);
                float h0 = SampleHeight(tileUv - vel * _FlowStrength * p0);
                float h1 = SampleHeight(tileUv - vel * _FlowStrength * p1);
                return lerp(h0, h1, abs(1.0 - 2.0 * p0));
            }
            float FlowHeight (float2 baseUv, float2 vel) {
                float h = FlowHeightLayer(vel, baseUv * _Tiling, 1.0);
                #ifdef _DUAL_SCALE
                    h = (h + FlowHeightLayer(vel, baseUv * _Tiling * _DetailTiling, _DetailSpeed)) * 0.5;
                #endif
                return h;
            }

            half4 frag (Varyings input) : SV_Target {
                float2 vel = -1.0 * (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rg - 0.5); // decode; negated so flow travels WITH the field (static-texture scroll, opposite sign to IBFV — see FlowMap)

                // Flowed heightfield -> tangent-space normal by finite differences, then into world space via the TBN.
                float e = _SampleDist;
                float hc = FlowHeight(input.uv, vel);
                float hx = FlowHeight(input.uv + float2(e, 0), vel);
                float hy = FlowHeight(input.uv + float2(0, e), vel);
                float3 tn = normalize(float3((hc - hx) * _NormalStrength, (hc - hy) * _NormalStrength, 1.0));
                float3 N = normalize(tn.x * input.tangentWS + tn.y * input.bitangentWS + tn.z * input.normalWS);

                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, hc);

                // Ambient from spherical harmonics (scene ambient / skybox), then the main light.
                float3 col = water * SampleSH(N);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light main = GetMainLight(shadowCoord);
                float3 rad = main.color * (main.shadowAttenuation * main.distanceAttenuation);
                float3 H = normalize(main.direction + V);
                col += water * rad * saturate(dot(N, main.direction));
                col += rad * pow(saturate(dot(N, H)), _Shininess) * _Specular;

                // Point / spot lights you add to the scene.
                #ifdef _ADDITIONAL_LIGHTS
                    uint count = GetAdditionalLightsCount();
                    for (uint li = 0u; li < count; li++) {
                        Light add = GetAdditionalLight(li, input.positionWS);
                        float3 arad = add.color * (add.shadowAttenuation * add.distanceAttenuation);
                        float3 ah = normalize(add.direction + V);
                        col += water * arad * saturate(dot(N, add.direction));
                        col += arad * pow(saturate(dot(N, ah)), _Shininess) * _Specular;
                    }
                #endif

                // Grazing-angle fresnel brighten.
                col += pow(1.0 - saturate(dot(N, V)), _FresnelPower) * _FresnelStrength;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
