Shader "DragonLoot/Treasure Sparkle Pile Mask"
{
	Properties
	{
		[Toggle] _DeformEnabled("Runtime Deform Map", Float) = 0
		_DeformMap("Deform Height Map", 2D) = "black" {}
		_DeformScale("Deform Height Scale", Float) = 1.5
		_DeformWorldSize("Deform World Size", Float) = 4
		_DeformResolution("Deform Resolution", Float) = 64
		_GroundLevelHeight("Ground Level Height", Float) = 0.01
		_MaskWriteValue("Mask Write Value", Float) = 0.25
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
			Name "PileMask"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ZWrite Off
			ZTest LEqual
			Cull Off
			ColorMask R
			// Floor is often coplanar with the pile skirt; pull the mask toward the camera so the bottom isn't eaten.
			Offset -2, -80

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half _DeformEnabled;
				float _DeformScale;
				float _DeformWorldSize;
				float _DeformResolution;
				float _GroundLevelHeight;
				float _MaskWriteValue;
			CBUFFER_END

			TEXTURE2D(_DeformMap);
			SAMPLER(sampler_DeformMap);

			// Mask-only lift so near-ground slopes clear the floor depth without changing the visible pile.
			static const float kMaskHeightBias = 0.02;

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

			float3 ApplyDeformOS(float3 positionOS, float2 uv)
			{
				if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
				{
					float h = SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv, 0).r;
					float worldH = h * _DeformScale;
					if (worldH < _GroundLevelHeight)
						positionOS.y = -1000.0;
					else
						positionOS.y += worldH + kMaskHeightBias;
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

			half Frag(Varyings input) : SV_Target
			{
				if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
				{
					float h = SAMPLE_TEXTURE2D(_DeformMap, sampler_DeformMap, input.deformUV).r * _DeformScale;
					clip(h - _GroundLevelHeight);
				}
				return _MaskWriteValue;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
