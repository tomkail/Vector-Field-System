// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "El and Six/Vector Field/Vector Field Flow Visualization" {
	Properties {
		_MainTex ("Vector Field", 2D) = "white" {}
		_Tex ("Texture", 2D) = "white" {}
		_Rect ("Rect", Vector) = (0,0,1,1)
		_GridCellCount ("Grid Cell Count", Range(0,10000)) = 400.0
		_Speed ("Speed", Range(0,500)) = 20
		_TextureScale ("Texture Scale", Range(0,1000)) = 10
		_Brightness ("Brightness", Range(0,50)) = 8
	}

	SubShader {
		Tags { "RenderType"="Opaque" }

		Pass {
			ZTest LEqual
			Fog { Mode Off }

			CGPROGRAM
			#include "VectorFieldFlow.cginc" 

			#pragma fragment frag
			#pragma vertex vert

			struct appdata
			{
			    float4 vertex : POSITION;
			    float2 uv : TEXCOORD0;
			};

			struct v2f
			{
			    float2 uv : TEXCOORD0;
			    float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v) {
			    v2f o;
			    // transform position to clip space
			    // (multiply with model*view*projection matrix)
			    o.vertex = UnityObjectToClipPos(v.vertex);
			    // pass the texture coordinate, offset and scaled to show the target rect
			    float2 size = _Rect.zw - _Rect.xy;
			    o.uv = _Rect.xy + v.uv * size;
			    return o;
			}

			fixed4 frag (v2f i) : SV_Target {
				_AnimationTime = _Time;
				return CalculateFrag(i.uv);
			}
			ENDCG
		}
	}
}