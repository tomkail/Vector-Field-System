// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "El and Six/Terrain/FlowVis Terrain" {
	Properties {
		_StrongColor ("Strong Color", Color) = (0,0,0,0)
		_WeakColor ("Weak Color", Color) = (0,0,0,0)
		_LightColor ("Light Color", Color) = (0,0,0,0)
		_DarkColor ("Dark Color", Color) = (0,0,0,0)

		_MainTex ("Vector Field", 2D) = "white" {}
		_VectorField ("Vector Field", 2D) = "white" {}
		_Tex ("Texture", 2D) = "white" {}
		_GridCellCount ("Grid Cell Count", Range(0,8192)) = 400.0
		_Speed ("Speed", Range(0,5000)) = 20
		_TextureScale ("Texture Scale", Range(0,20)) = 10
		_Brightness ("Brightness", Range(0,100)) = 8

		_FadeStart ("Distance Fade Start Distance", Range(0,500)) = 0
		_FadeEnd ("Distance Fade End Distance", Range(0,500)) = 250
		_FadeColor ("Fade Color", Color) = (0,0,0,0)
	}

	SubShader {
		Tags { "RenderType"="Transparent" "Queue"="Transparent"}
		Blend SrcAlpha OneMinusSrcAlpha
		Pass {
//			ZTest Always
			Fog { Mode Global }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag alpha
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
				UNITY_FOG_COORDS(2)
            };

            uniform float4 _StrongColor;
            uniform float4 _WeakColor;
            uniform float4 _LightColor;
            uniform float4 _DarkColor;

			uniform sampler2D _MainTex;
			uniform sampler2D _VectorField;
			uniform sampler2D _Tex;
			uniform float _GridCellCount;
			uniform float _Speed;
			uniform float _TextureScale;
			uniform float _Brightness;

			uniform float _FadeStart;
			uniform float _FadeEnd;
			uniform float4 _FadeColor;

			// TODO: Pull out to varying
            float2 fragGridCellFrac;
            
            // TODO: Pull out into uniforms
            float2 cellSizeNorm;
            float2 midGrid;

            sampler2D_float _CameraDepthTexture;

            float3 getTexelStrengthFromTileWithCentre(float2 loc)
            {
                // Sample the flow from the centre of the tile
                float2 flowVec  = -1.0 * ( tex2D(_VectorField, loc).rg  - float2(0.5, 0.5) );

                // Find direction and magnitude of this flow
                float flowMag = length(flowVec);
                return _Brightness * flowMag;
            }

            float3 getRawTexelFromTileWithCentre(float2 loc, float dist)
            {
                // Sample the flow from the centre of the tile
                float2 flowVec  = -1.0 * ( tex2D(_VectorField, loc).rg  - float2(0.5, 0.5) );

                // Find direction and magnitude of this flow
                float flowMag = length(flowVec);
                float2 flowDir = normalize(flowVec);
                float2 flowSide = float2(flowDir.y, -flowDir.x);

                // Use UV of exact pixel position to lookup a UV on the fluid texture
                float scalar = 1.0;///200.0;
                float2 fragVec = scalar * (fragGridCellFrac-midGrid);
                float2 fluidTexUV = float2( dot(flowDir,  fragVec), 
                                        dot(flowSide, fragVec) );

                // Scroll the UV in the direction of flow on texture (X)
                fluidTexUV.x += flowMag * _Speed * scalar * _Time;

                fluidTexUV = fluidTexUV / _TextureScale / flowMag;

                fluidTexUV /= scalar;
//                fluidTexUV /= dist;

                float3 texPixel = tex2D(_Tex, fluidTexUV).rgb;

                return texPixel;
            }

			v2f vert (appdata v) {
                v2f o;
                // transform position to clip space
                // (multiply with model*view*projection matrix)
                o.vertex = UnityObjectToClipPos(v.vertex);
               
                // Transform the vertex coordinates from model space into world space
//	            float4 vv = mul( _Object2World, v.vertex );
	 
	            // Now adjust the coordinates to be relative to the camera position
//	            vv.xyz -= _WorldSpaceCameraPos.xyz;

	            // Now apply the offset back to the vertices in model space
//	            o.vertex += mul(_World2Object, vv);


//				o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
				UNITY_TRANSFER_FOG(o,o.vertex);

				o.projPos = ComputeScreenPos (o.vertex);

	             // just pass the texture coordinate
                o.uv = v.uv;
                return o;
            }

//            float GetDistance (v2f i) {
//				return LinearEyeDepth (SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
//			}

            float4 DistanceFade (v2f i, float4 col) {
//				if(_FadeEnd > 0) {
					// Distance to camera near clip plane
//					float sceneZ = GetDistance(i);
					// Distance to camera
					float partZ = i.projPos.z;
//					float dist = (partZ-sceneZ);
					float diff = saturate((partZ - _FadeStart) / (_FadeEnd - _FadeStart));
//					col = lerp(_FadeColor, col, diff);
					col.rgb *= lerp(_FadeColor.rgb, float3(1,1,1), diff);
//					col.a *= diff;
//				}
				return col;
			}

			fixed4 frag (v2f i) : SV_Target {
				cellSizeNorm = float2(1.0,1.0) / _GridCellCount;

                fragGridCellFrac = i.uv * _GridCellCount;

                float2 cellCoordBaseIdx = floor(fragGridCellFrac);
                midGrid = float2(0.5, 0.5) * _GridCellCount;

                float2 topLeftNorm = cellCoordBaseIdx / _GridCellCount;
                float2 botRightNorm = (cellCoordBaseIdx + float2(1.0,1.0)) / _GridCellCount;
                float2 topRightNorm = float2( botRightNorm.x, topLeftNorm.y );
                float2 botLeftNorm = float2( topLeftNorm.x, botRightNorm.y );

                float dist = i.projPos.z;
                float3 texelFromTopLeftTile  = getRawTexelFromTileWithCentre(topLeftNorm, dist);
                float3 texelFromBotRightTile = getRawTexelFromTileWithCentre(botRightNorm, dist);
                float3 texelFromTopRightTile = getRawTexelFromTileWithCentre(topRightNorm, dist);
                float3 texelFromBotLeftTile  = getRawTexelFromTileWithCentre(botLeftNorm, dist);

                float texelStrengthFromTopLeftTile  = getTexelStrengthFromTileWithCentre(topLeftNorm);
                float texelStrengthFromBotRightTile = getTexelStrengthFromTileWithCentre(botRightNorm);
                float texelStrengthFromTopRightTile = getTexelStrengthFromTileWithCentre(topRightNorm);
                float texelStrengthFromBotLeftTile  = getTexelStrengthFromTileWithCentre(botLeftNorm);

                // THIS ALL WORKS!
                float2 xyInCell = fragGridCellFrac - cellCoordBaseIdx;

                float smoothX = smoothstep(0.0, 1.0, xyInCell.x);
                float smoothY = smoothstep(0.0, 1.0, xyInCell.y);

                float3 top = lerp(texelFromTopLeftTile, texelFromTopRightTile, smoothX);
                float3 bot = lerp(texelFromBotLeftTile, texelFromBotRightTile, smoothX);
                float4 pathColor = float4( lerp(top, bot, smoothY), 1.0);

                float topStrength = lerp(texelStrengthFromTopLeftTile, texelStrengthFromTopRightTile, smoothX);
                float botStrength = lerp(texelStrengthFromBotLeftTile, texelStrengthFromBotRightTile, smoothX);
                float pathStrength = lerp(topStrength, botStrength, smoothY);
//                if(pathStrength < 0.02) pathStrength = 0;
//                pathStrength = max(0.01, pathStrength);
                float opacity = pathStrength * (pathStrength);
                float textureBrightness = (pathColor.r+pathColor.g+pathColor.b) * 0.33;
                opacity *= textureBrightness;
//                opacity *= 2;
                pathColor.rgb = lerp(_WeakColor.rgb, _StrongColor.rgb, pathStrength * 0.5);
                pathColor.rgb += lerp(_DarkColor.rgb, _LightColor.rgb, textureBrightness * 3 - 1);
//                pathColor.rgb *= 1 + opacity;
//                pathColor.rgb += lerp(float3(0,0,0), _StrongColor.rgb, opacity);
                pathColor.a = opacity * 3;

                pathColor = DistanceFade(i, pathColor);
                return pathColor;
//                float4 finalColor = lerp(_StrongColor.rgb, pathColor.rgb, pathColor.a);
//                return float4 (finalColor, lerp(_BaseColor.a, _Color.a, pathColor.a));
			}
			ENDCG
		}
	}
}