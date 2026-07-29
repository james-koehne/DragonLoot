#ifndef PICKABLE_OUTLINE_PASS_INCLUDED
#define PICKABLE_OUTLINE_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
	float4 _OutlineColor;
	float4 _GlowColor;
	float _OutlineWidth;
	float _GlowWidth;
	float _GlowIntensity;
	float _OutlineIntensity;
	float _OutlineDepthBias;
	float _HullExtrudeScale;
	float _FrontFaceFallback;
	float _PulseSpeed;
	float _PulseAmount;
	float3 _ObjectCenterWS;
CBUFFER_END

struct OutlineAttributes
{
	float4 positionOS : POSITION;
	float3 normalOS : NORMAL;
};

struct OutlineVaryings
{
	float4 positionCS : SV_POSITION;
};

float2 SafeNormalize2(float2 v)
{
	float len = length(v);
	return len > 1e-5 ? v / len : float2(0.0, 0.0);
}

float GetPulseAlpha()
{
	return 1.0 + sin(_Time.y * _PulseSpeed * 6.2831853) * _PulseAmount;
}

float2 ComputeOffsetDir(float4 positionCS, float3 normalWS, bool perpendicular)
{
	float3 normalCS = TransformWorldToHClipDir(normalWS, true);
	float2 normalOffset = normalCS.xy;
	float normalMag = length(normalOffset);

	float4 centerCS = TransformWorldToHClip(_ObjectCenterWS);
	float2 centerOffset = SafeNormalize2(positionCS.xy - centerCS.xy);

	float2 axisOffset = perpendicular
		? float2(-normalCS.y, normalCS.x)
		: normalOffset;

	float axisMag = length(axisOffset);
	float fallbackWeight = saturate((0.05 - normalMag) / 0.05) * _FrontFaceFallback;
	float2 offsetDir = SafeNormalize2(lerp(axisOffset, centerOffset, fallbackWeight));

	if (length(offsetDir) < 1e-5)
		offsetDir = axisMag > 1e-5 ? SafeNormalize2(axisOffset) : centerOffset;

	return offsetDir;
}

float4 ExtrudeClipPosition(float4 positionCS, float3 normalWS, float widthPixels, bool perpendicular)
{
	float clipScale = positionCS.w * 2.0 / _ScreenParams.y;
	float2 offsetDir = ComputeOffsetDir(positionCS, normalWS, perpendicular);
	positionCS.xy += offsetDir * widthPixels * clipScale;
	positionCS.z -= _OutlineDepthBias * positionCS.w;
	return positionCS;
}

OutlineVaryings OutlineVert(OutlineAttributes input, float widthPixels, bool perpendicular)
{
	OutlineVaryings output;
	VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
	VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

	float3 positionWS = posInputs.positionWS;
	if (_HullExtrudeScale > 0.0)
		positionWS += normalInputs.normalWS * _HullExtrudeScale;

	float4 positionCS = TransformWorldToHClip(positionWS);
	output.positionCS = ExtrudeClipPosition(positionCS, normalInputs.normalWS, widthPixels, perpendicular);
	return output;
}

half4 OutlineCoreFrag(OutlineVaryings input)
{
	float pulse = GetPulseAlpha();
	float alpha = saturate(_OutlineColor.a * _OutlineIntensity * pulse);
	return half4(_OutlineColor.rgb, alpha);
}

half4 OutlineGlowFrag(OutlineVaryings input)
{
	float pulse = GetPulseAlpha();
	float3 color = _GlowColor.rgb * _GlowIntensity * pulse;
	return half4(color, 1.0);
}

#endif
