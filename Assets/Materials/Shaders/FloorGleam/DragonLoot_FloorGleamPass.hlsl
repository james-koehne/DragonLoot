#ifndef DRAGONLOOT_FLOOR_GLEAM_PASS_INCLUDED
#define DRAGONLOOT_FLOOR_GLEAM_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half _Exposure;
	half _Alpha;
	half _AreaGlow;
	half _AreaGlowMul;
	float4 _GleamOrigin;
	float4 _GleamAxis;
	half _GleamSpan;
	half _GleamT;
	half _BandWidth;
	half _BandSoftness;
	half _EdgeFade;
	half _LineCount;
	half _LineSpacing;
	half _BandMul;
	half _FogInfluence;
CBUFFER_END

struct FloorGleamAttributes
{
	float4 positionOS : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FloorGleamVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	half fogFactor : TEXCOORD1;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

FloorGleamVaryings FloorGleamVert(FloorGleamAttributes input)
{
	FloorGleamVaryings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
	output.positionCS = posInputs.positionCS;
	output.positionWS = posInputs.positionWS;
	output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
	return output;
}

half FloorGleamBand(half t01, half bandCenter, half halfWidth, half soft)
{
	half dist = abs(t01 - bandCenter);
	return 1.0h - smoothstep(halfWidth, halfWidth + soft, dist);
}

half FloorGleamSwipe(float3 positionWS)
{
	float3 origin = _GleamOrigin.xyz;
	float3 axis = normalize(_GleamAxis.xyz + float3(1e-5, 0.0, 0.0));
	float span = max((float)_GleamSpan, 0.01);
	float along = dot(positionWS - origin, axis);
	float t01 = saturate(along / span);

	half bandCenter = (half)_GleamT * 1.4h - 0.2h;
	half halfWidth = max((half)_BandWidth * 0.5h, 0.001h);
	half soft = max((half)_BandSoftness, 0.001h);

	int lineCount = (int)clamp(round(_LineCount), 1.0h, 8.0h);
	half spacing = max((half)_LineSpacing, 0.0h);
	half band = 0.0h;
	half mid = (half)(lineCount - 1) * 0.5h;
	for (int i = 0; i < 8; i++)
	{
		if (i >= lineCount)
			break;

		half offset = ((half)i - mid) * spacing;
		band += FloorGleamBand((half)t01, bandCenter + offset, halfWidth, soft);
	}

	band = saturate(band);
	half edge = saturate(min((half)t01, 1.0h - (half)t01) / max((half)_EdgeFade, 0.001h));
	return band * edge;
}

half4 FloorGleamFrag(FloorGleamVaryings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	half area = saturate((half)_AreaGlow) * max((half)_AreaGlowMul, 0.0h);
	half swipe = FloorGleamSwipe(input.positionWS) * max((half)_BandMul, 0.0h);
	half intensity = (area + swipe) * (half)_Exposure * (half)_Alpha;

	half3 rgb = _Color.rgb * intensity;
	half alpha = saturate(intensity * _Color.a);

	half4 col = half4(rgb, alpha);
	col.rgb = MixFog(col.rgb, input.fogFactor * (half)_FogInfluence);
	return col;
}

#endif
