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
        _OctaveCount ("Octave Count", Range(1, 4)) = 2
        _OctaveScale ("Octave Scale", Range(1, 4)) = 2
        _OctaveInfluence ("Octave Influence", Range(0, 1)) = 0.5
        _CellBlend ("Cell Blend", Range(0, 1)) = 0.1
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
            float _OctaveCount;
            float _OctaveScale;
            float _OctaveInfluence;
            float _CellBlend;

            // Modified noise functions for seamless tiling
            float2 hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return -1.0 + 2.0 * frac((p3.xx + p3.yz) * p3.zy);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                // Cubic Hermine curve
                float2 u = f * f * (3.0 - 2.0 * f);

                // Sample the four corners
                float a = dot(hash(i + float2(0,0)), f - float2(0,0));
                float b = dot(hash(i + float2(1,0)), f - float2(1,0));
                float c = dot(hash(i + float2(0,1)), f - float2(0,1));
                float d = dot(hash(i + float2(1,1)), f - float2(1,1));

                // Bilinear interpolation with smooth curve
                return lerp(lerp(a, b, u.x),
                          lerp(c, d, u.x), u.y) * 0.5 + 0.5;
            }

            float getPattern(float2 uv)
            {
                #if _PATTERNTYPE_TEXTURE
                    return tex2D(_WaveTexture, uv).r;
                #elif _PATTERNTYPE_STRIPES
                    return frac(uv.x * 5) < 0.5;
                #elif _PATTERNTYPE_NOISE
                    // Use multiple noise octaves for more natural look
                    float result = 0;
                    float amplitude = 1.0;
                    float frequency = 5.0;
                    float total = 0;

                    for(int i = 0; i < 3; i++)
                    {
                        result += noise(uv * frequency) * amplitude;
                        total += amplitude;
                        amplitude *= 0.5;
                        frequency *= 2.0;
                    }

                    return result / total;
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

            float4 calculateCell(float2 cellCenter, float2 localUV, float gridSize)
            {
                // Sample vector field at cell center
                float4 vectorSample = tex2D(_VectorField, cellCenter);
                float2 flowDir = -((vectorSample.xy - 0.5) * 2.0);
                float magnitude = length(flowDir);
                flowDir = magnitude > 0.001 ? flowDir / magnitude : float2(0, 0);

                // Create scrolling wave pattern
                float2 waveUV = localUV;
                float2 scrollDir = flowDir;

                if (_RotatePattern > 0)
                {
                    waveUV = rotateUV(waveUV, flowDir);
                    float angle = atan2(flowDir.y, flowDir.x) - UNITY_PI * 0.5;
                    scrollDir = float2(1,0);
                }

                // Scale the wave speed inversely with grid size
                // float speedScale = gridSize/_GridSize;
                float speedScale = 1;
                waveUV += scrollDir * _Time.y * _WaveSpeed * magnitude * speedScale;
                waveUV = waveUV * _WaveScale;

                float wave = getPattern(waveUV);
                return lerp(_BackgroundColor, _WaveColor, wave * magnitude * _WaveStrength);
            }

            float4 calculateOctave(float2 uv, float gridSize)
            {
                // Calculate grid cell
                float2 cell = floor(uv * gridSize);
                float2 localUV = frac(uv * gridSize);

                // Calculate blend factors
                float2 blend = smoothstep(_CellBlend, 1.0 - _CellBlend, localUV);

                // Sample current cell and neighbors
                float2 cellCenter = (cell + 0.5) / gridSize;
                float2 rightCenter = (cell + float2(1.0, 0.0) + 0.5) / gridSize;
                float2 topCenter = (cell + float2(0.0, 1.0) + 0.5) / gridSize;
                float2 topRightCenter = (cell + float2(1.0, 1.0) + 0.5) / gridSize;

                // Calculate pattern for each cell
                float4 current = calculateCell(cellCenter, localUV, gridSize);
                float4 right = calculateCell(rightCenter, localUV - float2(1.0, 0.0), gridSize);
                float4 top = calculateCell(topCenter, localUV - float2(0.0, 1.0), gridSize);
                float4 topRight = calculateCell(topRightCenter, localUV - float2(1.0, 1.0), gridSize);

                // Blend horizontally
                float4 bottomBlend = lerp(current, right, 1.0 - blend.x);
                float4 topBlend = lerp(top, topRight, 1.0 - blend.x);

                // Blend vertically
                return lerp(bottomBlend, topBlend, 1.0 - blend.y);
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
                float4 finalColor = float4(0,0,0,0);
                float totalInfluence = 0;
                float influence = 1.0;
                float gridSize = _GridSize;

                // Calculate multiple octaves
                for(int oct = 0; oct < _OctaveCount; oct++)
                {
                    finalColor += calculateOctave(i.uv, gridSize) * influence;
                    totalInfluence += influence;

                    gridSize *= _OctaveScale;
                    influence *= _OctaveInfluence;
                }

                // Normalize the result
                return finalColor / totalInfluence;
            }
            ENDCG
        }
    }
}
