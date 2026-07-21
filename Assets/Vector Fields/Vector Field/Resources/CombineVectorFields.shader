Shader "Hidden/Vector Fields/Combine Vector Fields"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VectorField ("Vector Field", 2D) = "black" {}
        _Strength ("Strength", Float) = 1
        [Enum(Add,0,Blend,1)] _BlendMode ("Blend Mode", Int) = 0
        // Bitmask: Magnitude = 1, Direction = 2, All = 3. Set from script per layer.
        _Components ("Components", Int) = 3
        // Alignment ramp (modulation path only): x = (dot(currentDir, incomingDir) + 1) * 0.5 (0 = opposed, 1 = aligned).
        _AlignmentRamp ("Alignment Ramp", 2D) = "white" {}
        [Toggle] _ScaleByFieldMagnitude ("Scale By Field Magnitude", Int) = 0
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
            // Compiled out unless a layer actually uses the alignment ramp or the field-magnitude coupling.
            #pragma multi_compile _ VF_MODULATION

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
            float4x4 _VectorRotation;
            float _Strength;
            int _BlendMode;
            int _Components;
            #ifdef VF_MODULATION
            sampler2D _AlignmentRamp;
            int _ScaleByFieldMagnitude;
            #endif
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            float2 SafeNormalize(float2 v)
            {
                float len = length(v);
                return len > 1e-6 ? v / len : float2(0, 0);
            }

            // Canonical per-vector blend. Must stay in sync with GroupVectorFieldComponent.BlendVector (C#).
            // _Components is a bitmask: Magnitude = 1, Direction = 2, All = 3. The two aspects compose.
            float2 BlendVectors(float2 current, float2 vectorB, float strength, int blendMode, int components)
            {
                bool hasMagnitude = (components & 1) != 0;
                bool hasDirection = (components & 2) != 0;
                if (!hasMagnitude && !hasDirection)
                    return current;

                if (blendMode == 0) // Add
                {
                    if (hasMagnitude && hasDirection)
                        return current + vectorB * strength;
                    if (hasMagnitude) // lengthen along current direction by incoming magnitude
                        return current + SafeNormalize(current) * length(vectorB) * strength;
                    // direction only: push current's magnitude toward the incoming direction
                    return current + SafeNormalize(vectorB) * length(current) * strength;
                }
                else // Blend
                {
                    if (hasMagnitude && hasDirection)
                        return lerp(current, vectorB, strength);
                    if (hasMagnitude) // keep current direction, blend length toward incoming
                        return SafeNormalize(current) * lerp(length(current), length(vectorB), strength);
                    // direction only: rotate current toward incoming direction, keep current magnitude
                    return SafeNormalize(lerp(SafeNormalize(current), SafeNormalize(vectorB), strength)) * length(current);
                }
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
                    
                    
                    
                    // Rotate the sampled (layer-local) vector into the group's frame and project onto the group
                    // plane. Lifting to 3D (z = 0), rotating, then dropping z mirrors the CPU path
                    // (TransformDirection -> InverseTransformDirection -> Vector2), so tilted/scaled layers compose
                    // correctly. _VectorRotation is a pure rotation, so scale never corrupts the direction.
                    newVector = mul((float3x3)_VectorRotation, float3(newVector, 0)).xy;

                    float strength = _Strength;
                    #ifdef VF_MODULATION
                    // Per-layer modulators (orthogonal to the blend mode), mirror of BlendVector (C#): the alignment
                    // ramp scales strength by current/incoming alignment, the coupling scales the incoming vector by
                    // the underlying flow speed (so a layer only acts where there's flow and grows with it).
                    float alignment = dot(SafeNormalize(currentVector), SafeNormalize(newVector));
                    strength *= tex2D(_AlignmentRamp, float2(saturate(alignment * 0.5 + 0.5), 0.5)).r;
                    if (_ScaleByFieldMagnitude != 0) newVector *= length(currentVector);
                    #endif

                    // Blend vectors
                    float2 result = BlendVectors(currentVector, newVector, strength, _BlendMode, _Components);
                    
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