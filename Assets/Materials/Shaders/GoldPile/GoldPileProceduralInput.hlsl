#ifndef DRAGONLOOT_GOLDPILE_PROCEDURAL_INPUT_INCLUDED
#define DRAGONLOOT_GOLDPILE_PROCEDURAL_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _GapColor;
    half _Metallic;
    half _Smoothness;
    half _GapMetallic;
    half _GapSmoothness;
    half _BumpScale;
    half _CoinNormalStrength;

    half _DeformEnabled;
    half _DeformScale;
    half _DeformNormalSoften;
    half _DeformSampleBlur;
    float _DeformWorldSize;
    float _DeformResolution;
    half _GroundLevelHeight;

    float _CoinDiameter;
    float _CoinDensity;
    half _CellJitter;
    half _RotationRandomness;
    half _CoinTilt;
    half _RimBevelStrength;
    half _RimBevelWidth;
    half _RimAoStrength;
    half _BurialAmount;
    half _TintVariation;
    half _SmoothnessVariation;
    half _SpecularVariation;
    half _HueVariation;
    half _ValueVariation;
    half _MetallicVariation;
    half _EdgeHighlightStrength;
    half _EdgeWidth;
    half4 _CoinUVCenter;
    half _CoinUVScale;
    half _CopperAmount;
    half _SilverAmount;
    half4 _CopperColor;
    half4 _SilverColor;
    half _DirtStrength;
    half _AOStrength;

    half _DistantCoinEnabled;
    half4 _DistantCoinColor0;
    half4 _DistantCoinColor1;
    half4 _DistantCoinColor2;
    half4 _DistantCoinColor3;
    half _DistantCoinAmount0;
    half _DistantCoinAmount1;
    half _DistantCoinAmount2;
    half _DistantCoinAmount3;
    half _DistantCoinDensity;
    half _DistantCoinCoverage;
    half _DistantCoinIntensity;
    half _DistantCoinMaxPixels;
    half _DistantCoinGrowFar;
    half _DistantCoinGrowNear;
    half _DistantCoinFadeStart;
    half _DistantCoinFadeEnd;
    half _DistantCoinTopMask;
    half _DistantCoinMetalFocusEnabled;
    half _DistantCoinMetalFocus;
    half _DistantCoinMetalFocusPower;

    half _ReflectionFloor;
    half4 _FresnelColor;
    half _FresnelIntensity;
    half _FresnelPower;

    half _DisableLod;
    half _LodNear;
    half _LodMid;
    half _LodFar;
    half _LodNoiseFade;

    half _Cutoff;
    half _Surface;
CBUFFER_END

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_CopperBaseMap);      SAMPLER(sampler_CopperBaseMap);
TEXTURE2D(_SilverBaseMap);      SAMPLER(sampler_SilverBaseMap);
TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);
TEXTURE2D(_DeformMap);          SAMPLER(sampler_DeformMap);

#endif
