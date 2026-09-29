// Dust (14 §5): soft glowing points, additive. One quad per dust in a dynamic mesh (DustMeshRenderer).
// UV = (along, across, stretch): along runs −stretch..stretch, across −1..1, so a stretched quad draws a streak along the
// velocity with a round head and a tail that narrows to a point. Vertex color rgb = tint × brightness / 2, alpha = the
// dust kind's shape id / 255 (20 §4): the silhouette branch, while the hue stays the palette's.
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

            // Silhouette of a dust kind (20 §4), as "how far out the shape reaches" in the direction of (x, y):
            // 1 = all the way to the quad edge, smaller = pulled in. Shapes stay inside the quad, so nothing is clipped.
            // 0 dot, 1 shard (a slanted chip), 2 clump (lumpy), 3 crystal (six points), 4 interstellar (a haze with four spikes).
            float ShapeReach(int shape, float x, float y)
            {
                if (shape == 0) return 1.0;
                float a = atan2(y, x);
                if (shape == 1) return lerp(0.40, 1.0, pow(abs(cos(a - 0.52)), 2.0));
                if (shape == 2) return lerp(0.72, 1.0, 0.5 + 0.5 * cos(3.0 * a + 0.7));
                if (shape == 3) return lerp(0.30, 1.0, pow(abs(cos(3.0 * a)), 6.0));
                return lerp(0.45, 1.0, max(pow(abs(cos(2.0 * a)), 8.0), 0.30));
            }

            float4 frag(Varyings i) : SV_Target
            {
                float s = max(i.uv.z, 1.0);
                // Round head at +along; toward the tail a stretched streak narrows to a point (15 §3). A still dot is round.
                float x = i.uv.x;
                float taper = saturate(s - 1.0);
                float w = lerp(1.0, max(0.08, (x + s) / max(2.0 * s - 1.0, 1e-3)), taper);
                float d = x > s - 1.0
                    ? length(float2(x - (s - 1.0), i.uv.y))
                    : length(float2(max(-x - (s - 1.0), 0.0), i.uv.y / w));
                // The kind's shape rides on the still dot; a fast streak is already a shape of its own, so it fades out.
                int shape = (int)round(i.color.a * 255.0);
                float reach = lerp(ShapeReach(shape, x, i.uv.y), 1.0, taper);
                d /= max(reach, 1e-3);
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
