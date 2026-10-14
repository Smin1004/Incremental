// 24 §24.2-2: the black hole's lens. BlackHoleAbility hands the black hole to every shader as two global values, and
// the background stars, the dust and the trails are drawn pushed outward around it: most at its edge, nothing past
// _BHLensShape.x radii. Only where things are drawn moves; the simulation does not. No screen copy is taken.
#ifndef INCREMENTAL_LENS_INCLUDED
#define INCREMENTAL_LENS_INCLUDED

// xy centre (world), z radius (world), w push at the edge in radii (0 = no lens).
float4 _BHLens;
// x reach in radii, y brightness of the light ring, z ring width as a share of the radius.
float4 _BHLensShape;

// How far (world) a point at distance r is pushed outward. Inside the hole the push grows from 0 at the centre so that
// nothing jumps; it is under the black disc there anyway.
float LensPushAt(float r)
{
    float radius = max(_BHLens.z, 1e-4);
    float u = r / radius;
    float reach = max(_BHLensShape.x, 1.001);
    if (u >= reach) return 0.0;
    if (u <= 1.0) return _BHLens.w * radius * u;
    float f = 1.0 - (u - 1.0) / (reach - 1.0);
    return _BHLens.w * radius * f * f;
}

// A world position as it is drawn under the lens (dust and trail vertices).
float2 LensForward(float2 p)
{
    if (_BHLens.w <= 0.0) return p;
    float2 d = p - _BHLens.xy;
    float r = length(d);
    if (r < 1e-6) return p;
    return p + d / r * LensPushAt(r);
}

// The world position whose picture lands at p under the lens (the background is drawn per pixel, so it looks back).
float2 LensBackward(float2 p)
{
    if (_BHLens.w <= 0.0) return p;
    float2 d = p - _BHLens.xy;
    float r = length(d);
    if (r < 1e-6) return p;
    float src = max(r - LensPushAt(r), 0.0);
    return _BHLens.xy + d / r * src;
}

// The thin ring of light just outside the edge, 0..1 brightness (x _BHLensShape.y).
float LensRing(float2 p)
{
    if (_BHLens.w <= 0.0 || _BHLensShape.y <= 0.0) return 0.0;
    float radius = max(_BHLens.z, 1e-4);
    float w = max(_BHLensShape.z, 1e-3);
    float u = length(p - _BHLens.xy) / radius;
    float x = (u - 1.0 - w) / w;
    return exp(-x * x) * _BHLensShape.y;
}

#endif
