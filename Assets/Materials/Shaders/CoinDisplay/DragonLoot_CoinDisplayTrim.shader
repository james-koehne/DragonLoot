Shader "DragonLoot/CoinDisplayTrim"
{
	Properties
	{
		[HDR] _TrimColor("Trim Color", Color) = (1, 0.82, 0.45, 1)

		[Header(Edge)]
		_TrimWidth("Trim Width", Range(0.01, 0.45)) = 0.08
		_TrimSoftness("Trim Softness", Range(0.001, 0.2)) = 0.04
		_InnerGlowWidth("Inner Glow Width", Range(0, 0.3)) = 0.05
		_GlowIntensity("Glow Intensity", Range(0, 4)) = 1.4
		_Alpha("Alpha", Range(0, 1)) = 0.9

		[Header(Motion)]
		_PulseSpeed("Pulse Speed", Float) = 1.1
		_PulseAmount("Pulse Amount", Range(0, 1)) = 0.18
		_ShimmerScale("Shimmer Scale", Range(1, 64)) = 18
		_ShimmerStrength("Shimmer Strength", Range(0, 1)) = 0.35
		_ScrollSpeed("Scroll Speed", Float) = 0.4
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
			Name "CoinDisplayTrim"
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
				half4 _TrimColor;
				half _TrimWidth;
				half _TrimSoftness;
				half _InnerGlowWidth;
				half _GlowIntensity;
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
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

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

				VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = posInputs.positionCS;
				output.positionWS = posInputs.positionWS;
				output.uv = input.uv;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 uv = input.uv;
				half edgeDist = min(min(uv.x, uv.y), min(1.0h - uv.x, 1.0h - uv.y));

				half outer = 1.0h - smoothstep(_TrimWidth, _TrimWidth + _TrimSoftness, edgeDist);
				half innerGlow = 1.0h - smoothstep(0.0h, _TrimWidth + _InnerGlowWidth, edgeDist);
				innerGlow = saturate(innerGlow - outer * 0.35h);

				half mask = saturate(outer + innerGlow * 0.55h);
				half time = _Time.y;
				half pulse = 1.0h + sin(time * _PulseSpeed * 6.2831853h) * _PulseAmount;

				half2 shimmerUv = floor(uv * _ShimmerScale) + floor(time * _ScrollSpeed * 8.0h);
				half shimmerNoise = Hash21(shimmerUv);
				half shimmer = lerp(1.0h - _ShimmerStrength, 1.0h, shimmerNoise);

				half edgeBoost = outer * _GlowIntensity + innerGlow * (_GlowIntensity * 0.45h);
				half3 color = _TrimColor.rgb * (0.55h + edgeBoost) * pulse * shimmer;
				half alpha = saturate(_Alpha * _TrimColor.a * mask * pulse);

				return half4(color, alpha);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
