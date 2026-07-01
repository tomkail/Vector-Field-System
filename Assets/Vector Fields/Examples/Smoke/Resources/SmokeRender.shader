Shader "VectorField/SmokeRender" {
    Properties {
        _MainTex ("Density", 2D) = "black" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Opacity ("Opacity", Float) = 1
    }

    // Shared program body, included by both the URP and Built-in SubShaders below. Draws the smoke density texture on
    // a plane: RGB is the smoke colour, A is opacity. Straight alpha blend over whatever's behind.
    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    sampler2D _MainTex;
    float4 _Tint;
    float _Opacity;

    v2f vert (appdata v) {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
    }

    fixed4 frag (v2f i) : SV_Target {
        float4 d = tex2D(_MainTex, i.uv);
        float a = saturate(d.a * _Opacity);
        return fixed4(d.rgb * _Tint.rgb, a);
    }
    ENDCG

    // URP: a pass tagged SRPDefaultUnlit is what the Universal renderer draws for unlit materials.
    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    // Built-in render pipeline.
    SubShader {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
