#ifndef DRAGONLOOT_COIN_COMMON_INCLUDED
#define DRAGONLOOT_COIN_COMMON_INCLUDED

#include "CoinInput.hlsl"

// Cheap deterministic hash — no procedural noise loops.
float CoinHash11(float p)
{
    float3 p3 = frac(float3(p, p, p) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float CoinHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

half3 CoinHueShift(half3 color, half shift)
{
    half3 k = half3(0.57735, 0.57735, 0.57735);
    half cosA = cos(shift);
    half sinA = sin(shift);
    return color * cosA + cross(k, color) * sinA + k * dot(k, color) * (1.0h - cosA);
}

// Stable seed only — never hash world position. CoinHash11 is chaotic, so any
// transform jitter completely reshuffles tint/smoothness (looks like flicker).
float CoinInstanceSeed()
{
    float seed = CoinReadVariationSeed();
    if (seed <= 0.0)
        seed = 1.0;
    return CoinHash11(seed);
}

struct CoinVariation
{
    half tintScale;
    half smoothnessScale;
    half specularScale;
    half hueShift;
    half valueScale;
};

CoinVariation CoinBuildVariation(float seed)
{
    CoinVariation v;
    half t = (half)CoinHash11(seed);
    half s = (half)CoinHash11(seed + 17.13);
    half p = (half)CoinHash11(seed + 31.71);
    half h = (half)CoinHash11(seed + 53.97);
    half val = (half)CoinHash11(seed + 79.31);

    // Map [0,1] → [-1,1] then scale by authored ranges.
    v.tintScale = 1.0h + (t * 2.0h - 1.0h) * _TintVariation;
    v.smoothnessScale = 1.0h + (s * 2.0h - 1.0h) * _SmoothnessVariation;
    v.specularScale = 1.0h + (p * 2.0h - 1.0h) * _SpecularVariation;
    v.hueShift = (h * 2.0h - 1.0h) * _HueVariation;
    v.valueScale = 1.0h + (val * 2.0h - 1.0h) * _ValueVariation;
    return v;
}

void CoinApplyAlbedoVariation(inout half3 albedo, CoinVariation v)
{
    albedo *= v.tintScale;
    albedo = CoinHueShift(albedo, v.hueShift);
    albedo *= v.valueScale;
}

// UV-derived roughness micro-variation (hash of quantized UVs — not FBM noise).
half CoinUvRoughnessScale(float2 uv)
{
    float2 cell = floor(uv * max((float)_RoughnessUvScale, 1.0));
    half h = (half)CoinHash21(cell);
    return 1.0h + (h * 2.0h - 1.0h) * _RoughnessUvAmount;
}

half CoinSchlickFresnel(half ndotv, half power)
{
    half inv = 1.0h - saturate(ndotv);
    return pow(inv, max(power, 0.01h));
}

#endif
