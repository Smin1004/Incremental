// Procedural planet (14 §3). One quad per body, no textures: the sphere normal is rebuilt from the quad position,
// the surface is 3D noise sampled on the rotated normal (spin), and the look comes from per-renderer properties
// (PlanetVisual fills them from PlanetLook). Five surface types: 0 Rock, 1 Terran, 2 Gas, 3 Star, 4 BlackHole (15 §5).
// Output is premultiplied alpha: the disc and the ring are alpha blended, glows and the comet tail are additive (alpha 0).
Shader "Incremental/Planet"
{
    Properties
    {
        _Surface ("Surface (0 Rock, 1 Terran, 2 Gas, 3 Star, 4 BlackHole)", Float) = 0
        _Extent ("Quad half size in radii", Float) = 1.1
        _BaseColor ("Base", Color) = (0.6, 0.6, 0.6, 1)
        _DarkColor ("Dark", Color) = (0.3, 0.3, 0.3, 1)
        _AccentColor ("Accent", Color) = (1, 1, 1, 1)
        _GlowColor ("Glow", Color) = (1, 1, 1, 1)
        _RingColor ("Ring", Color) = (1, 1, 1, 0)
        _Surf1 ("noiseScale, patchLevel, patchSoftness, seed", Vector) = (2, 0.5, 0.2, 0)
        _Surf2 ("craterDensity, craterSize, craterScale, polarCap", Vector) = (0, 0.35, 3, 0)
        _Surf3 ("irregularity, cloudCover, cloudSpinMult, emission", Vector) = (0, 0, 1.5, 0)
        _Surf4 ("bandCount, turbulence, glowSize, glowStrength", Vector) = (0, 0, 0, 0)
        _Surf5 ("spotSize, spotLatitude", Vector) = (0, 0, 0, 0)
        _Motion ("spin rad/s, axis roll rad, axis pitch rad, spin phase rad", Vector) = (0, 0, 0.35, 0)
        _Extra ("tailLength, ringInner, ringOuter, fade", Vector) = (0, 0, 0, 1)
        _Tail ("tailWidth, tailSpread, tailFadePower", Vector) = (0.5, 0.3, 1.3, 0)
        _Light ("light: xy world point (w 0) or direction (w 1)", Vector) = (1, 1, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Surface;
                float _Extent;
                float4 _BaseColor;
                float4 _DarkColor;
                float4 _AccentColor;
                float4 _GlowColor;
                float4 _RingColor;
                float4 _Surf1;
                float4 _Surf2;
                float4 _Surf3;
                float4 _Surf4;
                float4 _Surf5;
                float4 _Motion;
                float4 _Extra;
                float4 _Tail;
                float4 _Light;
            CBUFFER_END

            // Globals from GameParams (PlanetVisual.ApplyGlobals).
            float _PlanetAmbient;
            float _PlanetLightZ;
            float4 _PlanetRim;      // x strength, y power
            float4 _PlanetDetail;   // x disc radius (px) where fine detail is fully shown, y crater relief

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 p : TEXCOORD0;       // quad position in planet radii
                float3 bx : TEXCOORD1;      // body frame in view space (y = spin axis)
                float3 by : TEXCOORD2;
                float3 bz : TEXCOORD3;
                float3 L : TEXCOORD4;       // light direction, view space (z toward the viewer)
                float4 spin : TEXCOORD5;    // sin, cos of the surface spin; sin, cos of the cloud spin
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = (v.uv * 2.0 - 1.0) * _Extent;

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float2 l2 = _Light.w > 0.5 ? _Light.xy : _Light.xy - centerWS.xy;
                float len = length(l2);
                l2 = len > 1e-5 ? l2 / len : float2(0.7071, 0.7071);
                o.L = normalize(float3(l2, _PlanetLightZ));

                float roll = _Motion.y, pitch = _Motion.z;
                float3 A = float3(-sin(roll) * cos(pitch), cos(roll) * cos(pitch), sin(pitch));
                float3 bx = float3(A.y, -A.x, 0);
                bx = dot(bx, bx) > 1e-6 ? normalize(bx) : float3(1, 0, 0);
                o.bx = bx;
                o.by = A;
                o.bz = cross(bx, A);

                float s = _Motion.x * _Time.y + _Motion.w;
                float c = s * _Surf3.z;
                o.spin = float4(sin(s), cos(s), sin(c), cos(c));
                return o;
            }

            // View-space normal -> body coordinates (y = spin axis), spun by angle (sin, cos).
            float3 ToBody(float3 n, Varyings i, float2 sc)
            {
                float3 q0 = float3(dot(n, i.bx), dot(n, i.by), dot(n, i.bz));
                return float3(q0.x * sc.y - q0.z * sc.x, q0.y, q0.x * sc.x + q0.z * sc.y);
            }

            // Body-space vector -> view space (inverse of ToBody).
            float3 ToView(float3 g, Varyings i, float2 sc)
            {
                float3 g0 = float3(g.x * sc.y + g.z * sc.x, g.y, -g.x * sc.x + g.z * sc.y);
                return g0.x * i.bx + g0.y * i.by + g0.z * i.bz;
            }

            // Craters: 3D cells, some holding a ball; its cut with the sphere is the crater. Returns the distance to the
            // nearest crater center relative to its radius (0 center, 1 rim, big = none) and the body-space direction
            // from that center to the point.
            float CraterField(float3 q, float density, float size, float scale, float seed, out float3 fromCenter)
            {
                float3 x = q * scale + seed * float3(3.1, 5.7, 1.3);
                float3 ip = floor(x);
                float3 fp = x - ip;
                float best = 10.0;
                fromCenter = float3(0, 1, 0);
                for (int k = -1; k <= 1; k++)
                for (int j = -1; j <= 1; j++)
                for (int m = -1; m <= 1; m++)
                {
                    float3 cell = ip + float3(k, j, m);
                    if (Hash31(cell + 17.0) > density) continue;
                    float3 d = float3(k, j, m) + Hash33(cell) - fp;       // point -> center, cell units
                    float rad = size * (0.45 + 0.55 * Hash31(cell + 41.0));
                    float dist = length(d) / rad;
                    if (dist < best)
                    {
                        best = dist;
                        fromCenter = -d;
                    }
                }
                return best;
            }

            float4 Over(float4 top, float4 bottom) { return top + bottom * (1.0 - top.a); }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = i.p;
                float r = length(p);
                float px = max(fwidth(r), 1e-4);           // one screen pixel in radii
                float seed = _Surf1.w;
                float t = _Time.y;
                bool isRock = _Surface < 0.5;
                bool isTerran = _Surface >= 0.5 && _Surface < 1.5;
                bool isGas = _Surface >= 1.5 && _Surface < 2.5;
                bool isStar = _Surface >= 2.5 && _Surface < 3.5;
                bool isHole = _Surface >= 3.5;
                // Fine detail fades out on tiny discs so it does not shimmer.
                float lod = saturate((1.0 / px - 3.0) / max(_PlanetDetail.x, 1.0));

                // Silhouette: round, or dented by noise around the rim (asteroid). The dents drift slowly with the spin.
                float edge = 1.0;
                if (_Surf3.x > 0.0)
                {
                    float2 dir = r > 1e-5 ? p / r : float2(1, 0);
                    float nEdge = Fbm3(float3(dir * 1.7, seed * 3.7 + t * _Motion.x * 0.08));
                    edge = 1.0 - _Surf3.x * saturate(nEdge * 1.4 - 0.2);
                }
                float rr = r / edge;
                float discA = saturate((1.0 - rr) * edge / px + 0.5);
                float3 n = float3(p / edge, sqrt(saturate(1.0 - rr * rr)));
                float3 L = i.L;
                float2 litDir2 = normalize(L.xy);
                float litSide = saturate(dot(r > 1e-5 ? p / r : float2(0, 0), litDir2) * 0.6 + 0.4);

                float3 q = ToBody(n, i, i.spin.xy);
                float3 P = q * _Surf1.x + seed * float3(13.1, 7.7, 3.3);
                float3 shadeN = n;
                float3 albedo;
                float3 col;
                float lvl = _Surf1.y, soft = max(_Surf1.z, 1e-3);

                if (isHole)
                {
                    // Black hole: the disc is simply black; the light is in the accretion disc and the edge glow below.
                    col = float3(0, 0, 0);
                }
                else if (isStar)
                {
                    // Large slow convection patches + fine granules (bright cell centers, dark lanes), low contrast.
                    float big = Fbm4(P * 0.45 + float3(0, 0, t * 0.03));
                    float3 dummy;
                    float cells = saturate(CraterField(q, 1.0, 1.0, _Surf1.x * 2.2, seed + t * 0.05, dummy));
                    float g = (big - 0.5) * 0.9 + (0.5 - cells) * 0.35 * lod;
                    // Limb darkening toward the dark color; the center runs hot toward white.
                    float mu = n.z;
                    float limb = pow(mu, 0.4);
                    col = lerp(_DarkColor.rgb, _BaseColor.rgb, saturate(0.25 + 0.75 * limb + g * 0.35));
                    col *= 0.78 + 0.22 * limb + g * 0.25;
                    col = lerp(col, float3(1, 1, 1), pow(mu, 3.0) * 0.12);
                }
                else
                {
                    if (isGas)
                    {
                        float turb = Fbm4(P * float3(1.4, 2.6, 1.4));
                        float bands = _Surf4.x;
                        float b1 = sin((q.y * bands + (turb - 0.5) * _Surf4.y * 2.2) * 3.14159);
                        float b2 = sin((q.y * bands * 3.1 + (turb - 0.5) * _Surf4.y * 3.5 + seed) * 3.14159);
                        float k = saturate(0.5 + 0.5 * b1) * 0.78 + (0.5 + 0.5 * b2) * 0.22 * lod + 0.11 * (1.0 - lod);
                        albedo = lerp(_DarkColor.rgb, _BaseColor.rgb, k);

                        // Storm spot: an oval at a fixed latitude and a seed longitude, so the spin has a landmark.
                        if (_Surf5.x > 0.0)
                        {
                            float lon = atan2(q.z, q.x);
                            float dl = lon - frac(seed * 0.37) * 6.28318;
                            dl = dl - 6.28318 * floor((dl + 3.14159) / 6.28318);
                            float e = pow(dl / (_Surf5.x * 2.2), 2.0) + pow((q.y - _Surf5.y) / _Surf5.x, 2.0);
                            float spot = smoothstep(1.0, 0.55, e + (turb - 0.5) * 0.4);
                            albedo = lerp(albedo, _AccentColor.rgb * (0.85 + 0.3 * (1.0 - e)), spot);
                        }
                    }
                    else
                    {
                        float h = Fbm5(P);
                        float land = smoothstep(lvl - soft, lvl + soft, h);
                        if (isTerran)
                        {
                            float3 ocean = lerp(_DarkColor.rgb * 0.55, _DarkColor.rgb, saturate((h - (lvl - 0.18)) / 0.18));
                            float high = saturate((h - lvl) / 0.16);
                            float3 ground = lerp(_BaseColor.rgb, _BaseColor.rgb * float3(1.55, 1.2, 0.7), high);
                            ground *= lerp(1.0, 0.8 + 0.4 * VNoise(P * 4.3), lod);
                            albedo = lerp(ocean, ground, land);
                        }
                        else
                        {
                            albedo = lerp(_DarkColor.rgb, _BaseColor.rgb, land);
                            albedo *= lerp(1.0, 0.82 + 0.36 * VNoise(P * 3.7), lod);
                        }

                        // Craters (Rock): darker bowl, bright rim, walls tilted so they catch the light on one side.
                        if (_Surf2.x > 0.0)
                        {
                            float3 fromC;
                            float x = CraterField(q, _Surf2.x, _Surf2.y, _Surf2.z, seed, fromC);
                            float slope = 0.0;
                            if (x < 1.0)
                            {
                                albedo *= lerp(0.72, 0.95, x * x);
                                slope = x;
                            }
                            else if (x < 1.35)
                            {
                                float rim = 1.0 - (x - 1.0) / 0.35;
                                albedo *= 1.0 + 0.12 * rim;
                                slope = -rim * 0.6;
                            }
                            float3 g = fromC - q * dot(fromC, q);
                            float gl = length(g);
                            if (gl > 1e-4 && slope != 0.0)
                            {
                                float3 gv = ToView(g / gl, i, i.spin.xy);
                                shadeN = normalize(n - gv * slope * _PlanetDetail.y * lod);
                            }
                        }

                        // Polar caps.
                        if (_Surf2.w > 0.0)
                        {
                            float lat = abs(q.y) + (VNoise(P * 2.3) - 0.5) * 0.12;
                            float cap = smoothstep(_Surf2.w - 0.015, _Surf2.w + 0.03, lat);
                            albedo = lerp(albedo, _AccentColor.rgb, cap);
                        }

                        // Terran: clouds on their own spin; ocean glint.
                        if (isTerran)
                        {
                            float3 qc = ToBody(n, i, i.spin.zw);
                            float3 Pc = qc * _Surf1.x * 1.25 + seed * float3(2.1, 9.4, 4.4);
                            float cl = Fbm4(Pc * float3(1.0, 2.0, 1.0));
                            float cover = _Surf3.y;
                            float cloud = smoothstep(1.0 - cover - 0.08, 1.0 - cover + 0.14, cl) * step(0.001, cover);
                            float glint = pow(saturate(dot(reflect(-L, n), float3(0, 0, 1))), 28.0) * (1.0 - land) * (1.0 - cloud);
                            albedo = lerp(albedo, _AccentColor.rgb, cloud * 0.95);
                            albedo += glint * 0.45;
                        }
                    }

                    float lam = saturate(dot(shadeN, L));
                    float amb = _PlanetAmbient;
                    col = albedo * (amb + (1.0 - amb) * lam);

                    // Rim light: brighter on the lit side, a trace on the dark side so the silhouette reads on black.
                    float rimF = pow(1.0 - saturate(n.z), max(_PlanetRim.y, 0.5));
                    float3 rimCol = _Surf4.w > 0.0 ? _GlowColor.rgb : albedo;
                    col += rimCol * rimF * _PlanetRim.x * (0.15 + 0.85 * saturate(dot(n.xy, litDir2) + 0.2));

                    // Self-lit (brown dwarf): a dim glow over the whole disc, stronger toward the limb.
                    float e = _Surf3.w;
                    col += (_GlowColor.rgb * (0.25 + 0.75 * rimF) + albedo * 0.5) * e;
                }

                float4 surf = float4(col * discA, discA);
                float4 outc = surf;

                // Ring in the equatorial plane: in front of the disc where it is nearer to the viewer, behind it elsewhere.
                if (_Extra.z > 0.0 && abs(i.by.z) > 0.02)
                {
                    float3 A = i.by;
                    float zr = -(p.x * A.x + p.y * A.y) / A.z;
                    float3 Pr = float3(p, zr);
                    float d = length(Pr);
                    float inner = _Extra.y, outer = _Extra.z;
                    float u = (d - inner) / max(outer - inner, 1e-3);
                    if (u > 0.0 && u < 1.0 && isHole)
                    {
                        // Accretion disc: self-lit, hot (base color) at the inner edge to the dark color outside, with
                        // streaks that turn around the axis at the spin speed. Angle from the body frame, sampled on a circle
                        // so there is no seam.
                        float ang = atan2(dot(Pr, i.bz), dot(Pr, i.bx)) + _Motion.x * t + _Motion.w;
                        float2 cs = float2(cos(ang), sin(ang));
                        float streak = VNoise(float3(cs * 1.6, u * 9.0 + seed));
                        float fine = VNoise(float3(cs * 5.0, u * 26.0 + seed * 1.7));
                        float heat = 1.0 - u;
                        float3 dc = lerp(_DarkColor.rgb, _BaseColor.rgb, heat * heat) * (0.5 + 0.7 * streak + 0.3 * fine * lod);
                        float ringA = saturate(_RingColor.a * smoothstep(0.0, 0.08, u) * pow(1.0 - u, 1.3));
                        float4 ring = float4(dc * ringA * (1.0 + 1.2 * heat), ringA * 0.85);
                        bool front = zr > n.z || discA <= 0.0;
                        outc = front ? Over(ring, surf) : Over(surf, ring);
                    }
                    else if (u > 0.0 && u < 1.0)
                    {
                        float bands = 0.62 + 0.38 * VNoise(float3(d * 16.0, seed, 0.5));
                        bands *= smoothstep(0.0, 0.06, u) * smoothstep(1.0, 0.9, u);
                        bands *= 1.0 - 0.85 * smoothstep(0.5, 0.53, u) * smoothstep(0.62, 0.59, u);
                        float bL = dot(Pr, L);
                        float c2 = dot(Pr, Pr) - 1.0;
                        float shadow = (bL < 0.0 && bL * bL > c2) ? 0.2 : 1.0;
                        float amb = _PlanetAmbient;
                        float3 ringCol = _RingColor.rgb * (amb + (1.0 - amb) * (0.55 + 0.45 * abs(dot(A, L)))) * shadow;
                        float ringA = saturate(_RingColor.a * bands);
                        float4 ring = float4(ringCol * ringA, ringA);
                        bool front = zr > n.z || discA <= 0.0;
                        outc = front ? Over(ring, surf) : Over(surf, ring);
                    }
                }

                // Outer glow: atmosphere (lit side only), coma, red glow, corona (with slow rays).
                float gs = _Surf4.z, gStr = _Surf4.w;
                if (gs > 0.0 && gStr > 0.0 && r > 0.9)
                {
                    float g = saturate(1.0 - (r - 1.0) / gs);
                    float halo = (g * g * 0.55 + pow(g, 6.0) * 0.45) * gStr;
                    if (isStar)
                    {
                        float2 dir = p / r;
                        halo *= 0.78 + 0.4 * VNoise(float3(dir * 3.0, t * 0.25 + seed));
                    }
                    if (isTerran) halo *= litSide;
                    outc.rgb += _GlowColor.rgb * halo * (1.0 - discA);
                }

                // Comet tail, away from the light, behind the nucleus.
                if (_Extra.x > 0.0)
                {
                    float2 td = -litDir2;
                    float a = dot(p, td);
                    float b = dot(p, float2(-td.y, td.x));
                    float w = max(_Tail.x, 1e-3) + max(a, 0.0) * _Tail.y;
                    float along = saturate(a / _Extra.x);
                    float tail = exp(-(b * b) / (w * w) * 2.0) * smoothstep(-0.2, 0.6, a) * pow(1.0 - along, max(_Tail.z, 0.01));
                    tail *= 0.7 + 0.3 * VNoise(float3(b * 4.0, a * 1.2 - t * 1.6, seed));
                    outc.rgb += _GlowColor.rgb * tail * 1.1 * (1.0 - discA);
                }

                return outc * _Extra.w;
            }
            ENDHLSL
        }
    }
}
