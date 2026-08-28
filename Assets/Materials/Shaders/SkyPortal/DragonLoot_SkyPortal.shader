// Sky Portal — cookie-masked opening that samples an assigned sky cubemap (same asset as your Skybox material _Tex).
Shader "DragonLoot/SkyPortal"
{
	Properties
	{
		[Header(Mask)]
		[MainTexture] _TextureMask("Sky Visibility Mask", 2D) = "white" {}

		[Header(Opening)]
		_Feather("Feather Width", Range(0, 1)) = 0.3
		[Toggle(_SKYPORTAL_VIEW_RAY)] _SkyDirectionView("Sky Direction View Ray", Float) = 0
		[Toggle(_SKYPORTAL_FLIP_SKY)] _FlipSkyDirection("Flip Sky Direction", Float) = 0

		[Header(Sky)]
		[NoScaleOffset] _SkyCubemap("Sky Cubemap", Cube) = "" {}
		_SkyExposure("Sky Exposure", Range(0, 8)) = 1
		_SkyRotation("Sky Rotation", Range(0, 360)) = 0
		_SkyIntensity("Sky Intensity", Range(0, 10)) = 1
		[HDR] _SkyTint("Sky Tint", Color) = (1, 1, 1, 1)

		[Header(Edge)]
		[HDR] _EdgeGlow("Edge Glow", Range(0, 5)) = 0.35
		_EdgeGlowPower("Edge Glow Power", Range(0.5, 8)) = 2.5

		[Header(Depth)]
		_DepthFadeDistance("Depth Fade Distance", Float) = 0.35

		[Header(Noise)]
		_NoiseTexture("Noise Texture", 2D) = "gray" {}
		_NoiseStrength("Noise Strength", Range(0, 1)) = 0
		_NoiseScale("Noise Scale", Float) = 4
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "SkyPortal"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off
			ZTest LEqual

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma shader_feature_local _ _SKYPORTAL_VIEW_RAY
			#pragma shader_feature_local _ _SKYPORTAL_FLIP_SKY
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

			TEXTURE2D(_TextureMask);
			SAMPLER(sampler_TextureMask);
			TEXTURE2D(_NoiseTexture);
			SAMPLER(sampler_NoiseTexture);
			TEXTURECUBE(_SkyCubemap);
			SAMPLER(sampler_SkyCubemap);

			CBUFFER_START(UnityPerMaterial)
				float4 _TextureMask_ST;
				float4 _NoiseTexture_ST;
				half _Feather;
				half _SkyExposure;
				half _SkyRotation;
				half _SkyIntensity;
				half4 _SkyTint;
				half _EdgeGlow;
				half _EdgeGlowPower;
				half _DepthFadeDistance;
				half _NoiseStrength;
				half _NoiseScale;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float2 uv : TEXCOORD2;
				half fogFactor : TEXCOORD3;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			half SampleMask(float2 uv)
			{
				half4 maskSample = SAMPLE_TEXTURE2D(_TextureMask, sampler_TextureMask, uv);
				return maskSample.r;
			}

			half ComputePortalAlpha(half mask)
			{
				half featherHalf = _Feather * 0.5h;
				half low = 0.5h - featherHalf;
				half high = 0.5h + featherHalf;
				return smoothstep(low, high, mask);
			}

			float3 RotateDirectionY(float3 direction, half degrees)
			{
				half rad = radians(degrees);
				half s = sin(rad);
				half c = cos(rad);
				return float3(c * direction.x + s * direction.z, direction.y, -s * direction.x + c * direction.z);
			}

			half3 SamplePortalSky(float3 positionWS, float3 normalWS)
			{
				float3 skyDirectionWS;
#if defined(_SKYPORTAL_VIEW_RAY)
				// Through the opening: camera → surface (not URP view dir, which points back at the camera).
				skyDirectionWS = normalize(positionWS - GetCurrentViewPosition());
#else
				// Portal normal should face the cave interior; sky is on the opposite side.
				skyDirectionWS = -normalize(normalWS);
#endif

#if defined(_SKYPORTAL_FLIP_SKY)
				skyDirectionWS = -skyDirectionWS;
#endif

				skyDirectionWS = RotateDirectionY(skyDirectionWS, _SkyRotation);
				half3 sky = SAMPLE_TEXTURECUBE_LOD(_SkyCubemap, sampler_SkyCubemap, skyDirectionWS, 0.0h).rgb;
				return sky * _SkyExposure * _SkyIntensity * _SkyTint.rgb;
			}

			// Dummy/unset _CameraDepthTexture is typically 1x1 (texel size 1). A real camera copy is ~1/width.
			bool HasValidSceneDepthTexture()
			{
				return _CameraDepthTexture_TexelSize.x > 0.0 && _CameraDepthTexture_TexelSize.x < 0.5;
			}

			half ApplyDepthFade(half alpha, float3 positionWS, float4 positionCS)
			{
				if (_DepthFadeDistance <= 0.0001h)
					return alpha;

				if (!HasValidSceneDepthTexture())
					return alpha;

				float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
				float sceneRawDepth = SampleSceneDepth(normalizedScreenSpaceUV);
				float portalEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
				float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

				float sceneInFront = portalEyeDepth - sceneEyeDepth;
				if (sceneInFront <= 0.0)
					return alpha;

				half depthFade = 1.0h - saturate(sceneInFront / _DepthFadeDistance);
				return alpha * depthFade;
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

				output.positionCS = positionInputs.positionCS;
				output.positionWS = positionInputs.positionWS;
				output.normalWS = normalInputs.normalWS;
				output.uv = TRANSFORM_TEX(input.uv, _TextureMask);
				output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 maskUV = input.uv;
				if (_NoiseStrength > 0.0001h)
				{
					half2 noise = SAMPLE_TEXTURE2D(_NoiseTexture, sampler_NoiseTexture, maskUV * _NoiseScale).rg;
					noise = noise * 2.0h - 1.0h;
					maskUV += noise * _NoiseStrength * 0.05h;
				}

				half mask = SampleMask(maskUV);
				half alpha = ComputePortalAlpha(mask);
				alpha = ApplyDepthFade(alpha, input.positionWS, input.positionCS);
				clip(alpha - 0.0001h);

				half3 skyColor = SamplePortalSky(input.positionWS, input.normalWS);

				half edge = saturate(1.0h - mask);
				half glow = _EdgeGlow * pow(edge, _EdgeGlowPower);
				skyColor += glow * skyColor;

				half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
				half3 foggedSky = MixFog(skyColor, fogCoord);
				half3 color = lerp(foggedSky, skyColor, alpha);

				return half4(color, alpha);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
