Shader "DragonLoot/Treasure Sparkle"
{
	Properties
	{
		[HDR] _SparkleColor ("Sparkle Color", Color) = (8, 6.2, 2.8, 1)
		_SparkleGlintTex ("Glint Texture", 2D) = "white" {}
		_SparkleUseGlintTex ("Use Glint Texture", Float) = 0
		_SparkleGlintTexBlend ("Glint Texture Blend", Range(0, 1)) = 1
		_SparkleTargetPixelSize ("Target Pixel Size", Range(0.25, 6)) = 0.9
		_SparkleGlintTexSizeNear ("Glint Tex Size Near", Range(0.25, 32)) = 16
		_SparkleGlintTexSizeFar ("Glint Tex Size Far", Range(0.25, 32)) = 4
		_SparkleGlintSizeNearDistance ("Glint Size Near Distance", Float) = 1.5
		_SparkleGlintSizeFarDistance ("Glint Size Far Distance", Float) = 35
		_SparkleSpotFalloff ("Spot Falloff", Range(0.5, 4)) = 1.8
		_SparkleCoreHotness ("Core Hotness", Range(0, 1)) = 0.75
		_SparkleBloomContribution ("Bloom Contribution", Range(0, 8)) = 4
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "SparkleQuad"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend One One

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct GlintInstance
			{
				float3 centerWS;
				float intensity;
				float sizeJitter;
				float pad0;
				float pad1;
				float pad2;
			};

			StructuredBuffer<GlintInstance> _SparkleGlints;
			TEXTURE2D(_SparkleGlintTex);
			SAMPLER(sampler_SparkleGlintTex);

			float4 _SparkleColor;
			float _SparkleUseGlintTex;
			float _SparkleGlintTexBlend;
			float _SparkleTargetPixelSize;
			float _SparkleGlintTexSizeNear;
			float _SparkleGlintTexSizeFar;
			float _SparkleGlintSizeNearDistance;
			float _SparkleGlintSizeFarDistance;
			float _SparkleSpotFalloff;
			float _SparkleCoreHotness;
			float _SparkleBloomContribution;
			float3 _SparkleCameraPosition;
			float4 _SparkleResolution;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				uint instanceID : SV_InstanceID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 stampUV : TEXCOORD0;
				nointerpolation float intensity : TEXCOORD1;
				nointerpolation float useTex : TEXCOORD2;
			};

			static const float2 kQuadCorners[6] =
			{
				float2(-0.5, -0.5),
				float2( 0.5, -0.5),
				float2( 0.5,  0.5),
				float2(-0.5, -0.5),
				float2( 0.5,  0.5),
				float2(-0.5,  0.5)
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				GlintInstance glint = _SparkleGlints[input.instanceID];
				float2 corner = kQuadCorners[input.vertexID % 6];

				float useTex = (_SparkleUseGlintTex > 0.5) ? saturate(_SparkleGlintTexBlend) : 0.0;
				float camDist = distance(_SparkleCameraPosition, glint.centerWS);
				float nearDist = _SparkleGlintSizeNearDistance;
				float farDist = max(_SparkleGlintSizeFarDistance, nearDist + 1e-3);
				float sizeT = saturate((camDist - nearDist) / (farDist - nearDist));
				float texSize = lerp(max(_SparkleGlintTexSizeNear, 0.25), max(_SparkleGlintTexSizeFar, 0.25), sizeT);
				float baseSize = lerp(max(_SparkleTargetPixelSize, 0.25), texSize, useTex);
				float diameterPx = max(baseSize * max(glint.sizeJitter, 0.25), 0.25);

				float2 resolution = max(_SparkleResolution.xy, float2(1.0, 1.0));
				float4 clip = TransformWorldToHClip(glint.centerWS);
				// Behind-camera / degenerate W covers the screen with additive HDR (white flash).
				if (clip.w <= 1e-4 || glint.intensity <= 1e-5)
				{
					output.positionCS = float4(0, 0, -1, 0);
					output.stampUV = 0;
					output.intensity = 0;
					output.useTex = 0;
					return output;
				}

				float2 ndcPerPixel = float2(2.0 / resolution.x, 2.0 / resolution.y);
				clip.xy += corner * diameterPx * ndcPerPixel * clip.w;

				output.positionCS = clip;
				output.stampUV = corner + 0.5;
				output.intensity = glint.intensity;
				output.useTex = useTex;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float spot = 0;
				float3 stampTint = 1;
				if (input.useTex > 0.5)
				{
					float4 texSample = SAMPLE_TEXTURE2D(_SparkleGlintTex, sampler_SparkleGlintTex, input.stampUV);
					spot = max(texSample.a, max(texSample.r, max(texSample.g, texSample.b)));
					stampTint = max(texSample.rgb, texSample.a);
				}
				else
				{
					float2 fromCenter = input.stampUV - 0.5;
					float glintDist = length(fromCenter) * 2.0;
					spot = saturate(1.0 - glintDist);
					spot = pow(spot, max(_SparkleSpotFalloff, 0.5));
				}

				if (spot <= 1e-4 || input.intensity <= 1e-5)
					return half4(0, 0, 0, 0);

				float2 fromCenterCore = input.stampUV - 0.5;
				float coreMask = saturate(1.0 - length(fromCenterCore) * 3.5) * spot;
				float3 whiteHot = float3(1.25, 1.18, 1.05);
				float peak = max(max(_SparkleColor.r, _SparkleColor.g), _SparkleColor.b);
				float coreMix = saturate(_SparkleCoreHotness) * coreMask;
				float3 tint = lerp(_SparkleColor.rgb, whiteHot * peak, coreMix) * stampTint;

				float3 rgb = tint * saturate(input.intensity) * max(_SparkleBloomContribution, 0.0);
				rgb = min(rgb, 64.0);
				return half4(rgb, 0);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
