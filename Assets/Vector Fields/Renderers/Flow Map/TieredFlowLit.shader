// Tiered variant of Flow Lit (URP): the same flowed-heightfield lighting, but the water look (height texture + tiling
// + flow params) comes from N SPEED TIERS packed into a Texture2DArray. Per pixel the two tiers bracketing the local
// flow speed are height-blended before the normal is derived (see VectorFieldSpeedTiers.cginc) — e.g. glassy ripples
// where the flow is slow, churn where it's fast. Surface/colour/specular stay global (material-authored).
//
// Wiring: use TieredFlowLitRenderer (drives the field texture, the tier array + params, and _MaxSpeed). Unlike Flow
// Lit's _DUAL_SCALE keyword, the second layer is a float branch here — keywords can't be set via a property block.
Shader "Vector Fields/Flow Map/Flow Lit (Tiered)" {
    Properties {
        [HideInInspector] _MainTex ("Vector Field (RG)", 2D) = "gray" {} // bound by the renderer
        [HideInInspector] _WaterArray ("Water Textures (per tier)", 2DArray) = "white" {}
        [HideInInspector] _MaxSpeed ("Max Speed", Float) = 1             // driven by the renderer
        // Driven by TieredFlowLitRenderer via the property block every bind — editing them on the material does nothing.
        [HideInInspector] _DualScale ("Second Layer (breaks up tiling)", Float) = 1
        [HideInInspector] _DetailTiling ("Detail Tiling x", Float) = 2.17
        [HideInInspector] _DetailSpeed ("Detail Speed x", Float) = 1.7
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
            #pragma target 3.5           // Texture2DArray sampling

            // Scene lighting keywords so the main light, additional lights, and shadows are actually fed in.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../_Shared/VectorFieldSpeedTiers.cginc"   // plain functions — HLSL-safe

            TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
            TEXTURE2D_ARRAY(_WaterArray); SAMPLER(sampler_WaterArray);   // one slice per speed tier

            CBUFFER_START(UnityPerMaterial)
                float _DualScale, _DetailTiling, _DetailSpeed, _MaxSpeed;
                float _NormalStrength, _SampleDist, _Specular, _Shininess, _FresnelPower, _FresnelStrength;
                float4 _DeepColor, _ShallowColor;
            CBUFFER_END

            // Per-tier data, sorted ascending by speed; _TierCount valid entries (set by TieredFlowLitRenderer).
            float _TierSpeed[VF_MAX_TIERS];
            float _TierTiling[VF_MAX_TIERS];
            float _TierStrength[VF_MAX_TIERS];
            float _TierFlowSpeed[VF_MAX_TIERS];
            int _TierCount;

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

            float SampleHeight (int slice, float2 uv) {
                // Explicit LOD: tier blending puts this inside per-pixel (non-uniform) control flow, where implicit
                // derivatives are undefined. The array is a mip-less render texture, so LOD 0 is exact.
                float3 c = SAMPLE_TEXTURE2D_ARRAY_LOD(_WaterArray, sampler_WaterArray, uv, slice, 0).rgb;
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            // One ping-pong flow layer of tier `slice` -> scalar height (see FlowMap for the ping-pong rationale).
            float FlowHeightLayer (int slice, float2 vel, float2 tileUv, float strength, float flowSpeed, float speedMul) {
                float t = _Time.y * flowSpeed * speedMul;
                float p0 = frac(t), p1 = frac(t + 0.5);
                float h0 = SampleHeight(slice, tileUv - vel * strength * p0);
                float h1 = SampleHeight(slice, tileUv - vel * strength * p1);
                return lerp(h0, h1, abs(1.0 - 2.0 * p0));
            }

            // One tier's full flowed height: primary layer + the optional detail layer (shared detail scale/speed).
            float TierFlowHeight (int slice, float2 baseUv, float2 vel) {
                float tiling = _TierTiling[slice], strength = _TierStrength[slice], flowSpeed = _TierFlowSpeed[slice];
                float h = FlowHeightLayer(slice, vel, baseUv * tiling, strength, flowSpeed, 1.0);
                if (_DualScale > 0.5)
                    h = (h + FlowHeightLayer(slice, vel, baseUv * tiling * _DetailTiling, strength, flowSpeed, _DetailSpeed)) * 0.5;
                return h;
            }

            // Speed-tier-blended height. lo/hi/w are resolved once per fragment (from the centre sample's speed) and
            // reused for the finite-difference offsets, so the derived normal stays consistent.
            float FlowHeight (float2 baseUv, float2 vel, int lo, int hi, float w) {
                float h = TierFlowHeight(lo, baseUv, vel);
                if (hi > lo) h = lerp(h, TierFlowHeight(hi, baseUv, vel), w);
                return h;
            }

            half4 frag (Varyings input) : SV_Target {
                float2 vel = -1.0 * (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rg - 0.5); // decode; negated so flow travels WITH the field (static-texture scroll — see FlowMap)

                // Find the two speed tiers bracketing this pixel (vel is the half-magnitude decode, so *2 = magnitude).
                float speed01 = saturate(length(vel) * 2.0 / max(_MaxSpeed, 1e-5));
                int lo, hi; float w;
                FindTierBracket(speed01, _TierSpeed, _TierCount, lo, hi, w);

                // Flowed heightfield -> tangent-space normal by finite differences, then into world space via the TBN.
                float e = _SampleDist;
                float hc = FlowHeight(input.uv, vel, lo, hi, w);
                float hx = FlowHeight(input.uv + float2(e, 0), vel, lo, hi, w);
                float hy = FlowHeight(input.uv + float2(0, e), vel, lo, hi, w);
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
