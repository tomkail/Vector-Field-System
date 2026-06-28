Shader "VectorField/InstanceDebugRenderer" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
    }

    // Shared program body, included by both the URP and Built-in SubShaders below.
    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        uint instanceID : SV_InstanceID;
    };

    struct v2f {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 value : TEXCOORD1;
        float alpha : TEXCOORD2;
    };

    sampler2D _MainTex;
    sampler2D _FieldTex;     // the vector field RenderTexture, vectors encoded in RG as (v * 0.5 + 0.5)
    float4x4 gridToWorldMatrix;
    float3 scaleFactor;
    float maxMagnitude;
    float _Opacity;
    float2 fieldSize;        // field resolution in cells (= RenderTexture size)
    int displayWidth;        // arrows drawn along x at the current LOD
    int baseStride;          // cells per arrow (power of two)
    float detailFade;        // LOD cross-fade weight for arrows not shared with the coarser octave

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

    // Builds a rotation from a direction vector. Because `direction` is not normalised, the arrow's
    // length ends up proportional to the vector's magnitude.
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

        // Reconstruct this arrow's cell from the instance id and the current LOD.
        uint w = (uint)displayWidth;
        uint ix = v.instanceID % w;
        uint iy = v.instanceID / w;
        float2 cell = float2(ix, iy) * baseStride;

        // Sample the vector straight from the field texture (texel centre) and decode it.
        float2 uv = (cell + 0.5) / fieldSize;
        float2 value = (tex2Dlod(_FieldTex, float4(uv, 0, 0)).rg - 0.5) * 2.0;

        float3 worldPoint = mul(gridToWorldMatrix, float4(cell, 0, 0)).xyz;

        float3 rotationAxis = float3(0,0,1);
        float4x4 rotationMatrix = mul(gridToWorldMatrix, RotateAroundAxis(rotationAxis, value));

        float4x4 transformation = mul(TranslationMatrix(worldPoint), rotationMatrix);
        transformation = mul(transformation, ScaleMatrix(scaleFactor));

        o.vertex = UnityObjectToClipPos(mul(transformation, v.vertex));
        o.uv = v.uv;
        o.value = value;
        // Arrows shared with the next-coarser octave (even index on both axes) stay solid; the rest fade.
        bool survivesToCoarser = ((ix & 1u) == 0u) && ((iy & 1u) == 0u);
        o.alpha = survivesToCoarser ? 1.0 : detailFade;
        return o;
    }

    half4 frag (v2f i) : SV_Target {
        half4 sampledColor = tex2D(_MainTex, i.uv);
        half4 color = DirectionToColor(i.value, maxMagnitude);
        color.a *= i.alpha; // LOD cross-fade weight
        return sampledColor * color;
    }
    ENDCG

    // URP: a pass tagged SRPDefaultUnlit is what the Universal renderer draws for unlit materials.
    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Pass {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            ENDCG
        }
    }

    // Built-in render pipeline (current).
    SubShader {
        Tags { "Queue" = "Overlay" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha // enable transparency
            ZWrite Off
            Cull Off
            ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            ENDCG
        }
    }
}
