// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "El and Six/Asteroid Scanner/Floor Reveal"
{
	Properties
	{
		_Position ("Ship Position", Vector) = (0,0,0,0)
		_ArcAngle ("Ship Angle", Range(0,360)) = 0
		_ArcSize ("Arc Size", Range(0,360)) = 0
		_AngleStrength  ("Angle Strength", Range(0,8)) = 2
		_AngleAttenuation  ("Angle Attenuation", Range(0,8)) = 2
		_RingStrength ("Ring Strength", Range(0,1)) = 0
		_Range ("Ring Distance", Float) = 0
		_RingColor ("Ring Color", Color) = (0,0,0,0)
		_RingRadiusEnd ("Ring Radius End", Float) = 0.5
		_RingRadiusStart ("Ring Radius Start", Float) = 0.05

		_VectorFieldStrength ("Vector Field Strength", Range(0,1)) = 0
		_VectorFieldColor ("Vector Field Color", Color) = (0,0,0,0)
		_MainTex ("Vector Field", 2D) = "white" {}
		_Tex ("Texture", 2D) = "white" {}
		_GridCellCount ("Grid Cell Count", Range(0,8192)) = 400.0
		_Speed ("Speed", Range(0,5000)) = 20
		_TextureScale ("Texture Scale", Range(0,20)) = 10
		_Brightness ("Brightness", Range(0,100)) = 8

		_PerlinFrequency ("Perlin Frequency", Range(0,1)) = 0.1
		_PerlinSpeed ("Perlin Speed", Range(0,20)) = 5
		_PerlinIntensity ("Perlin Intensity", Float) = 1
		_PerlinOctaves ("Perlin Octaves", int) = 1
		_PerlinLacunarity ("Perlin Lacunarity", Float) = 0.5
		_PerlinBrightness ("Perlin Brightness", Range(-0.5,0.5)) = 0.5
	}
	SubShader
	{
		Cull Off
		Blend SrcAlpha One
		Lighting Off
		Fog { Mode Global }

		Tags {"Queue"="Transparent" "RenderType"="Transparent"}
		LOD 100

		Pass
		{

			Blend SrcAlpha One

			CGPROGRAM
			#include "ShaderMath.cginc"
			#include "VectorFieldFlow.cginc"
			#include "noiseSimplex.cginc"
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
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
				float4 vertex : SV_POSITION;
				float4 projPos : TEXCOORD1;
				float4 posWorld : TEXCOORD2;
				UNITY_FOG_COORDS(3)
			};

			uniform float4 _MainTex_ST;
			uniform float _VectorFieldStrength;
			uniform float4 _Position;

			uniform float4 _VectorFieldColor;
			uniform float _ArcAngle;
			uniform float _ArcSize;
			uniform float _AngleStrength;
			uniform float _AngleAttenuation;

			uniform float _RingStrength;
			uniform float _Range;
			uniform float _RingRadiusStart;
			uniform float _RingRadiusEnd;

			uniform float _PerlinFrequency;
			uniform float _PerlinSpeed;
			uniform float _PerlinIntensity;
			uniform int _PerlinOctaves;
			uniform float _PerlinLacunarity;
			uniform float _PerlinBrightness;


			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.posWorld = mul(unity_ObjectToWorld, v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				UNITY_TRANSFER_FOG(o,o.vertex);
				return o;
			}



			static const float deg2Rad = 0.0174533;

			float radiansBetween(float2 a, float2 b) {
				return atan2(-(b.y - a.y), b.x - a.x);
//				 + (deg2Rad * 90);
			}

			float degreesBetween(float2 a, float2 b) {
				return degrees(radiansBetween(a,b));
			}

			float WrapDegrees (float degrees) {
				return (degrees - floor(degrees / 360) * 360) - 180;
			}

			float DeltaDegrees (float a, float b) {
				float delta = a-b;
				return WrapDegrees(delta);
			}



			float GetSpreadAlpha (float dist) {
				float startDistance = _Range;
				float endDistance = _Range - (_Range * _RingRadiusStart);
				float endSpreadAlpha = invLerp(startDistance, endDistance, dist);
				float spreadAlpha = endSpreadAlpha;
				spreadAlpha = saturate(spreadAlpha);
				return spreadAlpha;
			}

			fixed4 frag (v2f i) : SV_Target
			{	
				fixed4 vectorFieldColor = fixed4(0,0,0,0);

				// 2D for accuracy - this means it doesn't really work in 3d space.
				float2 shipPosition = float2(_Position.x, _Position.z);
				float2 fragWorldPosition = float2(i.posWorld.x, i.posWorld.z);
				float dist = distance(shipPosition, fragWorldPosition);

				float spreadAlpha = GetSpreadAlpha(dist);

				float angleAlpha = 1;
				if(_ArcSize < 360) {
					// Cone
					{
//						float degreesFromPosition = degreesBetween(shipPosition, fragWorldPosition);
//						float halfAngleDelta = _ArcSize * 0.5;
//						float deltaDegrees = abs(DeltaDegrees(_ArcAngle, degreesFromPosition));
//						angleAlpha = (((halfAngleDelta-deltaDegrees))/halfAngleDelta) * _AngleAttenuation;
//						angleAlpha = clamp(angleAlpha, 0, 1);
//						angleAlpha *= (_Range-(dist))/_Range;
//	
//						angleAlpha += clamp(invLerp(0.5, 0, dist), 0, 1) * 0.5f;
//						angleAlpha = clamp(angleAlpha, 0, 1);
					} 

					// Ellipse
					{
						float rads = radians(_ArcAngle + 90);
						float2 angleDirection = float2(sin(rads), cos(rads));
						float2 directionToFrag = normalize((shipPosition - (angleDirection * (0.1))) - fragWorldPosition);
						float _dot = dot(directionToFrag, angleDirection);
						float normalizedDot = invLerp(-1, 1, _dot);
						dist *= lerp(_ArcSize, 1, clamp(normalizedDot, 0, 1));
						angleAlpha = GetSpreadAlpha(dist * 0.4);
						angleAlpha *= (_Range-(dist))/_Range;
					}
					angleAlpha *= _AngleStrength;
				}

				float3 scaledPos = i.posWorld * _PerlinFrequency;
				float ns = snoise(float4(scaledPos, _Time.x * _PerlinSpeed), _PerlinOctaves, _PerlinLacunarity);
//				ns = (ns * 0.5f + 0.5);
				ns += _PerlinBrightness;
				ns *= _PerlinIntensity;
				ns = clamp(ns, 0, 1);
//				vectorFieldCol.a *= vectorFieldCol.a * ns;
//				vectorFieldCol.a = saturate(vectorFieldCol.a);


				_AnimationTime = _Time;
				fixed4 vectorFieldFragColor = CalculateFrag(i.uv);
				vectorFieldColor += vectorFieldFragColor * _VectorFieldColor;
				float grey = (vectorFieldColor.r + vectorFieldColor.g + vectorFieldColor.b) * 0.333;
				grey *= _VectorFieldStrength;
				vectorFieldColor.a *= grey;
//				grey += 0.5;
				vectorFieldColor.rgb += (grey * grey * grey) * 0.5;

				vectorFieldColor.a *= spreadAlpha;
				vectorFieldColor.a *= angleAlpha;
				vectorFieldColor = clamp(0, 1, vectorFieldColor);


				if(_ArcSize < 360) {
//					vectorFieldColor.a -= lerp(ns, 0, clamp(0, 1, angleAlpha));
//					vectorFieldColor.a -= ns;
					vectorFieldColor = lerp(vectorFieldColor, max(float4(_VectorFieldColor.r, _VectorFieldColor.g, _VectorFieldColor.b, 0.2) * spreadAlpha, vectorFieldColor), angleAlpha);
//					spreadAlpha = 1;
//					angleAlpha = max(angleAlpha, 1);
					
//					vectorFieldColor.a = lerp(vectorFieldColor.a, max(0.2 * spreadAlpha, vectorFieldColor.a), angleAlpha);
//					vectorFieldColor.b = lerp(vectorFieldColor.r, max(_VectorFieldColor.r * spreadAlpha, vectorFieldColor.r), angleAlpha);
//					vectorFieldColor.g = lerp(vectorFieldColor.g, max(_VectorFieldColor.g * spreadAlpha, vectorFieldColor.g), angleAlpha);
//					vectorFieldColor.b = lerp(vectorFieldColor.b, max(_VectorFieldColor.b * spreadAlpha, vectorFieldColor.b), angleAlpha);
//					vectorFieldColor.a = max(vectorFieldColor.a, vectorFieldColor.b);
				} else {
//					vectorFieldColor.a -= ns;
				}
				vectorFieldColor.a -= ns;

//				vectorFieldColor = spreadAlpha;

				UNITY_APPLY_FOG(i.fogCoord, vectorFieldColor);
				return vectorFieldColor;
			}
			ENDCG



		}

		Pass
		{
			CGPROGRAM
			#include "ShaderMath.cginc"
			#include "VectorFieldFlow.cginc"
			#include "noiseSimplex.cginc"
			#pragma vertex vert
			#pragma fragment frag
			// make fog work
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
				float4 vertex : SV_POSITION;
				float4 projPos : TEXCOORD1;
				float4 posWorld : TEXCOORD2;
				UNITY_FOG_COORDS(3)
			};

			uniform float4 _RingColor;

			float4 _MainTex_ST;
			float _RingStrength;
			float4 _Position;
			float _Range;
			float _RingRadiusStart;
			float _RingRadiusEnd;

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.posWorld = mul(unity_ObjectToWorld, v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				UNITY_TRANSFER_FOG(o,o.vertex);
				return o;
			}

			float GetRingAlpha (float dist) {
				float startDistance = _Range + 0.01;
				float endDistance = _Range;
				float endSpreadAlpha = invLerp(startDistance, endDistance, dist);
				if(endSpreadAlpha > 1) 
					endSpreadAlpha = 0;
				float ringAlpha = endSpreadAlpha;

				startDistance = _Range - 0.3;
				endDistance = _Range;
				endSpreadAlpha = invLerp(startDistance, endDistance, dist);
				if(endSpreadAlpha > 1) 
					endSpreadAlpha = 0;
				ringAlpha += endSpreadAlpha;
				return ringAlpha;
			}

			fixed4 frag (v2f i) : SV_Target
			{	
				// 2D for accuracy - this means it doesn't really work in 3d space.
				float dist = distance(float2(_Position.x, _Position.z), float2(i.posWorld.x, i.posWorld.z));

				fixed4 ringColor = _RingColor;
				ringColor.a *= GetRingAlpha(dist) * 0.5f;

				fixed4 col = ringColor;
				col.a = saturate(col.a);
				// apply fog
				UNITY_APPLY_FOG(i.fogCoord, col);
				col *= _RingStrength;
				return col;
			}
			ENDCG
		}
	}
}
