Shader "DragonLoot/ConstellationLine"
{
	Properties
	{
		[HDR] _Color("Glow Color", Color) = (0.35, 0.85, 1.6, 1)
		[HDR] _CoreColor("Core Color", Color) = (1.8, 2.4, 3.0, 1)
		[HDR] _PulseColor("Pulse Color", Color) = (2.2, 1.4, 3.5, 1)

		[Header(Flow)]
		_ScrollSpeed("Scroll Speed", Float) = 1.35
		_PulseCount("Pulse Count", Range(1, 12)) = 3.5
		_PulseWidth("Pulse Width", Range(0.02, 0.5)) = 0.18
		_PulseSoft("Pulse Softness", Range(0.01, 0.25)) = 0.08
		_SecondarySpeed("Secondary Speed", Float) = -0.55
		_SecondaryStrength("Secondary Strength", Range(0, 1)) = 0.35

		[Header(Shape)]
		_CoreSharpness("Core Sharpness", Range(1, 64)) = 22
		_GlowSharpness("Glow Sharpness", Range(0.5, 24)) = 4.5
		_GlowIntensity("Glow Intensity", Range(0, 4)) = 1.35
		_CoreIntensity("Core Intensity", Range(0, 4)) = 1.8
		_EndpointBoost("Endpoint Boost", Range(0, 2)) = 0.65
		_ShimmerStrength("Shimmer Strength", Range(0, 1)) = 0.35
		_ShimmerScale("Shimmer Scale", Range(1, 64)) = 18
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
			Name "ConstellationLine"
			Tags { "LightMode" = "UniversalForward" }

			Blend One One
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
				half4 _Color;
				half4 _CoreColor;
				half4 _PulseColor;
				half _ScrollSpeed;
				half _PulseCount;
				half _PulseWidth;
				half _PulseSoft;
				half _SecondarySpeed;
				half _SecondaryStrength;
				half _CoreSharpness;
				half _GlowSharpness;
				half _GlowIntensity;
				half _CoreIntensity;
				half _EndpointBoost;
				half _ShimmerStrength;
				half _ShimmerScale;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			half PulseBand(half t, half width, half soft)
			{
				half halfW = max(width, soft) * 0.5h;
				half d = abs(t - 0.5h);
				return saturate(1.0h - smoothstep(halfW - soft, halfW + soft, d));
			}

			half Hash21(half2 p)
			{
				p = frac(p * half2(123.34h, 345.45h));
				p += dot(p, p + 34.345h);
				return frac(p.x * p.y);
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				output.color = input.color;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half across = abs(input.uv.y * 2.0h - 1.0h);
				half core = exp(-across * across * _CoreSharpness);
				half glow = exp(-across * across * _GlowSharpness);

				half time = _Time.y;
				half flowA = frac(input.uv.x * _PulseCount - time * _ScrollSpeed);
				half flowB = frac(input.uv.x * (_PulseCount * 0.6h) - time * _SecondarySpeed);
				half pulseA = PulseBand(flowA, _PulseWidth, _PulseSoft);
				half pulseB = PulseBand(flowB, _PulseWidth * 1.4h, _PulseSoft * 1.25h) * _SecondaryStrength;

				half ends = pow(saturate(1.0h - abs(input.uv.x * 2.0h - 1.0h)), 2.0h);
				half endpoint = ends * _EndpointBoost;

				half shimmerNoise = Hash21(half2(input.uv.x * _ShimmerScale, floor(time * 12.0h)));
				half shimmer = lerp(1.0h - _ShimmerStrength, 1.0h, shimmerNoise);

				half3 energy =
					_Color.rgb * glow * _GlowIntensity +
					_CoreColor.rgb * core * _CoreIntensity +
					_PulseColor.rgb * core * (pulseA + pulseB) * 2.2h +
					_CoreColor.rgb * glow * endpoint;

				energy *= shimmer;
				energy *= input.color.rgb * input.color.a;

				// Soft outer fade so additive edges don't hard-cut.
				half edgeFade = saturate(1.0h - across);
				energy *= edgeFade;

				return half4(energy, edgeFade * input.color.a);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
