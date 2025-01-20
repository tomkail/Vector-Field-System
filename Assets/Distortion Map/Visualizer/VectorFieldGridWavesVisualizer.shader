Shader "Custom/VectorFieldGridWaveVisualizer"
{
    Properties
    {
        _VectorField ("Vector Field", 2D) = "white" {}
        _GridSize ("Grid Size", Float) = 10.0
        [KeywordEnum(Texture, Stripes, Noise)] _PatternType ("Pattern Type", Float) = 0
        [Toggle] _RotatePattern ("Rotate Pattern", Float) = 0
        _WaveTexture ("Wave Pattern", 2D) = "white" {}
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveScale ("Wave Scale", Float) = 1.0
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.5
        _WaveColor ("Wave Color", Color) = (1,1,1,1)
        _BackgroundColor ("Background Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _PATTERNTYPE_TEXTURE _PATTERNTYPE_STRIPES _PATTERNTYPE_NOISE
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
            };

            sampler2D _VectorField;
            sampler2D _WaveTexture;
            float _GridSize;
            float _WaveSpeed;
            float _WaveScale;
            float _WaveStrength;
            float4 _WaveColor;
            float4 _BackgroundColor;
            float _RotatePattern;

            // Simple noise function
            float2 hash(float2 p)
            {
                p = float2(dot(p,float2(127.1,311.7)),
                          dot(p,float2(269.5,183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(dot(hash(i + float2(0,0)), f - float2(0,0)),
                               dot(hash(i + float2(1,0)), f - float2(1,0)), u.x),
                          lerp(dot(hash(i + float2(0,1)), f - float2(0,1)),
                               dot(hash(i + float2(1,1)), f - float2(1,1)), u.x), u.y);
            }

            float getPattern(float2 uv)
            {
                #if _PATTERNTYPE_TEXTURE
                    return tex2D(_WaveTexture, uv).r;
                #elif _PATTERNTYPE_STRIPES
                    return frac(uv.x * 5) < 0.5;
                #elif _PATTERNTYPE_NOISE
                    return noise(uv * 5) * 0.5 + 0.5;
                #endif
                return 0;
            }

            // Modified rotation function
            float2 rotateUV(float2 uv, float2 flowDir)
            {
                // Convert vector to angle (in radians)
                float angle = atan2(flowDir.y, flowDir.x);

                // Convert back to direction vector
                float2 rotatedFlow = float2(cos(angle), sin(angle));

                float2x2 rotationMatrix;
                rotationMatrix[0] = float2(rotatedFlow.x, rotatedFlow.y);
                rotationMatrix[1] = float2(rotatedFlow.y, -rotatedFlow.x);
                return mul(rotationMatrix, uv - 0.5) + 0.5;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Calculate grid cell
                float2 cellSize = 1.0 / _GridSize;
                float2 cell = floor(i.uv * _GridSize);
                float2 cellUV = cell / _GridSize;
                float2 cellCenter = (cell + 0.5) / _GridSize;

                // Sample vector field at cell center
                float4 vectorSample = tex2D(_VectorField, cellCenter);
                float2 flowDir = -((vectorSample.xy - 0.5) * 2.0); // Negated the flow direction
                float magnitude = length(flowDir);

                // Normalize flow direction (avoid division by zero)
                flowDir = magnitude > 0.001 ? flowDir / magnitude : float2(0, 0);

                // Calculate local UV coordinates within the cell
                float2 localUV = frac(i.uv * _GridSize);

                // Create scrolling wave pattern
                float2 waveUV = localUV;
                if (_RotatePattern > 0)
                {
                    waveUV = rotateUV(waveUV, flowDir);
                }
                waveUV += flowDir * _Time.y * _WaveSpeed * magnitude;
                waveUV = waveUV * _WaveScale;

                float wave = getPattern(waveUV);

                // Blend between background and wave color based on magnitude
                float4 finalColor = lerp(_BackgroundColor, _WaveColor, wave * magnitude * _WaveStrength);

                return finalColor;
            }
            ENDCG
        }
    }
}
