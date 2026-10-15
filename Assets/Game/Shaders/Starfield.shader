// Procedural background (14 §6): a base color, a soft light bleed from one point (the central star's direction), an
// optional faint two-color nebula (the remnant palette, 15 §5), and two layers of stars from hashed grid cells with a slow
// twinkle. Drawn on one camera-sized quad behind everything. Stars stay small and dim so dust never reads as background.
// 26 §12.1: the layers move a little against the gravity source (parallax, B2), the nebula is wound round it while
// holding (B4), and one very dim far body sits in a corner (B3).
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
        _Parallax ("Parallax px: far layer xy, near layer zw", Vector) = (0, 0, 0, 0)
        _NebulaShift ("Nebula: parallax px xy, swirl radians z, swirl radius px w", Vector) = (0, 0, 0, 1)
        _SwirlAt ("Nebula swirl centre px xy", Vector) = (0, 0, 0, 0)
        _FarObj ("Far body: centre px xy, radius px z, alpha w", Vector) = (0, 0, 1, 0)
        _FarObj2 ("Far body: kind x, turn radians y", Vector) = (0, 0, 0, 0)
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
            #include "Lens.hlsl"

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
                float4 _Parallax;
                float4 _NebulaShift;
                float4 _SwirlAt;
                float4 _FarObj;
                float4 _FarObj2;
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

            float2 Turn(float2 v, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            // 26 §12.1 B3: the far body at offset d (px) from its centre: its light (0..1, x color by the caller) and how
            // much of the stars behind it it hides (0..1). 0 spiral galaxy, 1 the edge of a ringed giant planet, 2 a galaxy
            // seen edge-on. Soft everywhere, no texture.
            float FarBody(float2 d, out float hide)
            {
                hide = 0.0;
                float R = max(_FarObj.z, 1.0);
                int kind = (int)round(_FarObj2.x);
                float2 v = Turn(d, -_FarObj2.y) / R;
                if (kind == 0)
                {
                    float2 t = float2(v.x, v.y / 0.55);
                    float r = length(t);
                    if (r > 1.2) return 0.0;
                    float a = atan2(t.y, t.x);
                    float arms = 0.5 + 0.5 * cos(2.0 * a - 7.0 * log(r + 0.06));
                    float disc = exp(-r * 3.2) * (0.25 + 0.75 * arms * arms * arms) * (1.0 - smoothstep(0.8, 1.2, r));
                    float core = exp(-r * r / 0.006);
                    float grain = 0.75 + 0.25 * Fbm3(float3(t * 6.0, 3.1));
                    return saturate(disc * grain * 1.6 + core);
                }
                if (kind == 1)
                {
                    float r = length(v);
                    // The disc hides the stars and shows only a lit rim on the side toward the screen's middle.
                    float body = 1.0 - smoothstep(0.995, 1.0, r);
                    float lit = saturate(dot(v / max(r, 1e-4), float2(0.75, 0.66)));
                    float e = (1.0 - r) / 0.025;
                    float rim = exp(-e * e) * lit * body;
                    float bands = (0.5 + 0.5 * sin(v.y * 34.0 + Fbm3(float3(v * 4.0, 1.7)) * 3.0)) * 0.12 * body * lit;
                    // The ring: a flattened band round it, in front below its middle and behind above it.
                    float2 rv = float2(v.x, v.y / 0.2);
                    float rr = length(rv);
                    float ring = smoothstep(1.3, 1.36, rr) * (1.0 - smoothstep(1.9, 2.0, rr)) * (0.65 + 0.35 * sin(rr * 40.0));
                    ring *= (rv.y < 0.0 || r > 1.0) ? 1.0 : 0.0;
                    hide = saturate(body * 0.92 + ring * 0.4);
                    return saturate(rim + bands + ring * 0.55);
                }
                float ly = v.y / 0.022;
                float lane = 1.0 - 0.85 * exp(-ly * ly) * (1.0 - smoothstep(0.55, 0.9, abs(v.x)));
                float dy = v.y / 0.09;
                float disc2 = exp(-v.x * v.x * 3.0 - dy * dy);
                float bulge = exp(-dot(v, v) / 0.02);
                return saturate((disc2 + bulge * 0.9) * lane);
            }

            float4 frag(Varyings i) : SV_Target
            {
                // 24 §24.2-2, 26 §12.2: around each lens the stars and the clouds are drawn moved.
                float2 world = i.px / max(_PxPerUnit, 1e-3);
                float2 px = LensBackward(world) * _PxPerUnit;
                float3 col = _BgColor.rgb;
                float2 dg = (i.px - _Glow.xy) / max(_Glow.z, 1.0);
                col += _GlowColor.rgb * _Glow.w * exp(-dot(dg, dg));
                if (_Nebula.x > 0.0)
                {
                    // B2: the nebula moves least. B4: wound round the gravity source while holding, the vortex's way.
                    float2 np = px + _NebulaShift.xy;
                    if (_NebulaShift.z != 0.0)
                    {
                        float2 d = np - _SwirlAt.xy;
                        float u = length(d) / max(_NebulaShift.w, 1.0);
                        np = _SwirlAt.xy + Turn(d, -_NebulaShift.z * exp(-u * u));
                    }
                    // Two slowly drifting noise fields: one shapes the clouds, the other picks the color between A and B.
                    float2 q = np / max(_Nebula.y, 1.0);
                    float shape = Fbm4(float3(q, _Time.y * 0.006));
                    float hue = Fbm3(float3(q * 0.7 + 17.3, _Time.y * 0.004));
                    float cloud = smoothstep(0.38, 0.82, shape);
                    col += lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.3, 0.7, hue)) * cloud * _Nebula.x;
                }
                // B2: the far layer (A, many faint) moves less than the near one (B).
                float3 stars = StarLayer(px + _Parallax.xy, _LayerA, 0.0) + StarLayer(px + _Parallax.zw, _LayerB, 37.0);
                if (_FarObj.w > 0.0)
                {
                    float hide;
                    float light = FarBody(i.px - _FarObj.xy, hide);
                    float3 tint = lerp(float3(0.78, 0.82, 1.0), _GlowColor.rgb, 0.35);
                    float cover = hide * saturate(_FarObj.w * 12.0);
                    col = lerp(col, col * 0.6, cover);
                    stars *= 1.0 - cover;
                    col += tint * light * _FarObj.w;
                }
                col += stars;
                col += _StarTint.rgb * LensRing(world);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
