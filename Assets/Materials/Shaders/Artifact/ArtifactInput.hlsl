#ifndef DRAGONLOOT_ARTIFACT_INPUT_INCLUDED
#define DRAGONLOOT_ARTIFACT_INPUT_INCLUDED

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
    half4 _EmissionColor;
    half4 _FresnelColor;
    half _FresnelIntensity;
    half _FresnelPower;
    half _CleanSmoothnessBoost;
    half _CleanFresnelBoost;
    half4 _MatCapColor;
    half _MatCapIntensity;
    half _MatCapPower;
    half4 _ShineColor;
    half _ShineBoost;
    half _DirtStrength;
    half _DirtMapStrength;
    half4 _DirtColor;
    half _DirtContrast;
    half _DirtMetallicLoss;
    half _DirtSmoothnessLoss;
    half _DirtAlbedoMultiply;
    half _Cutoff;
    half _Surface;
    half _Cull;
CBUFFER_END

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);
TEXTURE2D(_MatCap);             SAMPLER(sampler_MatCap);
TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);

#endif
