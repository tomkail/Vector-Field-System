Shader "Custom/NormalMapWarpUnlit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _WarpStrength ("Warp Strength", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            fixed4 _Color;
            sampler2D _MainTex;
            sampler2D _NormalMap;
            float _WarpStrength;
            float4 _MainTex_ST;
            float4 _NormalMap_ST;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Apply tiling and offset for the normal map
                float2 normalUV = i.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;
                
                // Sample the normal map
                fixed4 normalColor = tex2D(_NormalMap, normalUV);
                
                // Convert normal map from [0, 1] to [-1, 1]
                float2 normal = float2(normalColor.r * 2.0 - 1.0, normalColor.g * 2.0 - 1.0);
                
                // Calculate the offset using the normal map and warp strength
                float2 offset = normal.xy * _WarpStrength;

                // Apply tiling and offset for the base texture
                float2 baseUV = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                
                // Warp the UV coordinates
                float2 warpedUV = baseUV + offset;
                //return float4(baseUV.x,baseUV.y,0,1);
                //return float4(offset.x,offset.y,0,1);
                // Sample the base texture using the warped UV coordinates
                fixed4 color = tex2D(_MainTex, warpedUV) * _Color;
                return color;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
