#ifndef DRAGONLOOT_SOFT_VOLUME_CORE_INCLUDED
#define DRAGONLOOT_SOFT_VOLUME_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half _Exposure;
	half _Density;
	half _Absorption;
	half _Alpha;
	half _NoiseScale;
	half _NoiseStrength;
	half _NoiseContrast;
	float4 _NoiseSpeed;
	float4 _GlobalScroll;
	half _TimeScale;
	half _StepSize;
	half _HeightMin;
	half _HeightMax;
	half _HeightSoftness;
	float4 _RadialCenter;
	half _RadialRadius;
	half _RadialSoftness;
	half _GeometrySoftenDistance;
	half _GeometrySoftenPower;
	half _GeometrySoftenStrength;
	half _CameraFadeStart;
	half _CameraFadeEnd;
	half _FogInfluence;
CBUFFER_END

struct SoftVolumeAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SoftVolumeVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float3 positionOS : TEXCOORD1;
	half fogFactor : TEXCOORD2;
	UNITY_VERTEX_OUTPUT_STEREO
};

SoftVolumeVaryings SoftVolumeVert(SoftVolumeAttributes input)
{
	SoftVolumeVaryings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
	output.positionCS = positionInputs.positionCS;
	output.positionWS = positionInputs.positionWS;
	output.positionOS = input.positionOS.xyz;
	output.fogFactor = 0.0h;
	return output;
}

half Hash31(float3 p)
{
	p = frac(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return frac((p.x + p.x) * p.y);
}

half ValueNoise3D(float3 p)
{
	float3 i = floor(p);
	float3 f = frac(p);
	f = f * f * (3.0 - 2.0 * f);

	half n000 = Hash31(i + float3(0.0, 0.0, 0.0));
	half n100 = Hash31(i + float3(1.0, 0.0, 0.0));
	half n010 = Hash31(i + float3(0.0, 1.0, 0.0));
	half n110 = Hash31(i + float3(1.0, 1.0, 0.0));
	half n001 = Hash31(i + float3(0.0, 0.0, 1.0));
	half n101 = Hash31(i + float3(1.0, 0.0, 1.0));
	half n011 = Hash31(i + float3(0.0, 1.0, 1.0));
	half n111 = Hash31(i + float3(1.0, 1.0, 1.0));

	half n00 = lerp(n000, n100, f.x);
	half n10 = lerp(n010, n110, f.x);
	half n01 = lerp(n001, n101, f.x);
	half n11 = lerp(n011, n111, f.x);
	half n0 = lerp(n00, n10, f.y);
	half n1 = lerp(n01, n11, f.y);
	return lerp(n0, n1, f.z);
}

half Fbm3D(float3 p)
{
	half sum = 0.0h;
	half amp = 0.5h;
	half freq = 1.0h;

#if defined(_NOISEOCTAVES_OCTAVES2)
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq);
#elif defined(_NOISEOCTAVES_OCTAVES4)
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq);
#else
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq); freq *= 2.0h; amp *= 0.5h;
	sum += amp * ValueNoise3D(p * freq);
#endif

	return sum;
}

half ApplyContrast(half value, half contrast)
{
	half mid = 0.5h;
	return saturate((value - mid) * contrast + mid);
}

float InterleavedGradientNoise(float2 screenPos, float frameId)
{
	float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
	return frac(magic.z * frac(dot(screenPos + frameId, magic.xy)));
}

half SampleShapeMasks(float3 positionOS, float3 positionWS)
{
	half mask = 1.0h;

#if defined(_SOFTVOLUME_HEIGHT)
	half heightCoord;
#if defined(_HEIGHTSPACE_OBJECT)
	heightCoord = positionOS.y;
#else
	heightCoord = positionWS.y;
#endif
	half heightRange = max(_HeightMax - _HeightMin, 1e-4h);
	half heightT = saturate((heightCoord - _HeightMin) / heightRange);
	half soft = max(_HeightSoftness, 1e-3h);
	half invT = 1.0h - heightT;
	half heightMask = 1.0h - smoothstep(1.0h - soft, 1.0h, invT);
	heightMask *= smoothstep(0.0h, soft, invT + soft);
	mask *= heightMask;
#endif

#if defined(_SOFTVOLUME_RADIAL)
	float3 centerWS = TransformObjectToWorld(_RadialCenter.xyz);
#if defined(_RADIALMODE_CYLINDERXZ)
	float3 radialOffsetOS = positionOS - _RadialCenter.xyz;
	half radialDist = length(radialOffsetOS.xz);
#else
	half radialDist = distance(positionWS, centerWS);
#endif
	half radialT = 1.0h - saturate(radialDist / max(_RadialRadius, 1e-4h));
	half fadeBand = max(saturate(_RadialSoftness), 1e-3h);
	half radialMask = smoothstep(0.0h, fadeBand, radialT);
	mask *= radialMask;
#endif

	return mask;
}

