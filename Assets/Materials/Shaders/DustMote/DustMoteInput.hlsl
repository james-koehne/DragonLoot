#ifndef DRAGONLOOT_DUST_MOTE_INPUT_INCLUDED
#define DRAGONLOOT_DUST_MOTE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half4 _CoreColor;
	half4 _TwinkleColor;
	half _Exposure;
	half _WarmBias;
	half _ColorVariation;
	half _CoreHotness;
	half _Softness;
	half _AspectRatio;
	half _TwinkleSpeed;
	half _TwinkleStrength;
	half _PulseSpeed;
	half _PulseAmount;
	half _ShimmerScale;
	half _ShimmerStrength;
	half _FlickerSpeed;
	half _FlickerAmount;
	half _TurbulenceScale;
	half _TurbulenceStrength;
	float4 _TurbulenceSpeed;
	half _WobbleAmplitude;
	half _WobbleFrequency;
	half _SoftParticleDistance;
	half _CameraFadeNear;
	half _CameraFadeFar;
	half _GeometrySoftenDistance;
	half _GeometrySoftenPower;
	half _GeometrySoftenStrength;
	half _BloomContribution;
	half _FogInfluence;
CBUFFER_END

#endif
