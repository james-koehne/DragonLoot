Shader "DragonLoot/Placement Ghost"
{
	Properties
	{
		_BaseColor ("Color", Color) = (0.25, 0.9, 0.35, 0.35)
		_RimColor ("Rim Color", Color) = (0.45, 1.1, 0.55, 1)
		_CoreColor ("Core Color", Color) = (0.05, 0.2, 0.08, 1)
		_FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.4
		_FresnelBoost ("Fresnel Boost", Range(0, 2)) = 0.7
		_RimIntensity ("Rim Intensity", Range(0, 2)) = 1.15
		_CoreIntensity ("Core Intensity", Range(0, 1)) = 0.28
		_PulseSpeed ("Pulse Speed", Float) = 0.85
		_PulseAmount ("Pulse Amount", Range(0, 1)) = 0.12
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
				float4 _RimColor;
				float4 _CoreColor;
				float _FresnelPower;
				float _FresnelBoost;
				float _RimIntensity;
				float _CoreIntensity;
				float _PulseSpeed;
				float _PulseAmount;
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
				float ndv = saturate( dot( normalWS, viewDirWS ) );
				float fresnel = pow( saturate( 1.0 - ndv ), _FresnelPower );

				float pulse = 1.0 + sin( _Time.y * _PulseSpeed * 6.2831853 ) * _PulseAmount;

				float3 rim = _RimColor.rgb * fresnel * _RimIntensity;
				float3 core = _CoreColor.rgb * ( 1.0 - fresnel ) * _CoreIntensity;
				float3 tint = _BaseColor.rgb * ( 0.55 + fresnel * 0.55 );
				float3 color = ( tint + rim + core ) * pulse;

				float alpha = saturate( _BaseColor.a + fresnel * _FresnelBoost * _BaseColor.a );
				alpha *= lerp( 0.55, 1.0, fresnel );
				alpha *= pulse;

				return half4( color, saturate( alpha ) );
			}
			ENDHLSL
		}
	}

	FallBack Off
}
