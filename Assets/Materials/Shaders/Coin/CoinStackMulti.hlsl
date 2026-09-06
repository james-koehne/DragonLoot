#ifndef DRAGONLOOT_COIN_STACK_MULTI_INCLUDED
#define DRAGONLOOT_COIN_STACK_MULTI_INCLUDED

// Max type slots for per-type fresnel (Texture2DArray depth may be smaller).
#define COIN_STACK_MAX_TYPES 8

TEXTURE2D_ARRAY(_BaseMapArray);
SAMPLER(sampler_BaseMapArray);
TEXTURE2D_ARRAY(_BumpMapArray);
SAMPLER(sampler_BumpMapArray);
TEXTURE2D_ARRAY(_MetallicGlossMapArray);
SAMPLER(sampler_MetallicGlossMapArray);

TEXTURE2D(_CoinTypeMap);
SAMPLER(sampler_CoinTypeMap);

float4 _TypeFresnelColor[COIN_STACK_MAX_TYPES];
float _TypeCount;

int CoinStackClampTypeId(int typeId)
{
    int maxType = max((int)_TypeCount - 1, 0);
    maxType = min(maxType, COIN_STACK_MAX_TYPES - 1);
    return clamp(typeId, 0, maxType);
}

int CoinStackLoadTypeId(int coinIndex)
{
    int idx = max(coinIndex, 0);
    // RFloat LUT: red channel stores type index as float (0, 1, 2, ...).
    float raw = LOAD_TEXTURE2D(_CoinTypeMap, int2(idx, 0)).r;
    return CoinStackClampTypeId((int)round(raw));
}

int CoinStackResolveTypeId(float stackY01, float coinCount, bool isCap, float3 normalOS, float bakedCoinIndex)
{
    int baseIndex = (int)round((float)_ChunkBaseIndex);
    if ((float)_UseBakedCoinIndex > 0.5)
        return CoinStackLoadTypeId((int)round(bakedCoinIndex) + baseIndex);

    float count = max(coinCount, 1.0);
    if (isCap)
    {
        // Bottom cap (normal.y < 0) uses type 0; top cap uses top coin.
        if (normalOS.y < 0.0)
            return CoinStackLoadTypeId(baseIndex);
        return CoinStackLoadTypeId(baseIndex + (int)count - 1);
    }

    float coinIndex = floor(saturate(stackY01) * count);
    coinIndex = min(coinIndex, count - 1.0);
    return CoinStackLoadTypeId(baseIndex + (int)coinIndex);
}

half4 CoinStackSampleAlbedoMulti(float2 uv, int typeId)
{
    return SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_BaseMapArray, uv, typeId) * _BaseColor;
}

half4 CoinStackSampleMaskMulti(float2 uv, int typeId)
{
    return SAMPLE_TEXTURE2D_ARRAY(_MetallicGlossMapArray, sampler_MetallicGlossMapArray, uv, typeId);
}

half3 CoinStackSampleNormalTSMulti(float2 uv, int typeId, half bumpScale)
{
    return UnpackNormalScale(
        SAMPLE_TEXTURE2D_ARRAY(_BumpMapArray, sampler_BumpMapArray, uv, typeId),
        bumpScale);
}

half3 CoinStackTypeFresnelRgb(int typeId)
{
    int id = CoinStackClampTypeId(typeId);
    return (half3)_TypeFresnelColor[id].rgb;
}

#endif
