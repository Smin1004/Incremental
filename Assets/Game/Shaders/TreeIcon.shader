// Skill tree node icon (29 §6, Q10): a disc in the category color, a border, and one symbol drawn from signed
// distances (no textures). One uGUI quad per node (TreeIcon). The mesh carries the position in the quad (uv0.xy, -1..1),
// the symbol id (uv0.z, TreeIcon.Symbol) and flags (uv0.w: 1 = rule node, double border; 2 = preview of the next ring,
// outline and symbol only), the symbol and border color (uv1) and the disc color (vertex color). Output is premultiplied.
Shader "Incremental/TreeIcon"
{
    Properties
    {
        _Shape ("disc radius, border width, symbol radius, stroke width", Vector) = (0.84, 0.09, 0.5, 0.15)
        _Rule ("rule: disc radius, inner ring, outer ring, ring width", Vector) = (0.72, 0.8, 0.94, 0.07)
        _Shade ("bottom shade, rim light, preview symbol alpha, -", Vector) = (0.18, 0.25, 0.75, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "CanUseSpriteAtlas" = "False" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Shape;
                float4 _Rule;
                float4 _Shade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float4 p : TEXCOORD0;
                float4 ink : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.p = v.uv0;
                o.ink = v.uv1;
                return o;
            }

            // ---- signed distances (negative inside) ----

            float Seg(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            float Box(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Equilateral triangle pointing up, half width r: base at y = -r/√3, tip at y = 2r/√3.
            float Tri(float2 p, float r)
            {
                const float k = 1.7320508;
                p.x = abs(p.x) - r;
                p.y = p.y + r / k;
                if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
                p.x -= clamp(p.x, -2.0 * r, 0.0);
                return -length(p) * sign(p.y);
            }

            float Star5(float2 p, float r, float rf)
            {
                const float2 k1 = float2(0.809016994, -0.587785252);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0.0, 1.0);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }

            float Ring(float2 p, float r, float w) { return abs(length(p) - r) - w * 0.5; }
            float Disc(float2 p, float r) { return length(p) - r; }
            float Line(float2 p, float2 a, float2 b, float w) { return Seg(p, a, b) - w * 0.5; }
            float2 Rot(float2 p, float deg) { float a = radians(deg); float c = cos(a), s = sin(a); return float2(c * p.x + s * p.y, -s * p.x + c * p.y); }

            // An ellipse ring: the circle squashed in y (approximate distance, good enough at this size).
            float Orbit(float2 p, float rx, float ry, float w)
            {
                float2 rr = float2(rx, ry);
                float k0 = length(p / rr);
                float k1 = max(length(p / (rr * rr)), 1e-3);
                return abs(k0 * (k0 - 1.0) / k1) - w * 0.5;
            }

            // The symbol in glyph units (1 = symbol radius). w = stroke width. Ids match TreeIcon.Symbol.
            float Symbol(int s, float2 p, float w)
            {
                float d = 1e3;
                if (s == 1)        // spawn: three dots
                {
                    d = min(Disc(p - float2(-0.55, -0.35), 0.24), Disc(p - float2(0.55, -0.35), 0.24));
                    d = min(d, Disc(p - float2(0.0, 0.5), 0.24));
                }
                else if (s == 2)   // dust cap: rising bars
                {
                    d = Box(p - float2(-0.58, -0.45), float2(0.15, 0.35));
                    d = min(d, Box(p - float2(0.0, -0.25), float2(0.15, 0.55)));
                    d = min(d, Box(p - float2(0.58, -0.05), float2(0.15, 0.75)));
                }
                else if (s == 3)   // start bonus: play
                {
                    d = Tri(float2(p.y, p.x + 0.2), 0.8);
                }
                else if (s == 4)   // dust kind: gem outline
                {
                    float r = (abs(p.x) / 0.72 + abs(p.y) / 0.95 - 1.0) * 0.55;
                    d = min(abs(r) - w * 0.5, Line(p, float2(-0.4, 0.4), float2(0.4, 0.4), w));
                }
                else if (s == 5)   // dust mass: weight
                {
                    float2 q = p - float2(0.0, -0.3);
                    float body = Box(q, float2(0.62 - 0.18 * (q.y + 0.5), 0.5)) ;
                    d = min(body, Ring(p - float2(0.0, 0.45), 0.27, w));
                }
                else if (s == 6)   // pull: arrows inward
                {
                    float2 q = abs(p);
                    if (q.y > q.x) q = q.yx;
                    d = min(Line(q, float2(0.3, 0.0), float2(0.95, 0.0), w), Line(q, float2(0.3, 0.0), float2(0.62, 0.3), w));
                }
                else if (s == 7)   // radius: concentric rings
                {
                    d = min(Ring(p, 0.85, w), Ring(p, 0.45, w));
                    d = min(d, Disc(p, 0.13));
                }
                else if (s == 8)   // follow: double chevron
                {
                    float2 q = float2(p.x, abs(p.y));
                    d = min(Line(q, float2(-0.55, 0.6), float2(-0.05, 0.0), w), Line(q, float2(0.05, 0.6), float2(0.55, 0.0), w));
                }
                else if (s == 9)   // dust top speed: comet with speed lines
                {
                    d = Disc(p - float2(0.5, 0.0), 0.32);
                    d = min(d, Line(p, float2(-0.9, 0.0), float2(0.1, 0.0), w));
                    d = min(d, Line(p, float2(-0.6, 0.45), float2(0.2, 0.45), w));
                    d = min(d, Line(p, float2(-0.6, -0.45), float2(0.2, -0.45), w));
                }
                else if (s == 10)  // time: clock
                {
                    d = Ring(p, 0.85, w);
                    d = min(d, Line(p, float2(0.0, 0.0), float2(0.0, 0.55), w));
                    d = min(d, Line(p, float2(0.0, 0.0), float2(0.4, 0.0), w));
                }
                else if (s == 11)  // sale: coin
                {
                    d = Ring(p, 0.85, w);
                    d = min(d, Line(p, float2(0.0, -0.5), float2(0.0, 0.5), w * 1.2));
                    d = min(d, Line(p, float2(-0.28, 0.22), float2(0.28, 0.22), w));
                    d = min(d, Line(p, float2(-0.28, -0.22), float2(0.28, -0.22), w));
                }
                else if (s == 12)  // required mass down: arrow down
                {
                    float2 q = float2(abs(p.x), p.y);
                    d = min(Line(q, float2(0.0, 0.85), float2(0.0, -0.75), w), Line(q, float2(0.0, -0.8), float2(0.5, -0.3), w));
                }
                else if (s == 13)  // fragments: shards
                {
                    d = Tri(p - float2(-0.48, -0.42), 0.34);
                    d = min(d, Tri(Rot(p - float2(0.5, -0.3), 160.0), 0.28));
                    d = min(d, Tri(Rot(p - float2(0.0, 0.42), 40.0), 0.32));
                }
                else if (s == 14)  // gate: ringed planet
                {
                    float2 q = Rot(p, 20.0);
                    float ring = Orbit(q, 1.0, 0.32, w * 0.9);
                    // The ring passes behind the planet on its upper half.
                    float planet = Disc(p, 0.5);
                    if (q.y > 0.0) ring = max(ring, -(planet - w * 0.6));
                    d = min(planet, ring);
                }
                else if (s == 15)  // ability power: arrow up
                {
                    d = min(Tri(p - float2(0.0, 0.05), 0.6), Box(p - float2(0.0, -0.55), float2(0.17, 0.32)));
                }
                else if (s == 16)  // ability duration: hourglass
                {
                    d = min(Tri(float2(p.x, -(p.y - 0.62)), 0.48), Tri(float2(p.x, p.y + 0.62), 0.48));
                    d = min(d, Line(p, float2(-0.55, 0.92), float2(0.55, 0.92), w));
                    d = min(d, Line(p, float2(-0.55, -0.92), float2(0.55, -0.92), w));
                }
                else if (s == 17)  // ability count: plus
                {
                    d = min(Box(p, float2(0.75, 0.15)), Box(p, float2(0.15, 0.75)));
                }
                else if (s == 18)  // rule: star
                {
                    d = Star5(p - float2(0.0, -0.06), 0.95, 0.45);
                }
                else if (s == 19)  // evolve: double chevron up
                {
                    float2 q = float2(abs(p.x), p.y);
                    d = min(Line(q, float2(0.0, 0.75), float2(0.6, 0.2), w * 1.1), Line(q, float2(0.0, 0.15), float2(0.6, -0.4), w * 1.1));
                }
                else if (s == 20)  // 증폭 결정: faceted crystal
                {
                    float r = (abs(p.x) / 0.62 + abs(p.y - 0.0) / 0.98 - 1.0) * 0.5;
                    d = max(r, -Line(p, float2(-1.0, 0.28), float2(1.0, 0.28), w * 0.6));
                }
                else if (s == 21)  // 수축: ring closing in on a core
                {
                    d = min(Ring(p, 0.88, w), Disc(p, 0.2));
                    float2 q = abs(p);
                    d = min(d, Line(q, float2(0.55, 0.55), float2(0.3, 0.3), w));
                }
                else if (s == 22)  // 궤도석: body on an orbit
                {
                    d = min(Orbit(Rot(p, -15.0), 0.92, 0.5, w * 0.7), Disc(p, 0.28));
                    d = min(d, Disc(p - float2(0.75, 0.32), 0.18));
                }
                else if (s == 23)  // 유성: head and tail
                {
                    d = Disc(p - float2(-0.42, -0.42), 0.32);
                    d = min(d, Line(p, float2(-0.4, -0.4), float2(0.85, 0.85), w * 1.2));
                    d = min(d, Line(p, float2(-0.05, -0.5), float2(0.6, 0.15), w * 0.8));
                    d = min(d, Line(p, float2(-0.5, -0.05), float2(0.15, 0.6), w * 0.8));
                }
                else if (s == 24)  // 블랙홀: thick ring and its disc
                {
                    d = min(Ring(p, 0.5, w * 1.8), Orbit(Rot(p, 15.0), 1.0, 0.28, w * 0.8));
                    d = max(d, -Disc(p, 0.36));
                }
                return d;
            }

            float Cover(float d, float aa) { return saturate(0.5 - d / aa); }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p.xy;
                int symbol = (int)round(i.p.z);
                float flags = round(i.p.w);
                bool rule = fmod(flags, 2.0) > 0.5;
                bool preview = flags >= 1.5;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4) * 1.2;
                float3 fillRgb = i.color.rgb;
                float fillA = i.color.a;
                float3 ink = i.ink.rgb;
                float inkA = i.ink.a;

                float4 col = 0;
                float discR = rule ? _Rule.x : _Shape.x;
                if (!preview)
                {
                    float up = saturate(p.y * 0.5 + 0.5);
                    float3 rgb = fillRgb * (1.0 - _Shade.x * (1.0 - up));
                    // A little light along the rim, inside.
                    rgb += fillRgb * _Shade.y * smoothstep(discR - 0.25, discR, r);
                    float a = Cover(r - discR, aa) * fillA;
                    col = float4(rgb * a, a);
                }

                // Border: one ring, or two for a rule node (29 §6).
                float border;
                if (rule) border = min(Ring(p, _Rule.y, _Rule.w), Ring(p, _Rule.z, _Rule.w));
                else border = Ring(p, _Shape.x + _Shape.y * 0.5, _Shape.y * (preview ? 0.7 : 1.0));
                float ba = Cover(border, aa) * inkA * (preview ? 0.8 : 0.9);
                col = float4(ink * ba, ba) + col * (1.0 - ba);

                // Symbol.
                float scale = _Shape.z;
                float sd = Symbol(symbol, p / scale, _Shape.w) * scale;
                float sa = Cover(sd, aa) * inkA * (preview ? _Shade.z : 1.0);
                col = float4(ink * sa, sa) + col * (1.0 - sa);
                return col;
            }
            ENDHLSL
        }
    }
}
