// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "UnityX/Terrain/Terrain Debugger" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
		_StartColor ("Strong Color", Color) = (0,0,0,1)
		_EndColor ("Weak Color", Color) = (1,1,1,1)
		_StartHeight ("Start Height", float) = 0
		_EndHeight ("End Height", float) = 100
	}

	SubShader {
//	Tags { "RenderType"="Opaque" }
        LOD 100

		Tags {
//			"RenderType" = "Transparent"
//			"Queue" = "Transparent"
		}
//		Tags { "RenderType"="Transparent" "Queue"="Transparent"}
		Blend SrcAlpha OneMinusSrcAlpha
		Pass {
			Fog { Mode Global }
			Cull Back
//			ZTest Always
//			ZWrite Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag 
			#pragma multi_compile_fog
            
            #include "UnityCG.cginc"

           struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float4 vertexLocal : TEXCOORD2;
            };

            uniform sampler2D _MainTex;
            uniform float4 _StartColor;
            uniform float4 _EndColor;
            uniform float _StartHeight;
            uniform float _EndHeight;
            float4 _MainTex_ST;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertexLocal = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
				float n = (i.vertexLocal.y - _StartHeight) / (_EndHeight - _StartHeight);
//                 apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col * lerp(_StartColor, _EndColor, n);
            }
            ENDCG
		}
	}
}