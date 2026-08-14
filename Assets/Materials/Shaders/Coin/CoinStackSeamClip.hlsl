#ifndef DRAGONLOOT_COIN_STACK_SEAM_CLIP_INCLUDED
#define DRAGONLOOT_COIN_STACK_SEAM_CLIP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "CoinStackBand.hlsl"

struct CoinStackSeamView
{
    float verticalMask;
    float viewFade;
    float lateralAmt;
    float lateralSign;
    float2 radialWS;
    float2 viewRight;
    float along;
    float rimMask;
    bool valid;
};

// Stable lateral seam offset from static instance seed + coin index (not world position),
// so flying / relocating a stack does not reshuffle seams.
float2 CoinStackCoinOffsetFromSeed(float coinIndex, float instanceSeed)
{
    float h0 = CoinStackHash11(instanceSeed * 12.9898 + coinIndex * 78.233);
    float h1 = CoinStackHash11(instanceSeed * 37.719 + coinIndex * 19.19 + 17.13);
    return (float2(h0, h1) - 0.5) * 2.0 * (float)_SeamWorldOffsetMax;
}

float2 CoinStackCoinOffsetFromWorldPos(float3 coinCenterWS)
{
    // Legacy helper kept for compatibility; prefer CoinStackCoinOffsetFromSeed.
    float qx = floor(coinCenterWS.x * 64.0 + 0.5);
    float qy = floor(coinCenterWS.y * 64.0 + 0.5);
    float qz = floor(coinCenterWS.z * 64.0 + 0.5);
    float h0 = CoinStackHash11(qx * 0.1031 + qy * 0.371 + qz * 0.719);
    float h1 = CoinStackHash11(qx * 0.271 + qy * 0.533 + qz * 0.911 + 17.13);
    return (float2(h0, h1) - 0.5) * 2.0 * (float)_SeamWorldOffsetMax;
}

float3 CoinStackCoinCenterWS(float coinIndex, float coinCount)
{
    float count = max(coinCount, 1.0);
    float index = clamp(coinIndex, 0.0, count - 1.0);
    float minY = (float)_MeshBoundsMinY;
    float sizeY = max((float)_MeshBoundsSizeY, 1e-5);

    float3 bottomWS = TransformObjectToWorld(float3(0.0, minY, 0.0));
    float3 topWS = TransformObjectToWorld(float3(0.0, minY + sizeY, 0.0));
    float stackHeight = max(abs(topWS.y - bottomWS.y), 1e-5);
    float coinHeight = stackHeight / count;
    float ySign = topWS.y >= bottomWS.y ? 1.0 : -1.0;

    return float3(
        bottomWS.x,
        bottomWS.y + (index + 0.5) * coinHeight * ySign,
        bottomWS.z);
}

CoinStackSeamView CoinStackEvaluateSeamView(
    float3 positionWS,
    float3 normalOS,
    float stackY01,
    float coinCount,
    float instanceSeed,
    float grooveMask,
    float ridgeMask)
{
    CoinStackSeamView seam = (CoinStackSeamView)0;
    seam.valid = false;

    if (CoinStackIsCap(normalOS))
        return seam;

    float count = max(coinCount, 1.0);
    float coinIndex = floor(stackY01 * count);
    coinIndex = clamp(coinIndex, 0.0, count - 1.0);

    // Full coin side by default (ridge + groove). Grooves Only restricts to grooveMask.
    if ((float)_SeamClipGrooveOnly >= 0.5)
        seam.verticalMask = grooveMask;
    else
        seam.verticalMask = 1.0;

    if (seam.verticalMask <= 1e-4)
        return seam;

    float3 coinCenterWS = CoinStackCoinCenterWS(coinIndex, count);
    float2 toPixelXZ = positionWS.xz - coinCenterWS.xz;
    float radialLen = length(toPixelXZ);
    if (radialLen < 1e-5)
        return seam;

    seam.radialWS = toPixelXZ / radialLen;

    float2 offsetWS = CoinStackCoinOffsetFromSeed(coinIndex, instanceSeed);
    float offLen = length(offsetWS);

    float2 viewXZ = _WorldSpaceCameraPos.xz - positionWS.xz;
    float viewLen = length(viewXZ);
    if (viewLen < 1e-5)
        return seam;
    viewXZ /= viewLen;

    seam.viewFade = 1.0;
    if (offLen > 1e-6)
    {
        float2 offDir = offsetWS / offLen;
        float align = abs(dot(viewXZ, offDir));
        seam.viewFade = 1.0 - smoothstep((float)_SeamViewAlignStart, (float)_SeamViewAlignEnd, align);
    }

    if (seam.viewFade <= 1e-4)
        return seam;

    seam.viewRight = float2(-viewXZ.y, viewXZ.x);
    float lateral = dot(offsetWS, seam.viewRight);
    float maxOff = max((float)_SeamWorldOffsetMax, 1e-5);
    seam.lateralAmt = saturate(abs(lateral) / maxOff);
    if (seam.lateralAmt <= 1e-4)
        return seam;

    seam.lateralSign = lateral >= 0.0 ? 1.0 : -1.0;
    seam.along = dot(seam.radialWS, seam.viewRight) * seam.lateralSign;

    float width = saturate((float)_SeamClipWidth);
    float cutThreshold = 1.0 - width;
    seam.rimMask = smoothstep(cutThreshold, cutThreshold + width * 0.25, seam.along);
    seam.valid = true;
    return seam;
}

float CoinStackSeamEffectWeight(CoinStackSeamView seam)
{
    if (!seam.valid)
        return 0.0;
    return seam.verticalMask * seam.viewFade * seam.lateralAmt;
}

void CoinStackClipSeamSide(CoinStackSeamView seam)
{
    if ((float)_SeamSideClip <= 1e-5)
        return;

    float weight = CoinStackSeamEffectWeight(seam);
    if (weight <= 1e-4)
        return;

    float discardWeight = weight * seam.rimMask * (float)_SeamSideClip;
    clip(0.001 - discardWeight);
}

half CoinStackSeamSoftAO(CoinStackSeamView seam)
{
    if ((float)_SeamSoftAO <= 1e-5)
        return 1.0h;

    float weight = CoinStackSeamEffectWeight(seam);
    if (weight <= 1e-4)
        return 1.0h;

    float ao = weight * seam.rimMask * (float)_SeamSoftAO;
    return (half)saturate(1.0 - ao);
}

half3 CoinStackApplySeamNormalWS(half3 normalWS, CoinStackSeamView seam)
{
    if ((float)_SeamNormalStrength <= 1e-5)
        return normalWS;

    float weight = CoinStackSeamEffectWeight(seam);
    if (weight <= 1e-4)
        return normalWS;

    // Tilt lighting toward the visible overhang (opposite the AO/clip rim).
    float tilt = weight * (float)_SeamNormalStrength;
    float3 lateralWS = float3(seam.viewRight.x, 0.0, seam.viewRight.y) * seam.lateralSign;
    float3 n = (float3)normalWS;
    n += lateralWS * (tilt * (0.5 - seam.along));
    n += lateralWS * (tilt * 0.35 * (1.0 - seam.rimMask));
    return NormalizeNormalPerPixel(n);
}

#endif
