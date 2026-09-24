#ifndef DRAGONLOOT_SOFT_SHAFT_PASS_INCLUDED
#define DRAGONLOOT_SOFT_SHAFT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half _Exposure;
	half _Alpha;
	half _HeightMin;
	half _HeightMax;
	half _HeightPower;
	half _RadialRadius;
	half _RadialSoftness;
	half _RadialPower;
	half _FresnelPower;
	half _FresnelStrength;
	half _CameraFadeStart;
	half _CameraFadeEnd;
	half _NoiseStrength;
	half _NoiseScrollSpeed;
	half _NoiseTiling;
	half _PulseSpeed;
	half _PulseAmount;
	half _DepthFadeDistance;
	half _FogInfluence;
CBUFFER_END

struct SoftShaftAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS : NORMAL;
	float4 color : COLOR;
	float2 uv : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SoftShaftVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float3 positionOS : TEXCOORD1;
	half3 normalWS : TEXCOORD2;
	half fogFactor : TEXCOORD3;
	float2 uv : TEXCOORD4;
	half4 color : TEXCOORD5;
	float4 screenPos : TEXCOORD6;
	UNITY_VERTEX_OUTPUT_STEREO
};

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

float SoftShaftHash(float2 p)
{
	return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float SoftShaftValueNoise(float2 p)
{
	float2 i = floor(p);
	float2 f = frac(p);
	float a = SoftShaftHash(i);
	float b = SoftShaftHash(i + float2(1.0, 0.0));
	float c = SoftShaftHash(i + float2(0.0, 1.0));
	float d = SoftShaftHash(i + float2(1.0, 1.0));
	float2 u = f * f * (3.0 - 2.0 * f);
	return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

SoftShaftVaryings SoftShaftVert(SoftShaftAttributes input)
{
	SoftShaftVaryings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
	VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

	output.positionCS = positionInputs.positionCS;
	output.positionWS = positionInputs.positionWS;
	output.positionOS = input.positionOS.xyz;
	output.normalWS = normalInputs.normalWS;
	output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
	output.uv = input.uv;
	output.color = input.color;
	output.screenPos = ComputeScreenPos(positionInputs.positionCS);
	return output;
}

half SoftShaftDepthFade(float4 screenPos)
{
#if defined(_DEPTH_FADE)
	float2 uv = screenPos.xy / max(screenPos.w, 1e-5);
	float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
	float thisDepth = LinearEyeDepth(screenPos.w, _ZBufferParams);
	float fade = saturate((sceneDepth - thisDepth) / max(_DepthFadeDistance, 1e-4));
	return (half)fade;
#else
	return 1.0h;
#endif
}

half4 SoftShaftFrag(SoftShaftVaryings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	half heightMask;
	half radialMask;
	half meshTint = 1.0h;
	half meshAlpha = 1.0h;

#if defined(_MESH_MASK)
	// Baked vertex alpha owns height/edge/layer fades; UV.y remains available for noise.
	heightMask = 1.0h;
	radialMask = 1.0h;
	meshTint = max(input.color.r, max(input.color.g, input.color.b));
	meshAlpha = input.color.a;
#else
	float heightRange = max(_HeightMax - _HeightMin, 1e-4);
	half heightT = (half)saturate((input.positionOS.y - _HeightMin) / heightRange);
	heightMask = pow(1.0h - heightT, max(_HeightPower, 0.01h));

	float r = length(input.positionOS.xz);
	half radialT = (half)saturate(r / max(_RadialRadius, 1e-4));
	half soft = saturate(_RadialSoftness);
	half radialEdge = 1.0h - soft;
	radialMask = 1.0h - smoothstep(radialEdge, 1.0h, radialT);
	radialMask = pow(saturate(radialMask), max(_RadialPower, 0.01h));
#endif

	half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
	half3 normalWS = normalize(input.normalWS);
	half ndv = saturate(dot(normalWS, viewDirWS));
	half fresnel = lerp(1.0h, pow(ndv, max(_FresnelPower, 0.01h)), saturate(_FresnelStrength));

	half noiseMask = 1.0h;
#if defined(_NOISE_STREAKS)
	float n = SoftShaftValueNoise(float2(input.uv.x * max(_NoiseTiling, 0.01), input.uv.y * max(_NoiseTiling, 0.01) * 0.35 - _Time.y * _NoiseScrollSpeed));
	noiseMask = lerp(1.0h, (half)saturate(n), saturate(_NoiseStrength));
#endif

	half pulse = 1.0h;
#if defined(_PULSE)
	pulse = 1.0h - saturate(_PulseAmount) * (0.5h + 0.5h * (half)sin(_Time.y * max(_PulseSpeed, 0.0) * 6.2831853));
#endif

	half depthFade = SoftShaftDepthFade(input.screenPos);

	half falloff = heightMask * radialMask * fresnel * meshAlpha * noiseMask * pulse * depthFade;
	half alpha = falloff * _Alpha;
	alpha = ApplyCameraFade(alpha, input.positionWS);

	if (alpha <= 0.0001h)
		discard;

	half3 baseColor = _Color.rgb * meshTint;
	half3 color = baseColor * _Exposure * alpha;
	half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
	half3 fogged = MixFog(color, fogCoord);
	color = lerp(color, fogged, _FogInfluence);

	return half4(color, alpha);
}

#endif
