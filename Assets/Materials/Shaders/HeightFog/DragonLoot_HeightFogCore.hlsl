#ifndef DRAGONLOOT_HEIGHT_FOG_CORE_INCLUDED
#define DRAGONLOOT_HEIGHT_FOG_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half4 _DeepColor;
	half _Exposure;
	half _Density;
	half _HeightFalloff;
	half _FogHeight;
	half _HeightSoftness;
	half _EdgeFade;
	half _GeometrySoftenDistance;
	half _GeometrySoftenPower;
	half _GeometrySoftenStrength;
	half _CameraFadeNear;
	half _NoiseScale;
	half _NoiseStrength;
	float4 _NoiseSpeed;
	half _FogInfluence;
	half _Alpha;
CBUFFER_END

struct HeightFogAttributes
{
	float4 positionOS : POSITION;
	float2 uv : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct HeightFogVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float2 uv : TEXCOORD1;
	half fogFactor : TEXCOORD2;
	UNITY_VERTEX_OUTPUT_STEREO
};

HeightFogVaryings HeightFogVert(HeightFogAttributes input)
{
	HeightFogVaryings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
	output.positionCS = positionInputs.positionCS;
	output.positionWS = positionInputs.positionWS;
	output.uv = input.uv;
	output.fogFactor = 0.0h;
	return output;
}

half SampleHeightCoord(float3 positionWS)
{
#if defined(_HEIGHTSPACE_OBJECT)
	return TransformWorldToObject(positionWS).y;
#else
	return positionWS.y;
#endif
}

half SampleEdgeFadeUV(float2 uv)
{
	half fade = max(_EdgeFade, 1e-4h);
	half2 toEdge = min(uv, 1.0h - uv);
	half edge = min(toEdge.x, toEdge.y);
	return smoothstep(0.0h, fade, edge);
}

half SampleTopSoftness(half heightCoord)
{
	half soft = max(_HeightSoftness, 1e-4h);
	return 1.0h - smoothstep(_FogHeight - soft, _FogHeight + soft * 0.25h, heightCoord);
}

half Hash21(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.x + p3.y) * p3.z);
}

half ValueNoise2D(float2 p)
{
	float2 i = floor(p);
	float2 f = frac(p);
	f = f * f * (3.0 - 2.0 * f);

	half n00 = Hash21(i + float2(0.0, 0.0));
	half n10 = Hash21(i + float2(1.0, 0.0));
	half n01 = Hash21(i + float2(0.0, 1.0));
	half n11 = Hash21(i + float2(1.0, 1.0));

	half n0 = lerp(n00, n10, f.x);
	half n1 = lerp(n01, n11, f.x);
	return lerp(n0, n1, f.y);
}

half SampleSurfaceNoise(float3 positionWS, float time)
{
#if defined(_HEIGHTFOG_NOISE)
	float2 wind = _NoiseSpeed.xy * time;
	float2 noisePos = positionWS.xz * _NoiseScale + wind;
	half noise = ValueNoise2D(noisePos);
	noise = lerp(ValueNoise2D(noisePos * 2.17), noise, 0.65h);
	return lerp(1.0h, noise, _NoiseStrength);
#else
	return 1.0h;
#endif
}

// Optical depth of density = Density * exp(-Falloff * (y - FogHeight)) along a straight ray.
half IntegrateExponentialHeightFog(float3 rayOriginWS, float3 rayDirWS, float tNear, float tFar)
{
	half y0 = SampleHeightCoord(rayOriginWS);
	half dy = SampleHeightCoord(rayOriginWS + rayDirWS) - y0;

	half falloff = max(_HeightFalloff, 0.0h);
	half dens = max(_Density, 0.0h);
	half k = falloff * dy;
	half segment = max(tFar - tNear, 0.0h);

	half optical;
	if (abs(k) < 1e-4h)
		optical = dens * exp(-falloff * (y0 - _FogHeight)) * segment;
	else
	{
		half base = dens * exp(-falloff * (y0 - _FogHeight));
		optical = base * (exp(-k * tNear) - exp(-k * tFar)) / k;
	}

	half yNear = y0 + dy * tNear;
	half yFar = y0 + dy * tFar;
	half topMask = max(SampleTopSoftness(yNear), SampleTopSoftness(yFar));
	topMask = max(topMask, SampleTopSoftness(0.5h * (yNear + yFar)));

	float3 midWS = rayOriginWS + rayDirWS * ((tNear + tFar) * 0.5);
	half noise = SampleSurfaceNoise(midWS, _Time.y);
	return max(optical * topMask * noise, 0.0h);
}

