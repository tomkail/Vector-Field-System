Shader "Custom/VectorFieldWaveVisualizer"
{
    Properties
    {
        _VectorField ("Vector Field", 2D) = "white" {}
        [KeywordEnum(Texture, Stripes, Noise)] _PatternType ("Pattern Type", Float) = 0
        _WaveTexture ("Wave Pattern", 2D) = "white" {}
        _WaveFrequency ("Wave Frequency", Float) = 10.0
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.5
        _WaveColorA ("Wave Color A", Color) = (1,0,0,1)
        _WaveColorB ("Wave Color B", Color) = (1,0.5,0,1)
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
            float _WaveFrequency;
            float _WaveSpeed;
            float _WaveStrength;
            float4 _WaveColorA;
            float4 _WaveColorB;
            float4 _BackgroundColor;

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

                float2 u = f*f*(3.0-2.0*f);

                return lerp(lerp(dot(hash(i + float2(0.0,0.0)), f - float2(0.0,0.0)),
                               dot(hash(i + float2(1.0,0.0)), f - float2(1.0,0.0)), u.x),
                          lerp(dot(hash(i + float2(0.0,1.0)), f - float2(0.0,1.0)),
                               dot(hash(i + float2(1.0,1.0)), f - float2(1.0,1.0)), u.x), u.y);
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
                // Sample the vector field
                float4 vectorSample = tex2D(_VectorField, i.uv);
                float2 flowDir = (vectorSample.xy - 0.5) * 2.0;
                float magnitude = length(flowDir);
                flowDir = magnitude > 0.001 ? flowDir / magnitude : float2(0, 0);

                // Calculate wave UV coordinates
                float2 waveUV = i.uv + flowDir * _Time.y * _WaveSpeed * magnitude;

                // Generate pattern based on selected type
                float pattern;
                #if _PATTERNTYPE_TEXTURE
                    pattern = tex2D(_WaveTexture, waveUV).r;
                #elif _PATTERNTYPE_STRIPES
                    float projection = dot(waveUV, flowDir);
                    pattern = sin(projection * _WaveFrequency - _Time.y * _WaveSpeed);
                    pattern = pattern * 0.5 + 0.5; // Convert to [0,1] range
                #else // NOISE
                    pattern = noise(waveUV * _WaveFrequency + _Time.y * _WaveSpeed);
                    pattern = pattern * 0.5 + 0.5; // Convert to [0,1] range
                #endif

                // Lerp between colors based on pattern value
                float4 waveColor = lerp(_WaveColorA, _WaveColorB, pattern);

                // Use magnitude to blend between background and wave colors
                float4 finalColor = lerp(_BackgroundColor, waveColor, magnitude * _WaveStrength);

                return finalColor;
            }
            ENDCG
        }
    }
}
