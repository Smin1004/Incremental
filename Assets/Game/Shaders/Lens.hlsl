// 24 §24.2-2, 26 §12.2: the lenses. Up to four of them are handed to every shader as global values (LensSlots.cs):
// 0 the black hole, 1 the gravity source, 2 the implode wave, 3 the bundle ripple. The background stars, the dust and
// the trails are drawn moved around each one. Only where things are drawn moves; the simulation does not. No screen
// copy is taken.
#ifndef INCREMENTAL_LENS_INCLUDED
#define INCREMENTAL_LENS_INCLUDED

#define LENS_SLOTS 4
#define LENS_HOLE 0
#define LENS_BULGE 1
#define LENS_BAND 2

// Per slot. A: xy centre (world), z radius (world), w push (in radii for a hole and a bulge, in band widths for a band).
float4 _LensA[LENS_SLOTS];
// B: x reach in radii (hole), y twist in radians at the edge (counter-clockwise, like the vortex), z band width as a
// share of the radius (band), w kind (0 hole, 1 bulge, 2 band).
float4 _LensB[LENS_SLOTS];
// The black hole's own extras (slot 0): x unused, y brightness of the light ring, z ring width as a share of the radius,
// w how far the dust inside the disc is cleared out of it (0 = not at all, 1 = fully: 24 §24.3).
float4 _BHLensShape;

bool LensOn(int i) { return _LensA[i].w > 0.0 || _LensB[i].y != 0.0; }

// How far (world) a point at distance r from slot i's centre is pushed outward.
float LensPushAt(int i, float r)
{
    float4 a = _LensA[i];
    float4 b = _LensB[i];
    float radius = max(a.z, 1e-4);
    float u = r / radius;
    int kind = (int)round(b.w);
    if (kind == LENS_BULGE)
    {
        // 26 §12.2 D1: a soft swelling with no hole: nothing moves at the centre or past the edge, most a third of the way.
        if (u >= 1.0) return 0.0;
        return a.w * radius * 6.75 * u * (1.0 - u) * (1.0 - u);
    }
    if (kind == LENS_BAND)
    {
        // 26 §12.2 D2, D4: a band round the circle of the radius pushes outward and lets go again behind it.
        float w = max(b.z * radius, 1e-4);
        float x = (r - radius) / w;
        return a.w * w * exp(-x * x);
    }
    // The black hole: inside it the push grows from 0 at the centre so that nothing jumps (it is under the black disc
    // there anyway), then falls to nothing at b.x radii.
    float reach = max(b.x, 1.001);
    if (u >= reach) return 0.0;
    if (u <= 1.0) return a.w * radius * u;
    float f = 1.0 - (u - 1.0) / (reach - 1.0);
    return a.w * radius * f * f;
}

// 26 §12.2 D3: how far (radians) a point at distance r is turned round slot i's centre: all of it inside the edge,
// fading to nothing at the reach (a bulge fades to its edge).
float LensTwistAt(int i, float r)
{
    float4 b = _LensB[i];
    if (b.y == 0.0) return 0.0;
    float u = r / max(_LensA[i].z, 1e-4);
    int kind = (int)round(b.w);
    if (kind == LENS_BAND) return 0.0;
    if (kind == LENS_BULGE) return u >= 1.0 ? 0.0 : b.y * (1.0 - u) * (1.0 - u);
    float reach = max(b.x, 1.001);
    if (u <= 1.0) return b.y;
    if (u >= reach) return 0.0;
    float f = 1.0 - (u - 1.0) / (reach - 1.0);
    return b.y * f * f;
}

// 24 §24.3: the black hole's push for what is drawn on top of the disc (dust and trail vertices). Inside the disc
// nothing may stay on it, or the black hole reads grey under a dense field: the centre goes to just outside the edge,
// radius x (1 + ring width), and the edge itself to where the edge from outside is drawn, radius x (1 + push). So the
// disc stays black and the dust that was over it piles up into a bright ring round it, continuous across the edge.
float LensForwardPush(int i, float r)
{
    if (i != 0 || (int)round(_LensB[0].w) != LENS_HOLE || _BHLensShape.w <= 0.0) return LensPushAt(i, r);
    float radius = max(_LensA[0].z, 1e-4);
    float u = r / radius;
    if (u > 1.0) return LensPushAt(0, r);
    float inner = radius * (1.0 + _BHLensShape.z * _BHLensShape.w);
    float outer = radius * (1.0 + _LensA[0].w);
    float cleared = inner + (max(outer, inner) - inner) * u;
    return lerp(LensPushAt(0, r), cleared - r, _BHLensShape.w);
}

float2 Rotate2(float2 v, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// A world position as it is drawn under the lenses (dust and trail vertices).
float2 LensForward(float2 p)
{
    [unroll]
    for (int i = 0; i < LENS_SLOTS; i++)
    {
        if (!LensOn(i)) continue;
        float2 d = p - _LensA[i].xy;
        float r = length(d);
        // The very centre has no direction; it goes up, like any point it would be pushed out with.
        if (r < 1e-6) { d = float2(0.0, 1e-6); r = 1e-6; }
        float2 dir = Rotate2(d / r, LensTwistAt(i, r));
        p = _LensA[i].xy + dir * (r + LensForwardPush(i, r));
    }
    return p;
}

// The world position whose picture lands at p under the lenses (the background is drawn per pixel, so it looks back).
// The push is undone by looking it up twice, which is close enough for a lens this soft.
float2 LensBackward(float2 p)
{
    [unroll]
    for (int i = LENS_SLOTS - 1; i >= 0; i--)
    {
        if (!LensOn(i)) continue;
        float2 d = p - _LensA[i].xy;
        float r = length(d);
        if (r < 1e-6) continue;
        float src = max(r - LensPushAt(i, r), 0.0);
        src = max(r - LensPushAt(i, src), 0.0);
        float2 dir = Rotate2(d / r, -LensTwistAt(i, src));
        p = _LensA[i].xy + dir * src;
    }
    return p;
}

// The thin ring of light just outside the black hole's edge, 0..1 brightness (x _BHLensShape.y).
float LensRing(float2 p)
{
    if (_LensA[0].w <= 0.0 || _BHLensShape.y <= 0.0 || (int)round(_LensB[0].w) != LENS_HOLE) return 0.0;
    float radius = max(_LensA[0].z, 1e-4);
    float w = max(_BHLensShape.z, 1e-3);
    float u = length(p - _LensA[0].xy) / radius;
    float x = (u - 1.0 - w) / w;
    return exp(-x * x) * _BHLensShape.y;
}

#endif
