#ifndef DRAGONLOOT_ENVIRONMENT_LIT_INPUT_INCLUDED
#define DRAGONLOOT_ENVIRONMENT_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Metallic;
    half _Smoothness;
    half _BumpScale;
    half _OcclusionStrength;
    half4 _EmissionColor;
    half _EmissionIntensity;
    half _RimIntensityScale;
    half _SpecularIntensityScale;
    half _WrapOverride;
    half _ReceiveShadowsLocal;
    half _ReflectionsLocal;
    half _Cutoff;
    half _Surface;
    half _Cull;
CBUFFER_END

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);

#endif
