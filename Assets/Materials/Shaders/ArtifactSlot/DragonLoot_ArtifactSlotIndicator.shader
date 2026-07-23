Shader "DragonLoot/ArtifactSlotIndicator"
{
	Properties
	{
		[HDR] _TintColor("Tint Color", Color) = (0.35, 0.95, 1.4, 0.55)
		[HDR] _RimColor("Rim Color", Color) = (0.2, 1.2, 1.8, 1)
		[HDR] _CoreColor("Core Color", Color) = (0.08, 0.35, 0.5, 1)

		[Header(Fresnel)]
		_FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.2
		_RimIntensity("Rim Intensity", Range(0, 4)) = 1.65
		_CoreIntensity("Core Intensity", Range(0, 2)) = 0.35
		_Alpha("Alpha", Range(0, 1)) = 0.42

		[Header(Motion)]
		_PulseSpeed("Pulse Speed", Float) = 1.25
		_PulseAmount("Pulse Amount", Range(0, 1)) = 0.22
		_ShimmerScale("Shimmer Scale", Range(1, 64)) = 14
		_ShimmerStrength("Shimmer Strength", Range(0, 1)) = 0.4
		_ScrollSpeed("Scroll Speed", Float) = 0.35
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
			Name "ArtifactSlotIndicator"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off
			ZTest LEqual

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _TintColor;
				half4 _RimColor;
				half4 _CoreColor;
				half _FresnelPower;
				half _RimIntensity;
				half _CoreIntensity;
				half _Alpha;
				half _PulseSpeed;
				half _PulseAmount;
				half _ShimmerScale;
				half _ShimmerStrength;
				half _ScrollSpeed;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				float3 viewDirWS : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			half Hash21(half2 p)
			{
				p = frac(p * half2(123.34h, 345.45h));
				p += dot(p, p + 34.345h);
				return frac(p.x * p.y);
			}

			half SchlickFresnel(half ndv, half power)
			{
				half f = 1.0h - saturate(ndv);
				return pow(f, power);
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

				output.positionCS = posInputs.positionCS;
				output.positionWS = posInputs.positionWS;
				output.normalWS = normInputs.normalWS;
				output.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half3 normalWS = normalize(input.normalWS);
				half3 viewDirWS = normalize(input.viewDirWS);
				half ndv = saturate(dot(normalWS, viewDirWS));

				half fresnel = SchlickFresnel(ndv, _FresnelPower);
				half time = _Time.y;

				half pulse = 1.0h + sin(time * _PulseSpeed * 6.2831853h) * _PulseAmount;
				half shimmerNoise = Hash21(floor(input.positionWS.xz * _ShimmerScale) + floor(time * _ScrollSpeed * 8.0h));
				half shimmer = lerp(1.0h - _ShimmerStrength, 1.0h, shimmerNoise);

				half3 rim = _RimColor.rgb * fresnel * _RimIntensity;
				half3 core = _CoreColor.rgb * (1.0h - fresnel) * _CoreIntensity;
				half3 tint = _TintColor.rgb * (core + rim * 0.65h);

				half alpha = saturate(_Alpha * _TintColor.a);
				alpha *= lerp(0.35h, 1.0h, fresnel);
				alpha *= pulse * shimmer;

				half3 color = (tint + rim) * pulse;
				return half4(color, alpha);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
