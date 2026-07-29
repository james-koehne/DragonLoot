Shader "DragonLoot/Hover Outline Mask"
{
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
			ZTest LEqual
			Cull Back
			ColorMask R

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
				return 1.0h;
			}
			ENDHLSL
		}
	}

	FallBack Off
}
