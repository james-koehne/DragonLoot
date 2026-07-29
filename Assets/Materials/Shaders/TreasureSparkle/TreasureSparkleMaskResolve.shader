Shader "DragonLoot/Treasure Sparkle Mask Resolve"
{
	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
		}

		Pass
		{
			Name "StencilToMask"
			ZWrite Off
			ZTest Always
			Cull Off
			ColorMask R

			// Treasure sparkle stencil bit (Ref 64 / bit 6).
			Stencil
			{
				Ref 64
				Comp Equal
				Pass Keep
				ReadMask 64
				WriteMask 64
			}

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				uint vertexID : SV_VertexID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
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
