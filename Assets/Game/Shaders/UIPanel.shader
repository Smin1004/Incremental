// Every UI shape (26 §4): panel, button, key chip, bar, badge. One uGUI quad per shape, no textures. The mesh
// (UIShape) carries the position in reference px from the rect center and the room around the rect (uv0), the half
// size, the fill share of a bar and a 0..1 state for hover and flashes (uv1). The material carries the look of one role in one visual direction
// (UIStyle fills it from UITheme): fill, border, round or square corners, corner brackets, scale marks, light from
// the edge, glass highlight, outer shadow. Output is premultiplied alpha. Light follows the effect intensity (_UIEffect).
Shader "Incremental/UIPanel"
{
    Properties
    {
        _Fill ("Fill", Color) = (0, 0, 0, 0.7)
        _Border ("Border", Color) = (1, 1, 1, 0.3)
        _Bracket ("Corner brackets", Color) = (1, 1, 1, 0)
        _Shape ("corner px, border px, bracket length px, bracket width px", Vector) = (0, 1, 0, 2)
        _Marks ("tick spacing px, tick length px, tick alpha, bar (0/1)", Vector) = (0, 5, 0.4, 0)
        _Glow ("edge light px, edge light strength, sheen, fill shade", Vector) = (16, 0, 0, 0)
        _Outer ("shadow px, shadow alpha, state light, -", Vector) = (24, 0, 0.5, 0)
        _Segment ("bar cell px, gap px, fill fade toward the bottom", Vector) = (0, 2, 0, 0)
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
                float4 _Fill;
                float4 _Border;
                float4 _Bracket;
                float4 _Shape;
                float4 _Marks;
                float4 _Glow;
                float4 _Outer;
                float4 _Segment;
            CBUFFER_END

            // 0..1, GameRoot.EffectIntensity (UIStyle.SetEffect).
            float _UIEffect;

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
                float3 p : TEXCOORD0;       // px from the rect center; z = room around the rect, px
                float4 box : TEXCOORD1;     // half size px, fill share, state
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.p = v.uv0.xyz;
                o.box = v.uv1;
                return o;
            }

            float4 Over(float4 top, float4 bottom) { return top + bottom * (1.0 - top.a); }

            // 1 inside the band of this width that runs inward from the outline.
            float Band(float d, float width, float aa)
            {
                return saturate(0.5 - (abs(d + width * 0.5) - width * 0.5) / aa);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p.xy;
                float2 hs = max(i.box.xy, 1e-3);
                float share = i.box.z;
                float state = i.box.w;
                bool isBar = _Marks.w > 0.5;

                // Signed distance to the outline (< 0 inside), round or square corners.
                float r = min(_Shape.x, min(hs.x, hs.y));
                float2 q = abs(p) - hs + r;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                float aa = max(fwidth(d), 1e-4);
                float inside = saturate(0.5 - d / aa);
                float up = saturate((p.y + hs.y) / (2.0 * hs.y));     // 0 bottom, 1 top
                float2 e = hs - abs(p);                                 // distance to the nearest edges
                float3 tint = isBar ? float3(1, 1, 1) : i.color.rgb;

                // Fill, darker toward the bottom.
                float3 fillRgb = _Fill.rgb * tint * (1.0 - _Glow.w * (1.0 - up));
                float fillA = _Fill.a * lerp(1.0, up, _Segment.z);
                float4 col = float4(fillRgb * fillA, fillA) * inside;

                // Bar: the filled part takes the vertex color, optionally cut into cells.
                if (isBar)
                {
                    float x = p.x + hs.x;
                    float cut = saturate((share * 2.0 * hs.x - x) / aa + 0.5);
                    if (_Segment.x > 0.0)
                    {
                        float cell = frac(x / _Segment.x) * _Segment.x;
                        cut *= saturate((cell - _Segment.y) / aa + 0.5);
                    }
                    // The fill keeps a small gap to the frame, less of one on a bar that is only a few px tall.
                    float gapPx = min(_Shape.y + 1.5, hs.y * 0.34);
                    float inset = saturate((e.y - gapPx) / aa) * saturate((e.x - gapPx) / aa);
                    float a = cut * inside * inset;
                    float3 lit = i.color.rgb * (1.0 + _Glow.z * smoothstep(0.35, 1.0, up));
                    col = Over(float4(lit * a, a), col);
                    // The fill end glows a little past itself.
                    col.rgb += i.color.rgb * _Glow.y * _UIEffect * inside * exp(-abs(x - share * 2.0 * hs.x) / max(_Glow.x, 1e-3)) * step(0.001, share);
                }
                else
                {
                    // Glass highlight on the upper part.
                    col.rgb += _Glow.z * smoothstep(0.45, 1.0, up) * inside * 0.5;
                    // Light from the edge inward.
                    col.rgb += _Border.rgb * _Glow.y * _UIEffect * exp(d / max(_Glow.x, 1e-3)) * inside;
                }

                // Hover / flash.
                float lightUp = state * _Outer.z * _UIEffect;
                col.rgb += _Border.rgb * tint * lightUp * inside * (0.3 + 0.7 * exp(d / 14.0));

                // Scale marks along the bottom edge, every fifth one longer.
                if (_Marks.x > 0.0)
                {
                    float idx = floor(p.x / _Marks.x + 0.5);
                    float fx = abs(p.x - idx * _Marks.x);
                    float major = step(abs(frac(idx / 5.0 + 0.5) - 0.5), 0.05);
                    float len = _Marks.y * (1.0 + 0.9 * major);
                    float from = _Shape.y + 2.0;
                    float m = saturate(0.5 - (fx - 0.5) / aa) * step(from, e.y) * step(e.y, from + len) * step(p.y, 0.0)
                              * step(_Shape.z + 4.0, e.x) * inside;
                    float ta = m * _Marks.z;
                    col = Over(float4(_Border.rgb * tint * ta, ta), col);
                }

                // Border.
                if (_Shape.y > 0.0)
                {
                    float ba = Band(d, _Shape.y, aa) * _Border.a * saturate(1.0 + lightUp);
                    col = Over(float4(_Border.rgb * tint * ba, ba), col);
                }

                // Corner brackets.
                if (_Shape.z > 0.0)
                {
                    float ka = Band(d, _Shape.w, aa) * step(e.x, _Shape.z) * step(e.y, _Shape.z) * _Bracket.a;
                    col = Over(float4(_Bracket.rgb * tint * ka, ka), col);
                }

                // Outside: soft shadow under the shape, and the hover light spilling out. Both end exactly where the
                // quad does: in linear color even a trace of light left at the quad's edge shows as a box.
                float outside = 1.0 - inside;
                float room = saturate(1.0 - max(d, 0.0) / max(i.p.z, 1e-3));
                float sh = _Outer.y * exp(-max(d, 0.0) / max(_Outer.x, 1e-3)) * smoothstep(0.0, 0.5, room) * outside;
                col = Over(col, float4(0, 0, 0, sh));
                col.rgb += _Border.rgb * tint * lightUp * 0.5 * room * room * room * outside;

                return col * i.color.a;
            }
            ENDHLSL
        }
    }
}
