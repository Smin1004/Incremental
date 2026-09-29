// Dust trail (22 §5): the ribbon a medium-or-heavier dust leaves behind as it spirals into the gravity source.
// One additive quad strip per trail in a single dynamic mesh (DustTrailRenderer). UV.x runs −1..1 across the ribbon,
// so the edges fade out and the middle is the bright line; the taper along the ribbon is baked into the vertex color
// (rgb = the dust's color × brightness × taper / 2, the same halved encoding the dust mesh uses).
Shader "Incremental/DustTrail"
{
    Properties
    {
        _EdgeSoftness ("Edge softness", Range(0.5, 4)) = 1.5
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
                float _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = saturate(1.0 - abs(i.uv.x));
                float soft = pow(across, _EdgeSoftness);
                return half4(i.color.rgb * 2.0 * soft, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
