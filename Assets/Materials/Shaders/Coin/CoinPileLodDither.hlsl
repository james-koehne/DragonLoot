#ifndef DRAGONLOOT_COIN_PILE_LOD_DITHER_INCLUDED
#define DRAGONLOOT_COIN_PILE_LOD_DITHER_INCLUDED

// Pushed from GoldPileLootStreamSettings.PushCoinDitherGlobals().
float _GoldPileCoinLod0End;
float _GoldPileCoinLod1End;
float _GoldPileCoinLod2End;
float _GoldPileCoinDitherFade;
float _GoldPileCoinLodKeep1;
float _GoldPileCoinLodKeep2;
// 1 when drawing GPU coin instances into the sparkle mask; ignored when COIN_PILE_FORCE_LOD_DITHER.
float _GoldPileCoinDitherEnable;

// Instance matrices pack cull priority in m33 as (1 + priority), priority in [0,1).
// Lower priority = kept farther (lod2 subset); higher = lod0-only extras that dither out first.
float CoinPileLodPriorityFromMatrix()
{
    return saturate(UNITY_MATRIX_M._m33 - 1.0);
}

// Keep fraction of the lod0 set that should remain solid at this planar distance.
float CoinPileLodKeepThreshold(float dist)
{
    float fade = max(_GoldPileCoinDitherFade, 0.1);
    float lod0 = max(_GoldPileCoinLod0End, 0.1);
    float lod1 = max(_GoldPileCoinLod1End, lod0 + 0.01);
    float lod2 = max(_GoldPileCoinLod2End, lod1 + 0.01);
    float keep1 = saturate(_GoldPileCoinLodKeep1);
    float keep2 = saturate(_GoldPileCoinLodKeep2);

    float keep = 1.0;
    if (dist > lod0)
        keep = lerp(1.0, keep1, saturate((dist - lod0) / fade));
    if (dist > lod1)
        keep = lerp(keep1, keep2, saturate((dist - lod1) / fade));
    if (dist > lod2 - fade)
        keep = lerp(keep2, 0.0, saturate((dist - (lod2 - fade)) / fade));
    return keep;
}

float CoinPileLodVisibility(float3 positionWS)
{
    float2 camXZ = _WorldSpaceCameraPos.xz;
    float dist = distance(positionWS.xz, camXZ);
    float keep = CoinPileLodKeepThreshold(dist);
    float priority = CoinPileLodPriorityFromMatrix();
    // Soft band so instances cross the keep threshold via screen dither instead of popping.
    float soft = 0.04;
    return saturate((keep - priority) / soft);
}

void CoinPileApplyLodDither(float3 positionWS, float4 positionCS)
{
#if !defined(COIN_PILE_FORCE_LOD_DITHER)
    if (_GoldPileCoinDitherEnable < 0.5)
        return;
#endif
    float visibility = CoinPileLodVisibility(positionWS);
    float noise = InterleavedGradientNoise(positionCS.xy, 0);
    clip(visibility - noise);
}

#endif
