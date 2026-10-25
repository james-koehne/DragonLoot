// Floor Gleam — whole-mesh area glow + world-space multi-line swipe on a floor overlay.
Shader "DragonLoot/FloorGleam"
{
	Properties
	{
		[Header(Glow)]
		[HDR] _Color("Color", Color) = (1.35, 0.95, 0.45, 1)
		_Exposure("Exposure", Range(0, 8)) = 1.75
		_Alpha("Alpha", Range(0, 2)) = 1

		[Header(Area Glow Driven By Controller)]
		_AreaGlow("Area Glow", Range(0, 1)) = 0
		_AreaGlowMul("Area Glow Mul", Range(0, 4)) = 0.55

		[Header(Swipe Driven By Controller)]
		_GleamOrigin("Gleam Origin", Vector) = (0, 0, 0, 0)
		_GleamAxis("Gleam Axis", Vector) = (1, 0, 0, 0)
		_GleamSpan("Gleam Span", Float) = 10
		_GleamT("Gleam T", Range(0, 1)) = 0
		_BandMul("Band Mul", Range(0, 4)) = 1.25

		[Header(Band)]
		_BandWidth("Band Width", Range(0.005, 0.4)) = 0.08
		_BandSoftness("Band Softness", Range(0.001, 0.25)) = 0.04
		_EdgeFade("Edge Fade", Range(0.01, 0.4)) = 0.08

		[Header(Multiple Lines)]
		_LineCount("Line Count", Range(1, 8)) = 3
		_LineSpacing("Line Spacing", Range(0, 0.35)) = 0.07

		[Header(Render)]
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 2
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.25
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
			Name "FloorGleam"
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

			#include "DragonLoot_FloorGleamPass.hlsl"

			FloorGleamVaryings Vert(FloorGleamAttributes input)
			{
				return FloorGleamVert(input);
			}

			half4 Frag(FloorGleamVaryings input) : SV_Target
			{
				return FloorGleamFrag(input);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
