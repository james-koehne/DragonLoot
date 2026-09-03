// Dust Mote — atmospheric particle shader for Unity Particle System billboards.
Shader "DragonLoot/DustMote"
{
	Properties
	{
		[Header(Color)]
		[HDR] _Color("Color", Color) = (1.2, 0.95, 0.65, 0.35)
		[HDR] _CoreColor("Core Color", Color) = (1.8, 1.5, 1.0, 1)
		[HDR] _TwinkleColor("Twinkle Color", Color) = (2.2, 1.8, 1.2, 1)
		_Exposure("Exposure", Range(0, 8)) = 1.15
		_WarmBias("Warm Bias", Range(0, 1)) = 0.35
		_ColorVariation("Color Variation", Range(0, 1)) = 0.25
		_CoreHotness("Core Hotness", Range(0, 1)) = 0.55

		[Header(Shape)]
		_MainTex("Main Texture", 2D) = "white" {}
		[Toggle(_DUSTMOTE_USE_TEX)] _UseMainTex("Use Main Texture", Float) = 0
		_Softness("Softness", Range(0.5, 8)) = 2.2
		_AspectRatio("Aspect Ratio", Range(0.25, 4)) = 1

		[Header(Animation)]
		[Toggle(_DUSTMOTE_TWINKLE)] _TwinkleEnabled("Enable Twinkle", Float) = 1
		_TwinkleSpeed("Twinkle Speed", Float) = 2.5
		_TwinkleStrength("Twinkle Strength", Range(0, 1)) = 0.35
		_PulseSpeed("Pulse Speed", Float) = 1.2
		_PulseAmount("Pulse Amount", Range(0, 1)) = 0.15
		_ShimmerScale("Shimmer Scale", Range(0.1, 8)) = 1.8
		_ShimmerStrength("Shimmer Strength", Range(0, 1)) = 0.2
		_FlickerSpeed("Flicker Speed", Float) = 6
		_FlickerAmount("Flicker Amount", Range(0, 1)) = 0.08

		[Header(Turbulence)]
		[Toggle(_DUSTMOTE_TURBULENCE)] _TurbulenceEnabled("Enable Turbulence", Float) = 1
		_TurbulenceScale("Turbulence Scale", Range(0.01, 4)) = 0.65
		_TurbulenceStrength("Turbulence Strength", Range(0, 2)) = 0.35
		_TurbulenceSpeed("Turbulence Speed XYZ", Vector) = (0.08, 0.04, 0.06, 0)
		[KeywordEnum(Octaves2, Octaves3, Octaves4)] _NoiseOctaves("Noise Octaves", Float) = 1
		_WobbleAmplitude("Wobble Amplitude", Range(0, 0.5)) = 0.04
		_WobbleFrequency("Wobble Frequency", Float) = 1.8

		[Header(Depth And Distance)]
		[Toggle(_DUSTMOTE_SOFT_PARTICLES)] _SoftParticlesEnabled("Soft Particles", Float) = 1
		_SoftParticleDistance("Soft Particle Distance", Float) = 0.75
		_CameraFadeNear("Camera Fade Near", Float) = 0.15
		_CameraFadeFar("Camera Fade Far", Float) = 18
		[Toggle(_DUSTMOTE_GEOMETRY_SOFTEN)] _GeometrySoftenEnabled("Geometry Soften", Float) = 1
		_GeometrySoftenDistance("Geometry Soften Distance", Float) = 0.4
		_GeometrySoftenPower("Geometry Soften Power", Range(0.25, 8)) = 1
		_GeometrySoftenStrength("Geometry Soften Strength", Range(0, 1)) = 0.85

		[Header(Render)]
		[KeywordEnum(Additive, Alpha, Premultiply)] _BlendMode("Blend Mode", Float) = 0
		_BloomContribution("Bloom Contribution", Range(0, 8)) = 1.5
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.55
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 0
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
			Name "DustMote"
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
			#pragma shader_feature_local _DUSTMOTE_USE_TEX
			#pragma shader_feature_local _DUSTMOTE_TWINKLE
			#pragma shader_feature_local _DUSTMOTE_TURBULENCE
			#pragma shader_feature_local _DUSTMOTE_SOFT_PARTICLES
			#pragma shader_feature_local _DUSTMOTE_GEOMETRY_SOFTEN
			#pragma shader_feature_local _BLENDMODE_ADDITIVE _BLENDMODE_ALPHA _BLENDMODE_PREMULTIPLY
			#pragma shader_feature_local _NOISEOCTAVES_OCTAVES2 _NOISEOCTAVES_OCTAVES3 _NOISEOCTAVES_OCTAVES4

			#include "DustMotePass.hlsl"

			DustMoteVaryings Vert(DustMoteAttributes input)
			{
				return DustMoteVert(input);
			}

			half4 Frag(DustMoteVaryings input) : SV_Target
			{
				return DustMoteFrag(input);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
