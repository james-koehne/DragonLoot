#ifndef DRAGONLOOT_FLOOR_GLEAM_PASS_INCLUDED
#define DRAGONLOOT_FLOOR_GLEAM_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _Color;
	half _Exposure;
	half _Alpha;
	half _GleamT;
	half _ScreenAngle;
	half _BandWidth;
	half _BandSoftness;
	half _EdgeFade;
	half _LineCount;
	half _LineSpacing;
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
	half fogFactor : TEXCOORD0;
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
	output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
	return output;
}

half FloorGleamBand(half t01, half bandCenter, half halfWidth, half soft)
{
	half dist = abs(t01 - bandCenter);
	return 1.0h - smoothstep(halfWidth, halfWidth + soft, dist);
}

// Full-viewport UV, top-left origin. Mesh is the draw mask only — no footprint remap.
float2 FloorGleamScreenUV(float4 positionCS)
{
	float2 viewport = GetNormalizedScreenSpaceUV(positionCS);
	// GetNormalizedScreenSpaceUV: (0,0) = bottom-left. Flip Y so (0,0) = top-left.
	return float2(viewport.x, 1.0 - viewport.y);
}

half4 FloorGleamFrag(FloorGleamVaryings input) : SV_Target
{
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	float2 uv = FloorGleamScreenUV(input.positionCS);

	// 45° with TL origin → axis (1,1) → top-left toward bottom-right of the viewport.
	float angleRad = radians((float)_ScreenAngle);
	float2 axis = float2(cos(angleRad), sin(angleRad));
	float span = max(abs(axis.x) + abs(axis.y), 0.01);
	float t01 = saturate(dot(uv, axis) / span);

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
	band *= edge;

	half intensity = band * (half)_Exposure * (half)_Alpha;
	half3 rgb = _Color.rgb * intensity;
	half alpha = saturate(intensity * _Color.a);

	half4 col = half4(rgb, alpha);
	col.rgb = MixFog(col.rgb, input.fogFactor * (half)_FogInfluence);
	return col;
}

#endif
