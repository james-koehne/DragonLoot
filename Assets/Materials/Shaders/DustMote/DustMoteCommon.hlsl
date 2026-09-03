#ifndef DRAGONLOOT_DUST_MOTE_COMMON_INCLUDED
#define DRAGONLOOT_DUST_MOTE_COMMON_INCLUDED

#include "DustMoteInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

half DustMoteHash31(float3 p)
{
	p = frac(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return frac((p.x + p.x) * p.y);
}

half DustMoteValueNoise3D(float3 p)
{
	float3 i = floor(p);
	float3 f = frac(p);
	f = f * f * (3.0 - 2.0 * f);

	half n000 = DustMoteHash31(i + float3(0.0, 0.0, 0.0));
	half n100 = DustMoteHash31(i + float3(1.0, 0.0, 0.0));
	half n010 = DustMoteHash31(i + float3(0.0, 1.0, 0.0));
	half n110 = DustMoteHash31(i + float3(1.0, 1.0, 0.0));
	half n001 = DustMoteHash31(i + float3(0.0, 0.0, 1.0));
	half n101 = DustMoteHash31(i + float3(1.0, 0.0, 1.0));
	half n011 = DustMoteHash31(i + float3(0.0, 1.0, 1.0));
	half n111 = DustMoteHash31(i + float3(1.0, 1.0, 1.0));

	half n00 = lerp(n000, n100, f.x);
	half n10 = lerp(n010, n110, f.x);
	half n01 = lerp(n001, n101, f.x);
	half n11 = lerp(n011, n111, f.x);
	half n0 = lerp(n00, n10, f.y);
	half n1 = lerp(n01, n11, f.y);
	return lerp(n0, n1, f.z);
}

half DustMoteFbm3D(float3 p)
{
	half sum = 0.0h;
	half amp = 0.5h;
	half freq = 1.0h;

#if defined(_NOISEOCTAVES_OCTAVES2)
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq);
#elif defined(_NOISEOCTAVES_OCTAVES4)
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq);
#else
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * DustMoteValueNoise3D(p * freq);
#endif

	return sum;
}

half DustMoteParticleSeed(half particleAlpha, float3 positionWS)
{
	return frac(particleAlpha * 73.17h + DustMoteHash31(positionWS * 1.37) * 0.5h + 0.13h);
}

half3 DustMoteApplyColorVariation(half3 baseColor, half seed)
{
	half variation = (seed - 0.5h) * _ColorVariation;
	half3 warmShift = half3(_WarmBias, _WarmBias * 0.35h, -_WarmBias * 0.25h);
	return saturate(baseColor + warmShift * variation);
}

half2 DustMoteAspectUV(half2 uv, half aspectRatio)
{
	half safeAspect = max(aspectRatio, 0.25h);
	half2 centered = uv - 0.5h;
	centered.x *= safeAspect;
	return centered + 0.5h;
}

half DustMoteSampleShape(half2 uv)
{
#if defined(_DUSTMOTE_USE_TEX)
	half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
	return max(texSample.a, max(texSample.r, max(texSample.g, texSample.b)));
#else
	half2 fromCenter = uv - 0.5h;
	half dist = length(fromCenter) * 2.0h;
	half spot = saturate(1.0h - dist);
	return pow(spot, max(_Softness, 0.5h));
#endif
}

half DustMoteEvaluateAnimation(half seed, float3 positionWS, float time)
{
	half anim = 1.0h;

	if (_PulseAmount > 0.0001h)
	{
		half pulsePhase = time * _PulseSpeed + seed * 6.28318h;
		anim *= 1.0h + sin(pulsePhase) * _PulseAmount;
	}

	if (_FlickerAmount > 0.0001h)
	{
		half flicker = DustMoteValueNoise3D(float3(time * _FlickerSpeed, seed * 17.0h, 0.0h));
		anim *= lerp(1.0h - _FlickerAmount, 1.0h, flicker);
	}

#if defined(_DUSTMOTE_TWINKLE)
	if (_TwinkleStrength > 0.0001h)
	{
		half twinklePhase = time * _TwinkleSpeed + seed * 12.9898h;
		half twinkle = 0.5h + 0.5h * sin(twinklePhase);
		twinkle = pow(twinkle, 2.0h);
		anim *= lerp(1.0h, twinkle * 1.5h + 0.5h, _TwinkleStrength);
	}
#endif

	if (_ShimmerStrength > 0.0001h)
	{
		float3 shimmerPos = positionWS * _ShimmerScale + float3(time * 0.35h, seed * 4.0h, 0.0h);
		half shimmer = DustMoteFbm3D(shimmerPos);
		anim *= lerp(1.0h, shimmer * 1.25h + 0.25h, _ShimmerStrength);
	}

	return max(anim, 0.0h);
}