bool HasValidSceneDepthTexture()
{
	return _CameraDepthTexture_TexelSize.x > 0.0 && _CameraDepthTexture_TexelSize.x < 0.5;
}

half ApplyDepthSoften(half alpha, float planeEyeDepth, float sceneEyeDepth)
{
	float behind = sceneEyeDepth - planeEyeDepth;
	if (behind <= 0.0)
		return 0.0h;

	if (_GeometrySoftenDistance <= 0.0001h || _GeometrySoftenStrength <= 0.0001h)
		return alpha;

	half depthFade = saturate(behind / _GeometrySoftenDistance);
	depthFade = pow(max(depthFade, 0.0h), _GeometrySoftenPower);
	depthFade = lerp(1.0h, depthFade, _GeometrySoftenStrength);
	return alpha * depthFade;
}

half ApplyCameraNearFade(half alpha, float3 planeWS)
{
	if (_CameraFadeNear <= 0.0001h)
		return alpha;

	float cameraDistance = distance(GetCurrentViewPosition(), planeWS);
	return alpha * saturate(cameraDistance / _CameraFadeNear);
}

half3 EvaluateFogColor(float3 rayOriginWS, float3 rayDirWS, float tNear, float tFar, half opticalDepth)
{
	half y0 = SampleHeightCoord(rayOriginWS);
	half dy = SampleHeightCoord(rayOriginWS + rayDirWS) - y0;
	half yLow = min(y0 + dy * tNear, y0 + dy * tFar);

	half depthT = saturate(1.0h - exp(-opticalDepth * 0.65h));
	half softBand = max(_HeightSoftness * 8.0h, 1.0h);
	half heightT = 1.0h - saturate((yLow - (_FogHeight - softBand * 0.5h)) / softBand);
	half abyss = saturate(max(depthT, heightT * depthT));

	half3 rim = _Color.rgb * _Exposure;
	half3 deep = _DeepColor.rgb * _Exposure;
	return lerp(rim, deep, abyss);
}

void EvaluateHeightFog(float3 planeWS, float4 positionCS, float2 uv, out half3 outColor, out half outAlpha)
{
	outColor = 0.0h;
	outAlpha = 0.0h;

	if (!HasValidSceneDepthTexture())
		return;

	half edge = SampleEdgeFadeUV(uv);
	if (edge <= 1e-4h)
		return;

	float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
	float sceneRawDepth = SampleSceneDepth(screenUV);
	float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
	float planeEyeDepth = LinearEyeDepth(planeWS, GetWorldToViewMatrix());

	// Only fog what is behind the plane sheet.
	if (sceneEyeDepth <= planeEyeDepth + 1e-4)
		return;

	float3 sceneWS = ComputeWorldSpacePosition(screenUV, sceneRawDepth, UNITY_MATRIX_I_VP);
	float3 cameraWS = GetCurrentViewPosition();
	float3 rayDirWS = sceneWS - cameraWS;
	float tFar = length(rayDirWS);
	if (tFar <= 1e-4)
		return;
	rayDirWS /= tFar;

	float3 toPlane = planeWS - cameraWS;
	float tNear = dot(toPlane, rayDirWS);
	tNear = max(tNear, 0.0);

	if (tFar <= tNear + 1e-4)
		return;

	half optical = IntegrateExponentialHeightFog(cameraWS, rayDirWS, tNear, tFar);
	optical *= edge;

	half alpha = saturate((1.0h - exp(-optical)) * _Alpha);
	alpha = ApplyDepthSoften(alpha, planeEyeDepth, sceneEyeDepth);
	alpha = ApplyCameraNearFade(alpha, planeWS);

	if (alpha <= 0.0001h)
		return;

	outColor = EvaluateFogColor(cameraWS, rayDirWS, tNear, tFar, optical);
	outAlpha = alpha;
}

#endif
