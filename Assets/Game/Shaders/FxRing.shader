// Light ring / soft flash (14 §6), additive. A quad whose UV maps to −1..1; draws a soft ring of radius _Ring.x and half
// width _Ring.y (both in quad half-sizes) plus a soft center disc of strength _Ring.z. Color already carries the fade
// and effectIntensity (IgnitionFx).
Shader "Incremental/FxRing"
{
    Properties
    {
        _Color ("Color (rgb × strength)", Color) = (1, 1, 1, 1)
        _Ring ("radius, half width, center glow", Vector) = (0.5, 0.05, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Ring;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = v.uv * 2.0 - 1.0;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float d = length(i.p);
                float w = max(_Ring.y, 1e-3);
                float ring = exp(-pow((d - _Ring.x) / w, 2.0));
                float center = _Ring.z * exp(-d * d * 6.0);
                float edge = saturate((1.0 - d) * 8.0);
                return float4(_Color.rgb * _Color.a * (ring + center) * edge, 0);
            }
            ENDHLSL
        }
    }
}
