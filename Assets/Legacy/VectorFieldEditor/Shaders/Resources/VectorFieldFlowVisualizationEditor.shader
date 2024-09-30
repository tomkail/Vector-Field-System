// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "El and Six/Vector Field/Vector Field Flow Visualization Editor" {
	Properties {
		_MainTex ("Vector Field", 2D) = "white" {}
		_Tex ("Texture", 2D) = "white" {}
		_BackgroundTex ("Background", 2D) = "black" {}
		_Rect ("Rect", Vector) = (0,0,1,1)
		_GridCellCount ("Grid Cell Count", Range(0,10000)) = 400.0
		_Speed ("Speed", Range(0,500)) = 20
		_TextureScale ("Texture Scale", Range(0,1000)) = 10
		_Brightness ("Brightness", Range(0,50)) = 8
	}

	SubShader {

//	Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
//         Blend SrcAlpha OneMinusSrcAlpha
		Tags { "RenderType"="Opaque" }

		Pass {
			ZTest Always
			Fog { Mode Off }

			CGPROGRAM
//			#include "VectorFieldFlow.cginc" 




			uniform sampler2D _MainTex;
			uniform sampler2D _Tex;
			uniform float _AnimationTime;
			uniform float4 _Rect;
			uniform float _GridCellCount;
			uniform float _Speed;
			uniform float _TextureScale;
			uniform float _Brightness;

			// TODO: Pull out to varying
			float2 fragGridCellFrac;

			float2 cellSizeNorm;
			float2 midGrid;

			float3 rgb2hsv(float3 c) {
			    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
			    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
			    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

			    float d = q.x - min(q.w, q.y);
			    float e = 1.0e-10;
			    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
			}

			float3 hsv2rgb(float3 c) {
			    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
			    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
			    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
			}

			float vec2angle(float2 a) {
				return degrees(atan2(-(- a.y), - a.x));
			}

			float3 getTexelFromTileWithCentre(float2 loc)
			{
			    // Sample the flow from the centre of the tile
			    float2 flowVec  = -1.0 * ( tex2D(_MainTex, loc).rg  - float2(0.5, 0.5) );

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
			    fluidTexUV.x += flowMag * _Speed * scalar * _AnimationTime;

			    fluidTexUV = fluidTexUV / _TextureScale;

			    fluidTexUV /= scalar;

			    float3 texPixel = tex2D(_Tex, fluidTexUV).rgb;

			    float angle = (vec2angle(flowDir));
				float val = lerp(0.75, 1.5, flowMag);
			    float3 hsv = float3(angle/360, flowMag * 2, val);
			    float3 tex = hsv2rgb(hsv);
			    return tex * _Brightness * flowMag * texPixel;
			    // Darken where the flow slows down
			    return _Brightness * flowMag * texPixel;
			}

			float4 CalculateFrag(float2 uv) {
				cellSizeNorm = float2(1.0,1.0) / _GridCellCount;

			    fragGridCellFrac = uv * _GridCellCount;

			    float2 cellCoordBaseIdx = floor(fragGridCellFrac);
			    midGrid = float2(0.5, 0.5) * _GridCellCount;

			    float2 topLeftNorm = cellCoordBaseIdx / _GridCellCount;
			    float2 botRightNorm = (cellCoordBaseIdx + float2(1.0,1.0)) / _GridCellCount;
			    float2 topRightNorm = float2( botRightNorm.x, topLeftNorm.y );
			    float2 botLeftNorm = float2( topLeftNorm.x, botRightNorm.y );

			    float3 texelFromTopLeftTile  = getTexelFromTileWithCentre(topLeftNorm);
			    float3 texelFromBotRightTile = getTexelFromTileWithCentre(botRightNorm);
			    float3 texelFromTopRightTile = getTexelFromTileWithCentre(topRightNorm);
			    float3 texelFromBotLeftTile  = getTexelFromTileWithCentre(botLeftNorm);

			    float2 xyInCell = fragGridCellFrac - cellCoordBaseIdx;

			    float smoothX = smoothstep(0.0, 1.0, xyInCell.x);
			    float smoothY = smoothstep(0.0, 1.0, xyInCell.y);

			    float3 top = lerp(texelFromTopLeftTile, texelFromTopRightTile, smoothX);
			    float3 bot = lerp(texelFromBotLeftTile, texelFromBotRightTile, smoothX);
			    return float4( lerp(top, bot, smoothY), 1);
			}


			#pragma fragment frag
			#pragma vertex vert

			uniform float _EditorTime;
			uniform sampler2D _BackgroundTex;

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
				_AnimationTime = _Time.y;
				_AnimationTime = _EditorTime;
				float4 col = tex2D(_BackgroundTex, i.uv);
				float4 vectorField = CalculateFrag(i.uv);
//				vectorField.normalize
//				col = lerp(col, vectorField, vectorField.r);
				col += vectorField;
				return col;
			}


			ENDCG
		}
	}
}