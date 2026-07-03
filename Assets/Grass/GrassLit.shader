// URP lit counterpart to the unlit "GrassUnlitBasic" ShaderGraph. Reads the same compute-generated _DrawTriangles
// buffer (via SV_VertexID) that GrassComputeScript draws with Graphics.DrawProceduralIndirect, and lights each blade
// using the per-blade bent normal the compute shader writes — so bending/rippling shows up as moving light/dark bands
// and a specular shimmer. Set this shader's material as SO_GrassSettings.materialToUse to use the lit look.
Shader "Grass/GrassLit"
{
    Properties
    {
        _TopTint ("Top Tint", Color) = (1,1,1,1)
        _BottomTint ("Bottom Tint", Color) = (0.1,0.4,0.1,1)
        [HDR] _SpecColor ("Specular Color", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _SpecStrength ("Specular Strength", Range(0,4)) = 1
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 1
        _Translucency ("Translucency (wrap lighting)", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        // ---------------- Forward lit ----------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off   // grass blades are double-sided

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Grass.hlsl"   // DrawVertex/DrawTriangle + _DrawTriangles + GetComputeData_float

            CBUFFER_START(UnityPerMaterial)
                float4 _TopTint;
                float4 _BottomTint;
                float4 _SpecColor;
                float _Smoothness;
                float _SpecStrength;
                float _AmbientStrength;
                float _Translucency;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 color      : TEXCOORD3;
            };

            Varyings vert (uint vertexID : SV_VertexID)
            {
                Varyings o = (Varyings)0;
                float3 worldPos, normal, col; float2 uv;
                GetComputeData_float((float)vertexID, worldPos, normal, uv, col);
                o.positionWS = worldPos;
                o.normalWS = normalize(normal);
                o.uv = uv;
                o.color = col;
                o.positionCS = TransformWorldToHClip(worldPos);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // The per-blade normal is authored biased toward "up", so both sides of the double-sided blade light
                // consistently. We deliberately do NOT flip by front/back face: the grass is a triangle list whose
                // consecutive triangles wind oppositely, so a VFACE flip would invert the normal on every other
                // triangle and produce a light/dark checkerboard.
                float3 N = normalize(i.normalWS);

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Base colour: tip->base gradient (uv.y is 0 at root, 1 at tip) tinted by the per-blade colour.
                float3 baseColor = lerp(_BottomTint.rgb, _TopTint.rgb, saturate(i.uv.y)) * i.color;

                // Main light + shadows.
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = mainLight.direction;
                float atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // Wrap (half-lambert-ish) diffuse: grass looks better with soft translucent lighting.
                float ndl = dot(N, L);
                float diffuse = saturate((ndl + _Translucency) / (1.0 + _Translucency));

                // Blinn-Phong specular for the wind shimmer.
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), exp2(_Smoothness * 10.0 + 1.0)) * _SpecStrength;

                float3 lit = baseColor * mainLight.color * diffuse * atten
                           + _SpecColor.rgb * mainLight.color * spec * atten;

                // Ambient from spherical harmonics.
                float3 ambient = SampleSH(N) * baseColor * _AmbientStrength;

                float3 result = lit + ambient;

                // Additional lights (points/spots), diffuse only, cheap.
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0; li < count; li++)
                {
                    Light al = GetAdditionalLight(li, i.positionWS);
                    float ad = saturate((dot(N, al.direction) + _Translucency) / (1.0 + _Translucency));
                    result += baseColor * al.color * ad * (al.shadowAttenuation * al.distanceAttenuation);
                }
                #endif

                return half4(result, 1.0);
            }
            ENDHLSL
        }

        // ---------------- Shadow caster (so grass casts shadows) ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 4.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Grass.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 shadowVert (uint vertexID : SV_VertexID) : SV_POSITION
            {
                float3 worldPos, normal, col; float2 uv;
                GetComputeData_float((float)vertexID, worldPos, normal, uv, col);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - worldPos);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(worldPos, normalize(normal), lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 shadowFrag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
