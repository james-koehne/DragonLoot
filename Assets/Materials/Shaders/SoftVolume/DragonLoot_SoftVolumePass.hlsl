#ifndef DRAGONLOOT_SOFT_VOLUME_PASS_INCLUDED
#define DRAGONLOOT_SOFT_VOLUME_PASS_INCLUDED

#include "DragonLoot_SoftVolumeCore.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

SoftVolumeVaryings SoftVolumeVertFog(SoftVolumeAttributes input)
{
	SoftVolumeVaryings output = SoftVolumeVert(input);
	VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
	output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
	return output;
}

half4 SoftVolumeFrag(SoftVolumeVaryings input)
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	float time = _Time.y * _TimeScale;
	half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

	half3 color;
	half alpha;
	IntegrateFog(input.positionWS, input.positionOS, viewDirWS, input.positionCS, time, color, alpha);

	if (alpha <= 0.0001h)
		discard;

#if defined(_SOFTVOLUME_GEOMETRY_SOFTEN)
	alpha = ApplyGeometrySoften(alpha, input.positionWS, input.positionCS);
#endif
	alpha = ApplyCameraFade(alpha, input.positionWS);

	if (alpha <= 0.0001h)
		discard;

	half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
	half3 fogged = MixFog(color, fogCoord);
	color = lerp(color, fogged, _FogInfluence);

	return half4(color, alpha);
}

#endif