half3 DustMoteApplyCoreHotness(half3 color, half2 uv, half shape, half seed)
{
	half2 fromCenter = uv - 0.5h;
	half coreMask = saturate(1.0h - length(fromCenter) * 3.5h) * shape;
	half peak = max(max(_Color.r, _Color.g), _Color.b);
	half3 whiteHot = half3(1.25h, 1.18h, 1.05h) * peak;
	half coreMix = saturate(_CoreHotness) * coreMask;
	return lerp(color, whiteHot, coreMix);
}

bool DustMoteHasValidSceneDepthTexture()
{
	return _CameraDepthTexture_TexelSize.x > 0.0 && _CameraDepthTexture_TexelSize.x < 0.5;
}

half DustMoteApplySoftParticles(half alpha, float3 positionWS, float4 positionCS)
{
#if defined(_DUSTMOTE_SOFT_PARTICLES)
	if (_SoftParticleDistance <= 0.0001h)
		return alpha;

	if (!DustMoteHasValidSceneDepthTexture())
		return alpha;

	float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
	float sceneRawDepth = SampleSceneDepth(normalizedScreenSpaceUV);
	float particleEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
	float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
	float depthDelta = sceneEyeDepth - particleEyeDepth;
	half fade = saturate(depthDelta / _SoftParticleDistance);
	return alpha * fade;
#else
	return alpha;
#endif
}

half DustMoteApplyGeometrySoften(half alpha, float3 positionWS, float4 positionCS)
{
#if defined(_DUSTMOTE_GEOMETRY_SOFTEN)
	if (_GeometrySoftenDistance <= 0.0001h || _GeometrySoftenStrength <= 0.0001h)
		return alpha;

	if (!DustMoteHasValidSceneDepthTexture())
		return alpha;

	float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
	float sceneRawDepth = SampleSceneDepth(normalizedScreenSpaceUV);
	float particleEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
	float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

	float sceneInFront = particleEyeDepth - sceneEyeDepth;
	if (sceneInFront <= 0.0)
		return alpha;

	half depthFade = 1.0h - saturate(sceneInFront / _GeometrySoftenDistance);
	depthFade = pow(max(depthFade, 0.0h), _GeometrySoftenPower);
	depthFade = lerp(1.0h, depthFade, _GeometrySoftenStrength);
	return alpha * depthFade;
#else
	return alpha;
#endif
}

half DustMoteApplyCameraFade(half alpha, float3 positionWS)
{
	float cameraDistance = distance(GetCurrentViewPosition(), positionWS);
	half fade = 1.0h;

	if (_CameraFadeNear > 0.0001h)
		fade *= saturate(cameraDistance / _CameraFadeNear);

	if (_CameraFadeFar > 0.0001h)
		fade *= 1.0h - saturate((cameraDistance - _CameraFadeFar) / max(_CameraFadeFar * 0.35h, 1e-4h));

	return alpha * fade;
}

float3 DustMoteComputeWobble(float3 positionWS, half seed, float time)
{
#if defined(_DUSTMOTE_TURBULENCE)
	float3 noisePos = positionWS * _TurbulenceScale + _TurbulenceSpeed.xyz * time;
	noisePos += float3(seed * 11.0h, seed * 7.0h, seed * 5.0h);
	float3 turbulence = DustMoteFbm3D(noisePos).xxx - 0.5h;
	float wobblePhase = time * _WobbleFrequency + seed * 6.28318h;
	float3 wobble = float3(sin(wobblePhase), cos(wobblePhase * 1.37h), sin(wobblePhase * 0.91h));
	return (turbulence * _TurbulenceStrength + wobble * 0.35h) * _WobbleAmplitude;
#else
	return 0.0;
#endif
}

#endif
