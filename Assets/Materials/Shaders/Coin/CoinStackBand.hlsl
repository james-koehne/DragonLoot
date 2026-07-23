#ifndef DRAGONLOOT_COIN_STACK_BAND_INCLUDED
#define DRAGONLOOT_COIN_STACK_BAND_INCLUDED

#include "CoinStackMaterial.hlsl"

float CoinStackHash11(float p)
{
    float3 p3 = frac(float3(p, p, p) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float CoinStackInstanceSeed()
{
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float seed = dot(origin, float3(12.9898, 78.233, 37.719));
#if defined(UNITY_INSTANCING_ENABLED)
    seed += (float)unity_InstanceID * 19.19;
#endif
    return CoinStackHash11(seed);
}

float CoinStackComputeStackY01(float positionYOS)
{
    float sizeY = max((float)_MeshBoundsSizeY, 1e-5);
    return saturate((positionYOS - (float)_MeshBoundsMinY) / sizeY);
}

bool CoinStackIsCap(float3 normalOS)
{
    return abs(normalOS.y) > (float)_CapNormalThreshold;
}

float CoinStackGrooveMaskFromBand(float band)
{
    float w = max((float)_GrooveWidth, 1e-4);
    float soft = max((float)_RidgeSoftness, 0.0);
    float edge = w + soft;
    float groove = 1.0 - smoothstep(0.0, edge, band) * smoothstep(0.0, edge, 1.0 - band);
    return saturate(groove * (float)_BandContrast);
}

struct CoinStackBandData
{
    float band01;
    float grooveMask;
    float ridgeMask;
    float coinIndex;
    half bandShade;
};

CoinStackBandData CoinStackEvaluateBands(float stackY01, float coinCount)
{
    CoinStackBandData data = (CoinStackBandData)0;
    data.band01 = frac(stackY01 * coinCount);
    data.grooveMask = CoinStackGrooveMaskFromBand(data.band01);
    data.ridgeMask = saturate(1.0 - data.grooveMask);
    data.coinIndex = floor(stackY01 * coinCount);

    half shade = lerp(1.0h, 1.0h - _GrooveDarkness, (half)data.grooveMask);
    shade = lerp(shade, shade + (half)_RidgeAlbedoBoost, (half)data.ridgeMask);
    data.bandShade = shade;
    return data;
}

float CoinStackGrooveMaskAtStackY(float stackY01, float coinCount)
{
    float band = frac(stackY01 * coinCount);
    return CoinStackGrooveMaskFromBand(band);
}

float3 CoinStackComputeSideNormalOS(float3 normalOS, float stackY01, float coinCount)
{
    CoinStackBandData bands = CoinStackEvaluateBands(stackY01, coinCount);
    float band = bands.band01;

    float3 radial = float3(normalOS.x, 0.0, normalOS.z);
    float radialLen = length(radial);
    if (radialLen < 1e-5)
        radial = float3(1.0, 0.0, 0.0);
    else
        radial /= radialLen;

    float eps = 1.0 / max(coinCount * 256.0, 1.0);
    float g0 = CoinStackGrooveMaskAtStackY(stackY01, coinCount);
    float g1 = CoinStackGrooveMaskAtStackY(stackY01 + eps, coinCount);
    float dg = (g1 - g0) / eps;

    float grooveStr = (float)_GrooveNormalStrength * (float)_GrooveNormalBias;
    float3 nOS = radial + float3(0.0, -dg * grooveStr, 0.0);

    float bevelW = max((float)_CoinEdgeBevelWidth, 1e-4);
    float seamProx = min(band, 1.0 - band);
    float bevelMask = 1.0 - smoothstep(0.0, bevelW, seamProx);
    bevelMask *= (float)_CoinEdgeBevelStrength;
    float ySign = band < 0.5 ? -1.0 : 1.0;
    float3 bevelDir = normalize(float3(radial.x, ySign, radial.z));
    nOS = normalize(lerp(nOS, bevelDir, bevelMask));

    if ((float)_SideFacetStrength > 1e-5)
    {
        float ang = atan2(normalOS.x, normalOS.z) * (float)_SideFacetFrequency;
        float facet = sin(ang) * (float)_SideFacetStrength;
        nOS = normalize(nOS + radial * facet);
    }

    return nOS;
}

#endif
