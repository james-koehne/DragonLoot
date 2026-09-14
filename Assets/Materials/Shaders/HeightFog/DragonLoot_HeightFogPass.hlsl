#ifndef DRAGONLOOT_HEIGHT_FOG_PASS_INCLUDED
#define DRAGONLOOT_HEIGHT_FOG_PASS_INCLUDED

#include "DragonLoot_HeightFogCore.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

HeightFogVaryings HeightFogVertFog(HeightFogAttributes input)
{
	HeightFogVaryings output = HeightFogVert(input);
	VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
	output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
	return output;
}

half4 HeightFogFrag(HeightFogVaryings input)
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	half3 color;
	half alpha;
	EvaluateHeightFog(input.positionWS, input.positionCS, input.uv, color, alpha);

	if (alpha <= 0.0001h)
		discard;

	half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
	half3 fogged = MixFog(color, fogCoord);
	color = lerp(color, fogged, _FogInfluence);

	return half4(color, alpha);
}

#endif
