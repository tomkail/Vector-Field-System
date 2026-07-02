Shader "Custom/VectorFieldWavesVisualizer"
{
    Properties
    {
        _VectorField ("Vector Field", 2D) = "bump" {}
        _WaveFrequency ("Wave Frequency", Float) = 10.0
        _WaveSpeed ("Wave Speed", Float) = 1.0
        _Direction ("Direction", Vector) = (0,1,0,0)
        _BackgroundColor ("Background Color", Color) = (0,0,1,1)
        _WaveColorA ("Wave Color A", Color) = (1,0,0,1)  // Red
        _WaveColorB ("Wave Color B", Color) = (1,0.5,0,1)  // Orange
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

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
            float _WaveFrequency;
            float _WaveSpeed;
            float4 _Direction;
            float4 _BackgroundColor;
            float4 _WaveColorA;
            float4 _WaveColorB;

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
                float4 vectorField = tex2D(_VectorField, i.uv);

                // Convert from [0,1] to [-1,1] range
                float2 fieldDirection = float2(vectorField.r * 2.0 - 1.0, vectorField.g * 2.0 - 1.0);

                // Calculate magnitude
                float magnitude = length(fieldDirection);

                // Normalize the field direction (avoid division by zero)
                float2 dir = magnitude > 0.001 ? fieldDirection / magnitude : float2(0, 0);

                // Project UV onto direction vector
                float projection = dot(i.uv, dir);

                // Create wave pattern moving in specified direction
                float wave = sin(projection * _WaveFrequency - _Time.y * _WaveSpeed);
                wave = wave * 0.5 + 0.5; // Convert to [0,1] range

                // Lerp between colors based on wave value
                float4 waveColor = lerp(_WaveColorA, _WaveColorB, wave);

                // Use magnitude to blend between background and wave colors
                float4 finalColor = lerp(_BackgroundColor, waveColor, magnitude);

                return finalColor;
            }
            ENDCG
        }
    }
}
