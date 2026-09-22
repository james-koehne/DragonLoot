#ifndef DRAGONLOOT_SOFT_SHAFT_PASS_INCLUDED
#define DRAGONLOOT_SOFT_SHAFT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

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
	half _FogInfluence;
CBUFFER_END

struct SoftShaftAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SoftShaftVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float3 positionOS : TEXCOORD1;
	half3 normalWS : TEXCOORD2;
	half fogFactor : TEXCOORD3;
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
	return output;
}

half4 SoftShaftFrag(SoftShaftVaryings input)
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	// Height: _Alpha at HeightMin, 0 at HeightMax.
	float heightRange = max(_HeightMax - _HeightMin, 1e-4);
	half heightT = (half)saturate((input.positionOS.y - _HeightMin) / heightRange);
	half heightMask = pow(1.0h - heightT, max(_HeightPower, 0.01h));

	// Radial: full at OS origin, 0 at RadialRadius (softness controls fade band).
	float r = length(input.positionOS.xz);
	half radialT = (half)saturate(r / max(_RadialRadius, 1e-4));
	half soft = saturate(_RadialSoftness);
	half radialEdge = 1.0h - soft;
	half radialMask = 1.0h - smoothstep(radialEdge, 1.0h, radialT);
	radialMask = pow(saturate(radialMask), max(_RadialPower, 0.01h));

	half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
	half3 normalWS = normalize(input.normalWS);
	half ndv = saturate(dot(normalWS, viewDirWS));
	half fresnel = lerp(1.0h, pow(ndv, max(_FresnelPower, 0.01h)), saturate(_FresnelStrength));

	half falloff = heightMask * radialMask * fresnel;
	half alpha = falloff * _Alpha;
	alpha = ApplyCameraFade(alpha, input.positionWS);

	if (alpha <= 0.0001h)
		discard;

	// Premultiply so falloffs work for both alpha-blend and additive (One One).
	half3 color = _Color.rgb * _Exposure * alpha;
	half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
	half3 fogged = MixFog(color, fogCoord);
	color = lerp(color, fogged, _FogInfluence);

	return half4(color, alpha);
}

#endif
