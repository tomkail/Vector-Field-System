// Lit normal-flow water: the same ping-pong flow map as WaterFlowMap, but instead of scrolling the albedo we flow a
// HEIGHTFIELD (the water texture's luminance) and light it — so what you see is moving specular glints and shading that
// ride the field, which is what actually sells "water" far more than a scrolling colour.
//
// Self-contained lighting (its own _LightDir), not URP scene lights, so it's pipeline-agnostic and needs no normal-map
// asset — the surface normal is derived from the flowed height by finite differences. Wiring is identical to the other
// visualizers: a VectorFieldTextureRenderer binds the field to _MainTex; assign a (tiling) water texture to _WaterTex.
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
        [Header(Lighting)]
        _LightDir ("Light Direction", Vector) = (0.4,0.5,-0.75,0)
        _LightColor ("Light Color", Color) = (1,1,1,1)
        _Specular ("Specular Intensity", Range(0,8)) = 2.5
        _Shininess ("Shininess", Range(1,256)) = 60
        _Ambient ("Ambient", Range(0,1)) = 0.35
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 4
        _FresnelStrength ("Fresnel Strength", Range(0,1)) = 0.25
    }

    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DUAL_SCALE
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _WaterTex;
            float _Tiling, _FlowStrength, _FlowSpeed, _DetailTiling, _DetailSpeed;
            float _NormalStrength, _SampleDist, _Specular, _Shininess, _Ambient, _FresnelPower, _FresnelStrength;
            fixed4 _DeepColor, _ShallowColor, _LightColor, _LightDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 flow : TEXCOORD1; };

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.flow = 0;
                return o;
            }

            // One ping-pong flow layer, returning a scalar height (water texture luminance) at the given tiled UV.
            float flowHeightLayer (float2 vel, float2 tileUv, float speed) {
                float t = _Time.y * _FlowSpeed * speed;
                float p0 = frac(t), p1 = frac(t + 0.5);
                float h0 = Luminance(tex2D(_WaterTex, tileUv - vel * _FlowStrength * p0).rgb);
                float h1 = Luminance(tex2D(_WaterTex, tileUv - vel * _FlowStrength * p1).rgb);
                return lerp(h0, h1, abs(1.0 - 2.0 * p0));
            }

            // Flowed height at a base UV (adds the dual-scale detail layer when enabled).
            float flowHeight (float2 baseUv, float2 vel) {
                float h = flowHeightLayer(vel, baseUv * _Tiling, 1.0);
                #ifdef _DUAL_SCALE
                    h = (h + flowHeightLayer(vel, baseUv * _Tiling * _DetailTiling, _DetailSpeed)) * 0.5;
                #endif
                return h;
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 vel = -1.0 * (tex2D(_MainTex, i.uv).rg - 0.5); // decode field (matches other visualizers)

                // Heightfield -> normal by finite differences of the flowed height.
                float e = _SampleDist;
                float hc = flowHeight(i.uv, vel);
                float hx = flowHeight(i.uv + float2(e, 0), vel);
                float hy = flowHeight(i.uv + float2(0, e), vel);
                float3 n = normalize(float3((hc - hx) * _NormalStrength, (hc - hy) * _NormalStrength, 1.0));

                float3 L = normalize(_LightDir.xyz);
                float3 V = float3(0, 0, 1);          // ortho view, looking along the plane normal
                float3 H = normalize(L + V);
                float ndl = saturate(dot(n, L));
                float spec = pow(saturate(dot(n, H)), _Shininess) * _Specular;
                float fres = pow(1.0 - saturate(dot(n, V)), _FresnelPower) * _FresnelStrength;

                float3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, hc);
                float3 col = water * (_Ambient + (1.0 - _Ambient) * ndl)   // diffuse shading
                           + spec * _LightColor.rgb                        // moving specular glints
                           + fres;                                         // grazing-angle brighten
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
