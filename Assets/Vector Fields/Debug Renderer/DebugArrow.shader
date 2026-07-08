Shader "VectorField/InstanceDebugRenderer" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        // Blend factors, driven from VectorFieldDebugRenderer so the Invert Background colour mode can switch the pass
        // from straight alpha-over to a destination invert. Defaults reproduce the normal transparent blend.
        [HideInInspector] _SrcBlend ("__src", Float) = 5  // SrcAlpha
        [HideInInspector] _DstBlend ("__dst", Float) = 10 // OneMinusSrcAlpha
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
        float2 cellPos : TEXCOORD3;   // this fragment's position in grid-cell space, for clipping to the field bounds
    };

    sampler2D _MainTex;
    sampler2D _FieldTex;     // the vector field RenderTexture, vectors encoded in RG as (v * 0.5 + 0.5)
    float4x4 gridToWorldMatrix;
    float3 scaleFactor;
    float maxMagnitude;
    float _Opacity;
    float2 fieldSize;        // field resolution in cells (= RenderTexture size)
    float displayWidth;      // arrows drawn along x at the current LOD (whole number; float for reliable binding)
    float2 arrowSpacing;     // cells between adjacent arrows (per axis); the grid spans edge-to-edge
    float2 detailFade;       // per-axis cross-fade weight for the odd-index arrows the finer level adds
    float colorMode;         // 0 = direction (hue), 1 = magnitude (low->high gradient), 2 = fixed colour, 3 = invert background
    float4 fixedColor;       // Fixed mode tint
    float4 lowColor;         // Magnitude mode colour at zero magnitude
    float4 highColor;        // Magnitude mode colour at (and above) maxMagnitude

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

        // Reconstruct this arrow's cell from the instance id and the current LOD. The grid is laid out edge-to-edge:
        // index 0 sits on cell 0 and the last index on the far-edge cell, spaced by arrowSpacing (a power-of-two
        // division of the field span). That keeps both edges covered with balanced margins, and each coarser level is
        // the exact even-index subset of the finer one, so shared arrows never move as you zoom.
        uint w = (uint)(displayWidth + 0.5);
        uint ix = v.instanceID % w;
        uint iy = v.instanceID / w;
        float2 cell = float2(ix, iy) * arrowSpacing;

        // Sample the field (bilinearly) at the arrow position, which need not sit exactly on a cell centre.
        float2 uv = (cell + 0.5) / fieldSize;
        float2 value = (tex2Dlod(_FieldTex, float4(uv, 0, 0)).rg - 0.5) * 2.0;

        float3 worldPoint = mul(gridToWorldMatrix, float4(cell, 0, 0)).xyz;

        float3 rotationAxis = float3(0,0,1);
        float4x4 rotationMatrix = mul(gridToWorldMatrix, RotateAroundAxis(rotationAxis, value));

        float4x4 transformation = mul(TranslationMatrix(worldPoint), rotationMatrix);
        transformation = mul(transformation, ScaleMatrix(scaleFactor));

        o.vertex = UnityObjectToClipPos(mul(transformation, v.vertex));
        // Where this vertex lands in cell space (= cell centre + the rotated/scaled quad offset). gridToWorldMatrix is
        // linear so applying it to (cell + offset) equals worldPoint + gridToWorld*offset; we keep the pre-world value
        // here so the fragment shader can clip anything that spills past the field's [-0.5, fieldSize-0.5] bounds.
        float4 cellOffset = mul(RotateAroundAxis(rotationAxis, value), mul(ScaleMatrix(scaleFactor), v.vertex));
        o.cellPos = cell + cellOffset.xy;
        o.uv = v.uv;
        o.value = value;
        // Even-index arrows are the ones the next-coarser level keeps; the odd-index ones are the extra detail that
        // fades out as we coarsen. frac(i/2) is 0 for even, 0.5 for odd. Fade per axis, and an arrow that's "extra" on
        // either axis follows the sooner of the two fades (min), so it's gone by the time that axis coarsens.
        float ax = (frac(ix * 0.5) > 0.25) ? detailFade.x : 1.0;
        float ay = (frac(iy * 0.5) > 0.25) ? detailFade.y : 1.0;
        o.alpha = min(ax, ay);
        return o;
    }

    half4 frag (v2f i) : SV_Target {
        // Clip anything outside the field rectangle so arrows near the edge don't overflow the bounds.
        if (any(i.cellPos < -0.5) || any(i.cellPos > fieldSize - 0.5)) discard;
        half4 shape = tex2D(_MainTex, i.uv); // arrow glyph (alpha mask + its own texture colour)

        if (colorMode > 2.5) {
            // Invert Background: ignore the colour fields and output premultiplied coverage. Combined with the
            // OneMinusDstColor/OneMinusSrcAlpha blend the renderer sets for this mode, the result is
            // lerp(dst, 1-dst, coverage) — each arrow inverts whatever it's drawn over, so it stands out against any
            // background (the one null is exact mid-grey, which inverts to itself). Coverage = glyph mask * opacity * LOD fade.
            float coverage = shape.a * _Opacity * i.alpha;
            return half4(coverage, coverage, coverage, coverage);
        }

        half4 color;
        if (colorMode < 0.5) {
            // Direction: hue from the vector's angle, opacity from its magnitude.
            color = DirectionToColor(i.value, maxMagnitude);
        } else if (colorMode < 1.5) {
            // Magnitude: low -> high colour gradient by |v| / maxMagnitude.
            float t = maxMagnitude > 1e-5 ? saturate(length(i.value) / maxMagnitude) : 0.0;
            color = lerp(lowColor, highColor, t);
            color.a *= _Opacity;
        } else {
            // Fixed: a single flat colour.
            color = fixedColor;
            color.a *= _Opacity;
        }
        color.a *= i.alpha; // LOD cross-fade weight
        return shape * color;
    }
    ENDCG

    // URP: a pass tagged SRPDefaultUnlit is what the Universal renderer draws for unlit materials.
    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Pass {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
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
            Blend [_SrcBlend] [_DstBlend] // straight alpha-over, or destination-invert in Invert Background mode
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
