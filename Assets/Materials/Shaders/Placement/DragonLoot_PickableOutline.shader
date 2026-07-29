Shader "DragonLoot/Pickable Outline"
{
	Properties
	{
		[HDR] _GlowColor ("Glow Color", Color) = (1.2, 1.0, 0.5, 1)
		[HDR] _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.35, 1)
		_OutlineWidth ("Outline Width (pixels)", Range(0.5, 24)) = 3
		_GlowWidth ("Glow Spread (pixels)", Range(0, 24)) = 6
		_GlowIntensity ("Glow Intensity", Range(0, 4)) = 1.2
		_OutlineIntensity ("Outline Intensity", Range(0, 2)) = 1
		_OutlineDepthBias ("Depth Bias", Range(0, 0.01)) = 0.0008
		_HullExtrudeScale ("Hull Extrude Scale", Range(0, 0.01)) = 0
		_FrontFaceFallback ("Front Face Fallback", Range(0, 1)) = 1
		_PulseSpeed ("Pulse Speed", Float) = 1.1
		_PulseAmount ("Pulse Amount", Range(0, 1)) = 0.1
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
		}

		Pass
		{
			Name "Glow"
			Tags { "LightMode" = "UniversalForward" }

			Blend One One
			ZWrite Off
			ZTest LEqual
			Cull Front

			HLSLPROGRAM
			#pragma vertex GlowVert
			#pragma fragment GlowFrag
			#include "PickableOutlinePass.hlsl"

			OutlineVaryings GlowVert(OutlineAttributes input)
			{
				float widthPixels = max(_OutlineWidth + _GlowWidth, 0.0);
				return OutlineVert(input, widthPixels, false);
			}

			half4 GlowFrag(OutlineVaryings input) : SV_Target
			{
				return OutlineGlowFrag(input);
			}
			ENDHLSL
		}

		Pass
		{
			Name "CoreA"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Cull Front

			HLSLPROGRAM
			#pragma vertex CoreAVert
			#pragma fragment CoreFrag
			#include "PickableOutlinePass.hlsl"

			OutlineVaryings CoreAVert(OutlineAttributes input)
			{
				float widthPixels = max(_OutlineWidth, 0.0);
				return OutlineVert(input, widthPixels, false);
			}

			half4 CoreFrag(OutlineVaryings input) : SV_Target
			{
				return OutlineCoreFrag(input);
			}
			ENDHLSL
		}

		Pass
		{
			Name "CoreB"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Cull Front

			HLSLPROGRAM
			#pragma vertex CoreBVert
			#pragma fragment CoreBFrag
			#include "PickableOutlinePass.hlsl"

			OutlineVaryings CoreBVert(OutlineAttributes input)
			{
				float widthPixels = max(_OutlineWidth, 0.0);
				return OutlineVert(input, widthPixels, true);
			}

			half4 CoreBFrag(OutlineVaryings input) : SV_Target
			{
				return OutlineCoreFrag(input);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
