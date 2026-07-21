// Tiered variant of Flow-Aligned: the same cell-blend/seam-handling core (VectorFieldFlowAligned.cginc), but the
// streak texture, its scale, and its scroll speed come from N SPEED TIERS packed into a Texture2DArray, blended per
// sample by the local flow speed (VF_TIERED_STREAK — see the cginc). e.g. fine ripples where the flow is slow, bold
// churn where it's fast. Driven by TieredFlowAlignedTextureRenderer.
Shader "Vector Fields/Flow-Aligned/Flow-Aligned (Tiered)" {
	Properties {
		// Everything is driven by TieredFlowAlignedTextureRenderer via the property block every bind — editing the
		// material does nothing (the ramps are baked from the component's gradient/curve).
		[HideInInspector] _MainTex ("Vector Field", 2D) = "white" {} // bound by VectorFieldTextureRenderer
		[HideInInspector] _TexArray ("Streak Textures (per tier)", 2DArray) = "white" {}
		[HideInInspector] _Rect ("Rect", Vector) = (0,0,1,1)
		[HideInInspector] _GridCellCount ("Grid Cell Count", Range(0,256)) = 400.0
		[HideInInspector] _Brightness ("Brightness", Range(0,16)) = 8
		[HideInInspector] _FlowSamplingMode ("Flow Sampling Mode", Float) = 1
		[HideInInspector] _SeamBand ("Seam Mask Band (px)", Range(0,8)) = 2
		[HideInInspector] _SeamReach ("Seam Mask Reach (px)", Range(0,16)) = 4
		[HideInInspector] _ContinuousAmplitude ("Continuous Amplitude", Float) = 1
		[HideInInspector] _SeamDebug ("Seam Debug (nearest-good dir)", Float) = 0
		[HideInInspector] _UseTextureColor ("Use Texture Color", Float) = 0
		[HideInInspector] _ColorGradient ("Recolor Gradient", 2D) = "white" {}
		[HideInInspector] _TextureRotation ("Texture Rotation", Float) = 0
		[HideInInspector] _AmplitudeRamp ("Amplitude Alpha Ramp (curve)", 2D) = "white" {}
		// Shared styling fallbacks (driven by TieredFlowAlignedTextureRenderer via VectorFieldFlowStyle); identity
		// defaults so a bare material renders unstyled.
		[HideInInspector] _BackgroundColor ("Background", Color) = (0,0,0,0)
		[HideInInspector] _Contrast ("Contrast", Float) = 1
		[HideInInspector] _Gamma ("Gamma", Float) = 1
		[HideInInspector] _MaxSpeed ("Max Speed", Float) = 1
		[HideInInspector] _FlowAlpha ("Opacity", Float) = 1
	}

	SubShader {
		Tags { "RenderType"="Transparent" "Queue"="Transparent"}
		Blend SrcAlpha OneMinusSrcAlpha

		Pass {
			ZTest LEqual
			Fog { Mode Off }

			CGPROGRAM
			#define VF_TIERED_STREAK 1
			#include "VectorFieldFlowAligned.cginc"

			#pragma fragment frag
			#pragma vertex vert
			#pragma target 3.5           // Texture2DArray sampling

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
