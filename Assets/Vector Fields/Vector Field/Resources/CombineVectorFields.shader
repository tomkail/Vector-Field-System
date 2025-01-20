Shader "Custom/CombineVectorFields"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VectorField ("Vector Field", 2D) = "black" {}
        _Strength ("Strength", Float) = 1
        [Enum(Add,0,Blend,1)] _BlendMode ("Blend Mode", Int) = 0
        [Enum(All,0,Magnitude,1,Direction,2)] _Components ("Components", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Blend Off

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

            sampler2D _MainTex;
            sampler2D _VectorField;
            float4x4 _RelativeTransform;
            float _Strength;
            int _BlendMode;
            int _Components;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            float2 BlendVectors(float2 current, float2 vectorB, float strength, float blendMode, float components)
            {
                float2 result = current;

                if (blendMode == 0) // Add
                {
                    if (components == 0) // All
                        result = current + vectorB * strength;
                    else if (components == 1) // Magnitude
                        result = current + normalize(current) * length(vectorB) * strength;
                    else if (components == 2) // Direction
                        result = vectorB + length(current) * normalize(vectorB) * strength;
                }
                else if (blendMode == 1) // Blend
                {
                    if (components == 0) // All
                        result = lerp(current, vectorB, strength);
                    else if (components == 1) // Magnitude
                        result = normalize(current) * strength;
                    else if (components == 2) // Direction
                        result = normalize(vectorB) * length(current) * strength;
                }

                return result;
            }
            float2 Rotate2D(float2 v, float theta)
            {
                float cosTheta = cos(theta);
                float sinTheta = sin(theta);
                float2x2 rotationMatrix = float2x2(
                    cosTheta, -sinTheta,
                    sinTheta, cosTheta
                );
                return mul(rotationMatrix, v);
            }
            
            float3x3 ExtractRotation(float4x4 m)
            {
                // Extracting the upper-left 3x3 matrix (rotation part)
                float3x3 rotationMatrix = float3x3(
                    m[0].xyz,
                    m[1].xyz,
                    m[2].xyz
                );
            
                return rotationMatrix;
            }
            
            float3x3 Inverse3x3(float3x3 m)
            {
                // The inverse of a rotation matrix is its transpose
                return transpose(m);
            }
            
            float4 frag (v2f i) : SV_Target
            {
                
                // Sample current vector field
                float2 currentVector = tex2D(_MainTex, i.uv).rg;

                // Sample new vector field if UV is within bounds
                // Note that this transformation doesn't seem to work when rotated in 3D.
                float2 normalizedUV = mul(_RelativeTransform, float4(i.uv, 0, 1));
                if (all(normalizedUV >= 0 && normalizedUV <= 1))
                {
                    float2 newVector = tex2D(_VectorField, normalizedUV).rg;
                    
                    // Convert to -1,1
                    currentVector = (currentVector - 0.5) * 2;
                    newVector = (newVector - 0.5) * 2;
                    
                    
                    
                    // Rotate the newVector according to the matrix so that it respects its own transform
                    float3x3 invRotationMatrix = Inverse3x3(ExtractRotation(_RelativeTransform));
                    newVector = mul(invRotationMatrix, newVector);
                    
                    // Blend vectors
                    float2 result = BlendVectors(currentVector, newVector, _Strength, _BlendMode, _Components);
                    
                    // Convert to 0,1
                    result = (result / 2) + 0.5;
                    
                    return float4(result, 0, 1);
                } else {
                    return float4(currentVector, 0, 1);
                }
            }
            ENDCG
        }
    }
}