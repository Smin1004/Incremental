// Procedural background (14 §6): a base color, a soft light bleed from one point (the central star's direction), an
// optional faint two-color nebula (the remnant palette, 15 §5), and two layers of stars from hashed grid cells with a slow
// twinkle. Drawn on one camera-sized quad behind everything; the
// camera is fixed, so there is no parallax. Stars stay small and dim so dust never reads as background.
Shader "Incremental/Starfield"
{
    Properties
    {
        _BgColor ("Background", Color) = (0.02, 0.02, 0.05, 1)
        _GlowColor ("Light bleed color", Color) = (1, 0.8, 0.5, 1)
        _Glow ("Light bleed: center xy (reference px), radius px, strength", Vector) = (0, 0, 800, 0)
        _StarTint ("Star tint", Color) = (0.85, 0.9, 1, 1)
        _LayerA ("Layer A: cell px, density, size px, brightness", Vector) = (26, 0.35, 1, 0.35)
        _LayerB ("Layer B: cell px, density, size px, brightness", Vector) = (90, 0.5, 1.6, 0.7)
        _Twinkle ("Twinkle: speed, depth", Vector) = (0.6, 0.5, 0, 0)
        _PxPerUnit ("Reference px per world unit", Float) = 100
        _NebulaA ("Nebula color A", Color) = (0, 0, 0, 1)
        _NebulaB ("Nebula color B", Color) = (0, 0, 0, 1)
        _Nebula ("Nebula: strength, scale px", Vector) = (0, 650, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One Zero
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BgColor;
                float4 _GlowColor;
                float4 _Glow;
                float4 _StarTint;
                float4 _LayerA;
                float4 _LayerB;
                float4 _Twinkle;
                float _PxPerUnit;
                float4 _NebulaA;
                float4 _NebulaB;
                float4 _Nebula;
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

            // One star per occupied cell, kept away from the cell border so it is never cut.
            float3 StarLayer(float2 px, float4 layer, float salt)
            {
                float cellPx = max(layer.x, 4.0);
                float2 cell = floor(px / cellPx);
                float h = Hash21(cell + salt);
                if (h > layer.y) return 0;
                float2 center = (cell + 0.2 + 0.6 * Hash22(cell + salt * 1.7)) * cellPx;
                float r = layer.z * (0.6 + 0.8 * Hash21(cell + salt + 3.1));
                float d = length(px - center);
                float aa = max(fwidth(d), 0.5);
                float core = smoothstep(r + aa, r - aa, d);
                float halo = exp(-(d * d) / (r * r * 9.0)) * 0.35;
                float tw = 1.0 - _Twinkle.y * (0.5 + 0.5 * sin(_Time.y * _Twinkle.x * (0.6 + Hash21(cell + 9.2)) + h * 40.0));
                float b = layer.w * (0.45 + 0.55 * Hash21(cell + salt + 5.3)) * tw;
                float3 tint = lerp(float3(1, 1, 1), _StarTint.rgb, Hash21(cell + 7.7));
                return tint * (core + halo) * b;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 col = _BgColor.rgb;
                float2 dg = (i.px - _Glow.xy) / max(_Glow.z, 1.0);
                col += _GlowColor.rgb * _Glow.w * exp(-dot(dg, dg));
                if (_Nebula.x > 0.0)
                {
                    // Two slowly drifting noise fields: one shapes the clouds, the other picks the color between A and B.
                    float2 q = i.px / max(_Nebula.y, 1.0);
                    float shape = Fbm4(float3(q, _Time.y * 0.006));
                    float hue = Fbm3(float3(q * 0.7 + 17.3, _Time.y * 0.004));
                    float cloud = smoothstep(0.38, 0.82, shape);
                    col += lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.3, 0.7, hue)) * cloud * _Nebula.x;
                }
                col += StarLayer(i.px, _LayerA, 0.0);
                col += StarLayer(i.px, _LayerB, 37.0);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
