Shader "DragonLoot/Hover Outline Mask"
{
	Properties
	{
		[Toggle] _DeformEnabled("Runtime Deform Map", Float) = 0
		_DeformMap("Deform Height Map", 2D) = "black" {}
		_DeformScale("Deform Height Scale", Float) = 1.5
		_DeformWorldSize("Deform World Size", Float) = 4
		_DeformResolution("Deform Resolution", Float) = 64
		_DeformSampleBlur("Deform Sample Blur", Float) = 0
		_GroundLevelHeight("Ground Level Height", Float) = 0.01
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
		}

		Pass
		{
			Name "Mask"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ZWrite Off
			ZTest Always
			Cull Off
			ColorMask R

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half _DeformEnabled;
				float _DeformScale;
				float _DeformWorldSize;
				float _DeformResolution;
				float _DeformSampleBlur;
				float _GroundLevelHeight;
			CBUFFER_END

			TEXTURE2D(_DeformMap);
			SAMPLER(sampler_DeformMap);

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 deformUV : TEXCOORD0;
			};

			float SampleDeformHeight(float2 uv)
			{
				float center = SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv, 0).r;
				float blur = _DeformSampleBlur;
				if (blur <= 0.001)
					return center;

				float uvStep = rcp(max(_DeformResolution, 1.0)) * blur;
				float sum = center * 2.0;
				sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv + float2(uvStep, 0), 0).r;
				sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv - float2(uvStep, 0), 0).r;
				sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv + float2(0, uvStep), 0).r;
				sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv - float2(0, uvStep), 0).r;
				return sum * (1.0 / 6.0);
			}

			float3 ApplyDeformOS(float3 positionOS, float2 uv)
			{
				if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
				{
					float h = SampleDeformHeight(uv);
					float worldH = h * _DeformScale;
					if (worldH < _GroundLevelHeight)
						positionOS.y = -1000.0;
					else
						positionOS.y += worldH;
				}
				return positionOS;
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				float3 positionOS = ApplyDeformOS(input.positionOS.xyz, input.texcoord);
				output.positionCS = TransformObjectToHClip(positionOS);
				output.deformUV = input.texcoord;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
				{
					float h = SampleDeformHeight(input.deformUV) * _DeformScale;
					clip(h - _GroundLevelHeight);
				}
				return half4(1.0h, 0.0h, 0.0h, 1.0h);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
