// Hash and value noise shared by the procedural shaders. No textures.
// Hashes: "Hash without Sine" (Dave Hoskins), stable on large inputs and on every GPU.
#ifndef INCREMENTAL_NOISE_INCLUDED
#define INCREMENTAL_NOISE_INCLUDED

float Hash31(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

float3 Hash33(float3 p3)
{
    p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

float Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// Trilinear value noise with smoothstep fade, 0..1.
float VNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = x - i;
    float3 u = f * f * (3.0 - 2.0 * f);
    float a = Hash31(i);
    float b = Hash31(i + float3(1, 0, 0));
    float c = Hash31(i + float3(0, 1, 0));
    float d = Hash31(i + float3(1, 1, 0));
    float e = Hash31(i + float3(0, 0, 1));
    float f1 = Hash31(i + float3(1, 0, 1));
    float g = Hash31(i + float3(0, 1, 1));
    float h = Hash31(i + float3(1, 1, 1));
    return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), lerp(lerp(e, f1, u.x), lerp(g, h, u.x), u.y), u.z);
}

// Fractal sums, normalized to 0..1 (values gather around 0.5).
float Fbm3(float3 x)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 3; k++) { s += a * VNoise(x); x = x * 2.02 + float3(1.7, 9.2, 5.3); a *= 0.5; }
    return s / 0.875;
}

float Fbm4(float3 x)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 4; k++) { s += a * VNoise(x); x = x * 2.02 + float3(1.7, 9.2, 5.3); a *= 0.5; }
    return s / 0.9375;
}

float Fbm5(float3 x)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 5; k++) { s += a * VNoise(x); x = x * 2.02 + float3(1.7, 9.2, 5.3); a *= 0.5; }
    return s / 0.96875;
}

#endif
