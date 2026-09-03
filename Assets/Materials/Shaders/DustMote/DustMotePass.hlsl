#ifndef DRAGONLOOT_DUST_MOTE_PASS_INCLUDED
#define DRAGONLOOT_DUST_MOTE_PASS_INCLUDED

#include "DustMoteCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

struct DustMoteAttributes
{
	float4 positionOS : POSITION;
	half4 color : COLOR;
	float2 uv : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DustMoteVaryings
{
	float4 positionCS : SV_POSITION;
	float2 uv : TEXCOORD0;
	half4 color : TEXCOORD1;
	float3 positionWS : TEXCOORD2;
	half seed : TEXCOORD3;
	half fogFactor : TEXCOORD4;
	UNITY_VERTEX_OUTPUT_STEREO
};

DustMoteVaryings DustMoteVert(DustMoteAttributes input)
{
	DustMoteVaryings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
	half seed = DustMoteParticleSeed(input.color.a, positionWS);
	float time = _Time.y;

	positionWS += DustMoteComputeWobble(positionWS, seed, time);

	output.positionCS = TransformWorldToHClip(positionWS);
	output.positionWS = positionWS;
	output.uv = input.uv;
	output.color = input.color;
	output.seed = seed;
	output.fogFactor = ComputeFogFactor(output.positionCS.z);
	return output;
}

half4 DustMoteFrag(DustMoteVaryings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	float time = _Time.y;
	half2 uv = DustMoteAspectUV(input.uv, _AspectRatio);
	half shape = DustMoteSampleShape(uv);

	if (shape <= 1e-4h)
		discard;

	half anim = DustMoteEvaluateAnimation(input.seed, input.positionWS, time);
	half3 baseColor = lerp(_Color.rgb, _CoreColor.rgb, shape * 0.65h);
	baseColor = DustMoteApplyColorVariation(baseColor, input.seed);
	baseColor = lerp(baseColor, _TwinkleColor.rgb, saturate(anim - 1.0h) * _TwinkleStrength * 0.35h);
	baseColor = DustMoteApplyCoreHotness(baseColor, uv, shape, input.seed);
	baseColor *= _Exposure * anim * input.color.rgb;

	half alpha = shape * _Color.a * input.color.a * anim;

#if defined(_DUSTMOTE_USE_TEX)
	half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
	baseColor *= texSample.rgb;
#endif

	alpha = DustMoteApplySoftParticles(alpha, input.positionWS, input.positionCS);
	alpha = DustMoteApplyGeometrySoften(alpha, input.positionWS, input.positionCS);
	alpha = DustMoteApplyCameraFade(alpha, input.positionWS);

	if (alpha <= 1e-4h)
		discard;

	half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
	half3 fogged = MixFog(baseColor, fogCoord);
	baseColor = lerp(baseColor, fogged, _FogInfluence);

#if defined(_BLENDMODE_ADDITIVE)
	half3 rgb = baseColor * alpha * max(_BloomContribution, 0.0h);
	rgb = min(rgb, 64.0h);
	return half4(rgb, 0.0h);
#elif defined(_BLENDMODE_PREMULTIPLY)
	half3 rgb = baseColor * alpha * max(_BloomContribution, 1.0h);
	rgb = min(rgb, 64.0h);
	return half4(rgb, alpha);
#else
	half3 rgb = baseColor * max(_BloomContribution, 1.0h);
	rgb = min(rgb, 64.0h);
	return half4(rgb, alpha);
#endif
}

#endif
