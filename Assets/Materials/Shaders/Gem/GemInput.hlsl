#ifndef DRAGONLOOT_GEM_INPUT_INCLUDED
#define DRAGONLOOT_GEM_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _InternalColor;
    half4 _FresnelColor;
    half _BumpScale;
    half _OcclusionStrength;
    half _FacetScale;
    half _FacetStrength;
    half _LightSteps;
    half _LightStepBlend;
    half _ShadowStrength;
    half _LightWrap;
    half _DirectLightScale;
    half _AmbientFill;
    half _CatchLightIntensity;
    half _CatchLightSize;
    half _GraphicShine;
    half _ReflectionFloor;
    half _ShineIntensity;
    half _ShineSize;
    half _ShineThreshold;
    half _RimIntensity;
    half _RimPower;
    half _SparkleIntensity;
    half _SparkleDensity;
    half _SparkleSharpness;
    half _ConnectedGlow;
    half4 _ConnectedGlowColor;
    half _BrightnessVariation;
    half _SaturationVariation;
    half _HueVariation;
    half _SparkleVariation;
    half _ScratchStrength;
    half _EdgeWearStrength;
    half _InclusionStrength;
    half _Cutoff;
    half _Surface;
    half _Cull;
CBUFFER_END

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_ScratchMap);         SAMPLER(sampler_ScratchMap);
TEXTURE2D(_EdgeWearMap);        SAMPLER(sampler_EdgeWearMap);

#endif
