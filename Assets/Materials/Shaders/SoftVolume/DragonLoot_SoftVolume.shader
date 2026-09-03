// Soft Volume — cheap procedural volumetric fog on a mesh hull.
Shader "DragonLoot/SoftVolume"
{
	Properties
	{
		[Header(Fog)]
		[HDR] _Color("Fog Color", Color) = (0.55, 0.72, 0.85, 1)
		_Exposure("Exposure", Range(0, 8)) = 1.2
		_Density("Density", Range(0, 4)) = 0.55
		_Absorption("Absorption", Range(0.01, 8)) = 1
		_Alpha("Alpha Scale", Range(0, 2)) = 1

		[Header(Procedural Noise)]
		_NoiseScale("Noise Scale", Range(0.001, 1)) = 0.05
		_NoiseStrength("Noise Strength", Range(0, 1)) = 0.65
		_NoiseContrast("Noise Contrast", Range(0.1, 4)) = 1
		[KeywordEnum(Octaves2, Octaves3, Octaves4)] _NoiseOctaves("Noise Octaves", Float) = 1
		_NoiseSpeed("Noise Speed XYZ", Vector) = (0.02, 0.01, 0.015, 0)
		_GlobalScroll("Global Scroll", Vector) = (0.01, 0.005, 0.008, 0)
		_TimeScale("Time Scale", Float) = 1

		[Header(Raymarch)]
		[KeywordEnum(Steps2, Steps4, Steps8)] _MarchSteps("March Steps", Float) = 1
		_StepSize("Step Size", Range(0.05, 8)) = 1

		[Header(Height Falloff)]
		[Toggle(_SOFTVOLUME_HEIGHT)] _HeightEnabled("Enable Height Falloff", Float) = 1
		[KeywordEnum(World, Object)] _HeightSpace("Height Space", Float) = 0
		_HeightMin("Height Min", Float) = 0
		_HeightMax("Height Max", Float) = 4
		_HeightSoftness("Height Softness", Range(0.01, 2)) = 0.35

		[Header(Radial Falloff)]
		[Toggle(_SOFTVOLUME_RADIAL)] _RadialEnabled("Enable Radial Falloff", Float) = 0
		[KeywordEnum(Sphere, CylinderXZ)] _RadialMode("Radial Mode", Float) = 0
		_RadialCenter("Radial Center OS", Vector) = (0, 0, 0, 0)
		_RadialRadius("Radial Radius", Float) = 2
		_RadialSoftness("Radial Softness", Range(0.01, 1)) = 0.4

		[Header(Geometry Soften)]
		[Toggle(_SOFTVOLUME_GEOMETRY_SOFTEN)] _GeometrySoftenEnabled("Soften Near Geometry", Float) = 1
		_GeometrySoftenDistance("Geometry Soften Distance", Float) = 0.5
		_GeometrySoftenPower("Geometry Soften Power", Range(0.25, 8)) = 1
		_GeometrySoftenStrength("Geometry Soften Strength", Range(0, 1)) = 1

		[Header(Camera Fade)]
		_CameraFadeStart("Camera Fade Start", Float) = 0
		_CameraFadeEnd("Camera Fade End", Float) = 0

		[Header(Render)]
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 0
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.65
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
			Name "SoftVolume"
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
			#pragma shader_feature_local _SOFTVOLUME_HEIGHT
			#pragma shader_feature_local _SOFTVOLUME_RADIAL
			#pragma shader_feature_local _RADIALMODE_SPHERE _RADIALMODE_CYLINDERXZ
			#pragma shader_feature_local _SOFTVOLUME_GEOMETRY_SOFTEN
			#pragma shader_feature_local _HEIGHTSPACE_WORLD _HEIGHTSPACE_OBJECT
			#pragma shader_feature_local _MARCHSTEPS_STEPS2 _MARCHSTEPS_STEPS4 _MARCHSTEPS_STEPS8
			#pragma shader_feature_local _NOISEOCTAVES_OCTAVES2 _NOISEOCTAVES_OCTAVES3 _NOISEOCTAVES_OCTAVES4

			#include "DragonLoot_SoftVolumePass.hlsl"

			SoftVolumeVaryings Vert(SoftVolumeAttributes input)
			{
				return SoftVolumeVertFog(input);
			}

			half4 Frag(SoftVolumeVaryings input) : SV_Target
			{
				return SoftVolumeFrag(input);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
