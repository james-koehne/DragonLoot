// Unlit particle billboards. Vertex colour drives HDR emissive output for bloom.
Shader "DragonLoot/ParticleUnlitEmissive"
{
	Properties
	{
		[HDR] _Color("Color", Color) = (1, 1, 1, 1)
		_MainTex("Texture", 2D) = "white" {}
		_EmissionIntensity("Emission Intensity", Range(0, 16)) = 3
		_Softness("Softness", Range(0, 8)) = 2
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
			Name "ParticleUnlitEmissive"
			Tags { "LightMode" = "UniversalForward" }

			Blend One One
			ZWrite Off
			ZTest LEqual
			Cull Off

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
				half4 _Color;
				half _EmissionIntensity;
				half _Softness;
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

			half SoftDisc(half2 uv, half softness)
			{
				if (softness <= 0.001h)
					return 1.0h;

				half2 centered = uv * 2.0h - 1.0h;
				half radial = saturate(1.0h - length(centered));
				return pow(radial, softness);
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				output.color = input.color;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				half4 texSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
				half shape = SoftDisc(input.uv, _Softness);
				half alpha = texSample.a * input.color.a * _Color.a * shape;
				if (alpha <= 1e-4h)
					discard;

				half3 emissive = texSample.rgb * input.color.rgb * _Color.rgb * max(_EmissionIntensity, 0.0h);
				half3 rgb = min(emissive * alpha, 64.0h);
				return half4(rgb, 0.0h);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
