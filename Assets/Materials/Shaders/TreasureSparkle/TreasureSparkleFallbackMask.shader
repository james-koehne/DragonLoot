Shader "DragonLoot/Treasure Sparkle Fallback Mask"
{
	Properties
	{
		_MaskWriteValue ("Mask Write Value", Float) = 1
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
			Name "FallbackMask"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ZWrite Off
			ZTest LEqual
			Cull Off
			ColorMask R
			Offset 0, -1

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			float _MaskWriteValue;

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half Frag(Varyings input) : SV_Target
			{
				return _MaskWriteValue;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