half SampleFogDensity(float3 positionWS, float3 positionOS, float time)
{
	float3 wind = (_NoiseSpeed.xyz + _GlobalScroll.xyz) * time;
	float3 noisePos = positionWS * _NoiseScale + wind;
	half noise = ApplyContrast(Fbm3D(noisePos), _NoiseContrast);
	half noiseFactor = lerp(1.0h, noise, _NoiseStrength);
	half shapeMask = SampleShapeMasks(positionOS, positionWS);
	return max(_Density * noiseFactor * shapeMask, 0.0h);
}

bool HasValidSceneDepthTexture()
{
	return _CameraDepthTexture_TexelSize.x > 0.0 && _CameraDepthTexture_TexelSize.x < 0.5;
}

half ApplyGeometrySoften(half alpha, float3 positionWS, float4 positionCS)
{
	if (_GeometrySoftenDistance <= 0.0001h || _GeometrySoftenStrength <= 0.0001h)
		return alpha;

	if (!HasValidSceneDepthTexture())
		return alpha;

	float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
	float sceneRawDepth = SampleSceneDepth(normalizedScreenSpaceUV);
	float volumeEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
	float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

	float sceneInFront = volumeEyeDepth - sceneEyeDepth;
	if (sceneInFront <= 0.0)
		return alpha;

	half depthFade = 1.0h - saturate(sceneInFront / _GeometrySoftenDistance);
	depthFade = pow(max(depthFade, 0.0h), _GeometrySoftenPower);
	depthFade = lerp(1.0h, depthFade, _GeometrySoftenStrength);
	return alpha * depthFade;
}

half ApplyCameraFade(half alpha, float3 positionWS)
{
	if (_CameraFadeEnd <= _CameraFadeStart + 0.0001h)
		return alpha;

	float cameraDistance = distance(GetCurrentViewPosition(), positionWS);
	if (cameraDistance <= _CameraFadeStart)
		return alpha;

	half fade = saturate((cameraDistance - _CameraFadeStart) / (_CameraFadeEnd - _CameraFadeStart));
	return alpha * fade;
}

void IntegrateFog(
	float3 surfaceWS,
	float3 surfaceOS,
	half3 viewDirWS,
	float4 positionCS,
	float time,
	out half3 outColor,
	out half outAlpha)
{
	half3 fogColor = _Color.rgb * _Exposure;
	half transmittance = 1.0h;
	half3 accumColor = 0.0h;
	half dither = InterleavedGradientNoise(positionCS.xy, 0.0);

#if defined(_MARCHSTEPS_STEPS2)
	const int stepCount = 2;
#elif defined(_MARCHSTEPS_STEPS8)
	const int stepCount = 8;
#else
	const int stepCount = 4;
#endif

	UNITY_LOOP
	for (int i = 0; i < stepCount; i++)
	{
		half stepT = (i + dither) / stepCount;
		float3 sampleWS = surfaceWS - viewDirWS * (_StepSize * stepT * stepCount);
		float3 sampleOS = surfaceOS + (TransformWorldToObject(sampleWS) - TransformWorldToObject(surfaceWS));
		half sigma = SampleFogDensity(sampleWS, sampleOS, time) * _Absorption;
		half stepAlpha = 1.0h - exp(-sigma * _StepSize);
		accumColor += fogColor * stepAlpha * transmittance;
		transmittance *= 1.0h - stepAlpha;
	}

	outAlpha = saturate((1.0h - transmittance) * _Alpha);
	outColor = accumColor;
}

#endif
