// Height Fog — plane fog sheet. Samples scene depth behind the plane and integrates
// exponential height fog along the view ray so deep ravines read as an opaque abyss.
Shader "DragonLoot/HeightFog"
{
	Properties
	{
		[Header(Fog)]
		[HDR] _Color("Rim Color", Color) = (0.12, 0.045, 0.02, 1)
		[HDR] _DeepColor("Deep Color", Color) = (0.015, 0.01, 0.02, 1)
		_Exposure("Exposure", Range(0, 8)) = 1
		_Density("Density", Range(0, 2)) = 0.08
		_HeightFalloff("Height Falloff", Range(0, 4)) = 0.35
		_FogHeight("Fog Height", Float) = 0
		_HeightSoftness("Height Softness", Range(0.01, 20)) = 4
		_Alpha("Alpha Scale", Range(0, 2)) = 1

		[Header(Height Space)]
		[KeywordEnum(World, Object)] _HeightSpace("Height Space", Float) = 0

		[Header(Sheet)]
		_EdgeFade("Edge Fade (UV)", Range(0.001, 0.5)) = 0.08
		_GeometrySoftenDistance("Depth Soften Distance", Float) = 1.5
		_GeometrySoftenPower("Depth Soften Power", Range(0.25, 8)) = 1
		_GeometrySoftenStrength("Depth Soften Strength", Range(0, 1)) = 1
		_CameraFadeNear("Camera Fade Near", Float) = 0.35

		[Header(Noise)]
		[Toggle(_HEIGHTFOG_NOISE)] _NoiseEnabled("Enable Surface Noise", Float) = 1
		_NoiseScale("Noise Scale", Range(0.001, 2)) = 0.08
		_NoiseStrength("Noise Strength", Range(0, 1)) = 0.35
		_NoiseSpeed("Noise Speed XZ", Vector) = (0.015, 0.01, 0, 0)

		[Header(Render)]
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 0
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.55
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "HeightFog"
			Tags { "LightMode" = "UniversalForward" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite Off
			ZTest LEqual
			Cull [_Cull]

			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma multi_compile_fog
			#pragma shader_feature_local _HEIGHTSPACE_WORLD _HEIGHTSPACE_OBJECT
			#pragma shader_feature_local _HEIGHTFOG_NOISE

			#include "DragonLoot_HeightFogPass.hlsl"

			HeightFogVaryings Vert(HeightFogAttributes input)
			{
				return HeightFogVertFog(input);
			}

			half4 Frag(HeightFogVaryings input) : SV_Target
			{
				return HeightFogFrag(input);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
