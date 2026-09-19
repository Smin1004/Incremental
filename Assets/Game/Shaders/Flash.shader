// Radial flash (15 §3, rule of 14 §6 revised): a camera-sized quad, additive. Brightness falls off from a center point as
// a Gaussian that reaches _Flash.w (the edge share, 0.3) at radius _Flash.z, so a flash lights the area around its source
// instead of washing the whole screen grey. Color already carries the envelope, the peak alpha and effectIntensity
// (ScreenFlash).
Shader "Incremental/Flash"
{
    Properties
    {
        _Color ("Color (rgb × alpha)", Color) = (1, 1, 1, 0)
        _Flash ("center xy (reference px), radius px, brightness at the radius", Vector) = (0, 0, 1100, 0.3)
        _PxPerUnit ("Reference px per world unit", Float) = 100
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
                float4 _Flash;
                float _PxPerUnit;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 px : TEXCOORD0;   // world position in reference px
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.px = TransformObjectToWorld(v.positionOS.xyz).xy * _PxPerUnit;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float x = length(i.px - _Flash.xy) / max(_Flash.z, 1.0);
                // edge^(x²): 1 at the center, the edge share at the radius, then keeps falling.
                float k = exp(log(clamp(_Flash.w, 1e-3, 1.0)) * x * x);
                return float4(_Color.rgb * _Color.a * k, 0);
            }
            ENDHLSL
        }
    }
}
