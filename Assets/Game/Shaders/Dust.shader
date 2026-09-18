// Dust (14 §5): soft glowing points, additive. One quad per dust in a dynamic mesh (DustMeshRenderer).
// UV = (along, across, stretch): along runs −stretch..stretch, across −1..1, so a stretched quad draws a capsule
// (a streak along the velocity) with round ends. Vertex color = tint × brightness / 2.
Shader "Incremental/Dust"
{
    Properties
    {
        _Core ("Core radius (share of the halo radius)", Float) = 0.3
        _HaloAlpha ("Halo brightness", Float) = 0.35
        _StreakDim ("Brightness at full stretch", Float) = 0.7
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
                float _Core;
                float _HaloAlpha;
                float _StreakDim;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float s = max(i.uv.z, 1.0);
                float d = length(float2(max(abs(i.uv.x) - (s - 1.0), 0.0), i.uv.y));
                float aa = max(fwidth(d), 1e-3);
                float core = smoothstep(_Core + aa, _Core - aa, d);
                float halo = exp(-d * d * 5.0) * saturate(1.0 - d);
                float k = (core + halo * _HaloAlpha) * lerp(1.0, _StreakDim, saturate((s - 1.0) / 3.0));
                // A streak fades toward its tail (the head is at +along, the velocity direction) so it reads as motion.
                float head = saturate((i.uv.x + s) / (2.0 * s));
                k *= lerp(1.0, lerp(0.1, 1.0, head * head), saturate(s - 1.0));
                return float4(i.color.rgb * 2.0 * k, 0.0);   // vertex color holds brightness / 2
            }
            ENDHLSL
        }
    }
}
