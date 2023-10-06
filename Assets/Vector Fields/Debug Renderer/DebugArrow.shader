Shader "VectorField/InstanceDebugRenderer" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader {
        Tags { "RenderType"="Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha // enable transparency
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };
            
            struct MyStruct
            {
                int2 coord;
                float2 value;
            };

            StructuredBuffer<float4x4> matrixBuffer;
            StructuredBuffer<float4> colorBuffer;
            StructuredBuffer<MyStruct> dataBuffer;
            sampler2D _MainTex;
            float4x4 gridToWorldMatrix;
            int gridWidth;
            float3 scaleFactor;
            float maxMagnitude;
            float _Opacity;


            // Function to construct a translation matrix
            float4x4 TranslationMatrix(float3 translation)
            {
                return float4x4(
                    1, 0, 0, translation.x,
                    0, 1, 0, translation.y,
                    0, 0, 1, translation.z,
                    0, 0, 0, 1
                );
            }
            
            float4x4 ScaleMatrix(float3 scale)
            {
                return float4x4(
                    scale.x, 0.0f, 0.0f, 0.0f,
                    0.0f, scale.y, 0.0f, 0.0f,
                    0.0f, 0.0f, scale.z, 0.0f,
                    0.0f, 0.0f, 0.0f, 1.0f
                );
            }
            
            // Function to rotate a point around an axis by an angle
            float4x4 RotateAroundAxis(float3 axis, float2 direction)
            {
                float c = direction.y;
                float s = -direction.x;
                float t = 1.0f - c;
            
                float x = axis.x;
                float y = axis.y;
                float z = axis.z;
            
                return float4x4(
                    t*x*x + c,   t*x*y - s*z, t*x*z + s*y, 0,
                    t*x*y + s*z, t*y*y + c,   t*y*z - s*x, 0,
                    t*x*z - s*y, t*y*z + s*x, t*z*z + c,   0,
                    0,           0,           0,           1
                );
            }
            
            float3 GetLossyScale(float4x4 _matrix) {
                float3 scale;
                scale.x = length(float3(_matrix._11, _matrix._12, _matrix._13));
                scale.y = length(float3(_matrix._21, _matrix._22, _matrix._23));
                scale.z = length(float3(_matrix._31, _matrix._32, _matrix._33));
                return scale;
            }
            
            float4 DirectionToColor(float2 dir, float maxMagnitude)
            {
                // Calculate the angle between the vector and the up direction (0, 1)
                float angle = atan2(dir.y, dir.x) - atan2(1.0, 0.0);
                angle = degrees(angle);
                // This is so that R points right and G points up
                angle += 90;
                if (angle < 0.0) angle += 360.0;
            
                // Calculate hue, saturation, and lightness
                float hue = angle;
                float saturation = 1.0;
                float lightness = 0.5;
            
                // Calculate opacity based on magnitude
                float opacity = (length(dir) / maxMagnitude) * _Opacity;
            
                // Convert HSL to RGB (assuming H in [0, 360], S and L in [0, 1])
                float C = (1 - abs(2 * lightness - 1)) * saturation;
                float X = C * (1 - abs(fmod(hue / 60, 2) - 1));
                float m = lightness - C/2;
            
                float3 rgb;
                
                if (0 <= hue && hue < 60) rgb = float3(C, X, 0) + m;
                else if (60 <= hue && hue < 120) rgb = float3(X, C, 0) + m;
                else if (120 <= hue && hue < 180) rgb = float3(0, C, X) + m;
                else if (180 <= hue && hue < 240) rgb = float3(0, X, C) + m;
                else if (240 <= hue && hue < 300) rgb = float3(X, 0, C) + m;
                else if (300 <= hue && hue <= 360) rgb = float3(C, 0, X) + m;
            
                return float4(rgb, opacity);
            }

            v2f vert (appdata v) {
                v2f o;
                
                float3 cellPoint = float3(v.instanceID % gridWidth, floor(v.instanceID / gridWidth), 0);
                float3 cellValue = float3(dataBuffer[v.instanceID].value.x, dataBuffer[v.instanceID].value.y, 0);
                
                float3 worldPoint = mul(gridToWorldMatrix, float4(cellPoint, 0)).xyz;
                
                float3 rotationAxis = float3(0,0,1);
                float4x4 rotationMatrix = mul(gridToWorldMatrix, RotateAroundAxis(rotationAxis, cellValue));
                
                float4x4 transformation = mul(TranslationMatrix(worldPoint), rotationMatrix);
                transformation = mul(transformation, ScaleMatrix(scaleFactor));
                
                //float4x4 transformation = matrixBuffer[v.instanceID];
                o.vertex = UnityObjectToClipPos(mul(transformation, v.vertex));
                o.uv = v.uv;
                o.instanceID = v.instanceID;
                return o;
            }

            half4 frag (v2f i) : SV_Target {
                half4 sampledColor = tex2D(_MainTex, i.uv);
                half4 color = DirectionToColor(dataBuffer[i.instanceID].value, maxMagnitude);
                //half4 color = colorBuffer[i.instanceID];
                return sampledColor * color;
            }
            ENDCG
        }
    }
}