Shader "DragonLoot/Placement Ghost"
{
	Properties
	{
		_BaseColor ("Color", Color) = (0.25, 0.9, 0.35, 0.35)
		_FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5
		_FresnelBoost ("Fresnel Boost", Range(0, 2)) = 0.65
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
		}

		Pass
		{
			Name "ForwardUnlit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float _FresnelPower;
				float _FresnelBoost;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
			};

			Varyings vert( Attributes input )
			{
				Varyings output;
				VertexPositionInputs posInputs = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInputs = GetVertexNormalInputs( input.normalOS );
				output.positionCS = posInputs.positionCS;
				output.positionWS = posInputs.positionWS;
				output.normalWS = normalInputs.normalWS;
				return output;
			}

			half4 frag( Varyings input ) : SV_Target
			{
				float3 normalWS = normalize( input.normalWS );
				float3 viewDirWS = GetWorldSpaceNormalizeViewDir( input.positionWS );
				float fresnel = pow( saturate( 1.0 - saturate( dot( normalWS, viewDirWS ) ) ), _FresnelPower );
				float alpha = saturate( _BaseColor.a + fresnel * _FresnelBoost * _BaseColor.a );
				float3 color = _BaseColor.rgb * ( 0.75 + fresnel * 0.5 );
				return half4( color, alpha );
			}
			ENDHLSL
		}
	}

	FallBack Off
}
