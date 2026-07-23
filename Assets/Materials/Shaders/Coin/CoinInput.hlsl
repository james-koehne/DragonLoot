#ifndef DRAGONLOOT_COIN_INPUT_INCLUDED
#define DRAGONLOOT_COIN_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Metallic;
    half _Smoothness;
    half _BumpScale;
    half _OcclusionStrength;
    half _ReflectionFloor;
    half4 _FresnelColor;
    half _FresnelIntensity;
    half _FresnelPower;
    half _TintVariation;
    half _SmoothnessVariation;
    half _SpecularVariation;
    half _HueVariation;
    half _ValueVariation;
    half _RoughnessUvScale;
    half _RoughnessUvAmount;
    half _EdgeWearStrength;
    half _DirtStrength;
    half _Cutoff;
    half _Surface;
    half _Cull;
CBUFFER_END

// Per-renderer seed via MPB. Must stay out of UnityPerMaterial so GPU instancing
// / SRP Batcher do not ignore the override and fall back to a moving world hash.
UNITY_INSTANCING_BUFFER_START(DragonLootCoinVars)
    UNITY_DEFINE_INSTANCED_PROP(float, _VariationSeed)
UNITY_INSTANCING_BUFFER_END(DragonLootCoinVars)

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EdgeWearMap);        SAMPLER(sampler_EdgeWearMap);
TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);

float CoinReadVariationSeed()
{
    return UNITY_ACCESS_INSTANCED_PROP(DragonLootCoinVars, _VariationSeed);
}

#endif
