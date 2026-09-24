// Soft Shaft — cheap object-space upward glow for display / pedestal bases.
Shader "DragonLoot/SoftShaft"
{
	Properties
	{
		[Header(Glow)]
		[HDR] _Color("Color", Color) = (1.0, 0.75, 0.35, 1)
		_Exposure("Exposure", Range(0, 8)) = 1.5
		_Alpha("Alpha", Range(0, 2)) = 0.85

		[Header(Height Falloff Object Space)]
		_HeightMin("Height Min", Float) = 0
		_HeightMax("Height Max", Float) = 1.5
		_HeightPower("Height Power", Range(0.1, 8)) = 1.5

		[Header(Radial Falloff Object XZ)]
		_RadialRadius("Radial Radius", Float) = 0.5
		_RadialSoftness("Radial Softness", Range(0.01, 1)) = 0.55
		_RadialPower("Radial Power", Range(0.1, 8)) = 1.25

		[Header(View Soften)]
		_FresnelPower("Fresnel Power", Range(0.1, 8)) = 1.5
		_FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.45

		[Header(Camera Fade)]
		_CameraFadeStart("Camera Fade Start", Float) = 0.35
		_CameraFadeEnd("Camera Fade End", Float) = 1.25

		[Header(Mesh Mask Features)]
		[Toggle(_MESH_MASK)] _MeshMask("Use Mesh Mask (UV/Vertex Color)", Float) = 0
		[Toggle(_NOISE_STREAKS)] _NoiseStreaks("Noise Streaks", Float) = 0
		_NoiseStrength("Noise Strength", Range(0, 1)) = 0.35
		_NoiseScrollSpeed("Noise Scroll Speed", Float) = 0.15
		_NoiseTiling("Noise Tiling", Float) = 4
		[Toggle(_PULSE)] _Pulse("Pulse", Float) = 0
		_PulseSpeed("Pulse Speed", Float) = 0.4
		_PulseAmount("Pulse Amount", Range(0, 1)) = 0.15
		[Toggle(_DEPTH_FADE)] _DepthFade("Depth Fade", Float) = 0
		_DepthFadeDistance("Depth Fade Distance", Float) = 0.25

		[Header(Render)]
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 0
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.5
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
			Name "SoftShaft"
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
			#pragma shader_feature_local _MESH_MASK
			#pragma shader_feature_local _NOISE_STREAKS
			#pragma shader_feature_local _PULSE
			#pragma shader_feature_local _DEPTH_FADE

			#include "DragonLoot_SoftShaftPass.hlsl"

			SoftShaftVaryings Vert(SoftShaftAttributes input)
			{
				return SoftShaftVert(input);
			}

			half4 Frag(SoftShaftVaryings input) : SV_Target
			{
				return SoftShaftFrag(input);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
