// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Vector Fields/Vector Field Flow Visualization" {
	Properties {
		_MainTex ("Vector Field", 2D) = "white" {}
		_Tex ("Texture", 2D) = "white" {}
		_Rect ("Rect", Vector) = (0,0,1,1)
		_GridCellCount ("Grid Cell Count", Range(0,10000)) = 400.0
		_Speed ("Speed", Range(0,500)) = 20
		_TextureScale ("Texture Scale", Range(0,1000)) = 10
		_Brightness ("Brightness", Range(0,50)) = 8
		[Enum(Cell Blend Legacy,0,Cell Blend Seam Masked,1,Cell Blend Seam Copy,2)] _FlowSamplingMode ("Flow Sampling Mode", Float) = 1
		_SeamBand ("Seam Mask Band (px)", Range(0,8)) = 2
		_SeamReach ("Seam Mask Reach (px)", Range(0,16)) = 4
		[Toggle] _ContinuousAmplitude ("Continuous Amplitude", Float) = 1
		[Toggle] _SeamDebug ("Seam Debug (nearest-good dir)", Float) = 0
		[Toggle] _UseTextureColor ("Use Texture Color", Float) = 0
		[Enum(Magnitude,0,Luminance,1)] _GradientSource ("Recolor Gradient Source", Float) = 0
		_ColorGradient ("Recolor Gradient", 2D) = "white" {}
		[Enum(Rotate 0,0,Rotate 90,1,Rotate 180,2,Rotate 270,3)] _TextureRotation ("Texture Rotation", Float) = 0
		_AmplitudeRamp ("Amplitude Alpha Ramp (curve)", 2D) = "white" {}
	}

	SubShader {
		Tags { "RenderType"="Transparent" "Queue"="Transparent"}
		Blend SrcAlpha OneMinusSrcAlpha

		Pass {
			ZTest LEqual
			Fog { Mode Off }

			CGPROGRAM
			// NOTE: editing VectorFieldFlow.cginc alone may not trigger a recompile in Unity — touch this file (or
			// reimport the shader) to force it. Bump this when the .cginc changes: rev 16
			#include "VectorFieldFlow.cginc"

			#pragma fragment frag
			#pragma vertex vert
			#pragma target 3.0

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
				fixed4 col = CalculateFrag(i.uv);
//				col.a = col.rgb;
//				col = lerp(fixed4())
				return col;
			}
			ENDCG
		}
	}
}