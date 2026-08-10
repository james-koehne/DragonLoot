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
			#pragma multi_compile_instancing
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			float _MaskWriteValue;

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				return _MaskWriteValue;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
