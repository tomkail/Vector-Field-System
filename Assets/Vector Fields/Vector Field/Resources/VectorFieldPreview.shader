Shader "Hidden/VectorFieldPreview"
{
    // Editor-only: draws a vector field render texture in the inspector preview, applying the auto/manual contrast
    // scale on the GPU instead of round-tripping the field through a CPU Texture2D. The render texture stores the
    // field as rg = vector*0.5 + 0.5 (fixed scale, see VectorFieldComponent.WriteVectorFieldToRenderTexture), so this
    // re-encodes for display as (rg - 0.5)/scale + 0.5 — equivalent to the old CPU path's
    // VectorsToColors(values, 1/scale).
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Scale ("Scale", Float) = 1
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
            float _Scale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 encoded = tex2D(_MainTex, i.uv).rg;
                // Guard a zero scale (empty field / manual 0) so it shows as neutral gray rather than NaN.
                float inv = _Scale > 1e-6 ? 1.0 / _Scale : 1.0;
                float2 display = (encoded - 0.5) * inv + 0.5;
                return float4(display, 0, 1);
            }
            ENDCG
        }
    }
}
