Shader "DragonLoot/Treasure Sparkle Fallback Mask"
{
	Properties
	{
		_MaskWriteValue ("Mask Write Value", Float) = 1
		_GoldPileCoinDitherEnable ("Coin Lod Dither Enable", Float) = 0
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
			#include "../Coin/CoinPileLodDither.hlsl"

			float _MaskWriteValue;

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = posInputs.positionCS;
				output.positionWS = posInputs.positionWS;
				return output;
			}

			half Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				CoinPileApplyLodDither(input.positionWS, input.positionCS);
				return _MaskWriteValue;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
