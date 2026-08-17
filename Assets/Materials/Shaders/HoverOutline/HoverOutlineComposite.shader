Shader "DragonLoot/Hover Outline Composite"
{
	Properties
	{
		[HDR] _OutlineColor ("Outline Color", Color) = (1, 0.85, 0.35, 1)
		[HDR] _QuestOutlineColor ("Quest Outline Color", Color) = (0.2, 0.95, 1, 1)
		_OutlineIntensity ("Outline Intensity", Range(0, 2)) = 1
		_QuestOutlineIntensity ("Quest Outline Intensity", Range(0, 2)) = 1.1
		_HdrBoost ("HDR Boost", Range(0, 8)) = 1.35
		_QuestHdrBoost ("Quest HDR Boost", Range(0, 8)) = 1.6
		_Scale ("Scale (pixels)", Range(1, 8)) = 3
		_MaskDilatePixels ("Mask Dilate (pixels)", Range(0, 8)) = 2
		_DepthThreshold ("Depth Threshold", Range(0, 10)) = 1.5
		_NormalThreshold ("Normal Threshold", Range(0, 1)) = 0.4
		_DepthNormalThreshold ("Depth Normal Threshold", Range(0, 1)) = 0.5
		_DepthNormalThresholdScale ("Depth Normal Threshold Scale", Range(0, 20)) = 7
		_NormalEdgeWeight ("Normal Edge Weight", Range(0, 1)) = 1
		_PulseSpeed ("Pulse Speed", Float) = 1.1
		_PulseAmount ("Pulse Amount", Range(0, 1)) = 0.1
		_QuestPulseSpeed ("Quest Pulse Speed", Float) = 1.35
		_QuestPulseAmount ("Quest Pulse Amount", Range(0, 1)) = 0.14
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "Composite"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

			TEXTURE2D(_HoverOutlineMask);
			SAMPLER(sampler_HoverOutlineMask);
			float4 _HoverOutlineMask_TexelSize;

			TEXTURE2D(_QuestOutlineMask);
			SAMPLER(sampler_QuestOutlineMask);

			float4 _OutlineColor;
			float4 _QuestOutlineColor;
			float _OutlineIntensity;
			float _QuestOutlineIntensity;
			float _HdrBoost;
			float _QuestHdrBoost;
			float _Scale;
			float _MaskDilatePixels;
			float _DepthThreshold;
			float _NormalThreshold;
			float _DepthNormalThreshold;
			float _DepthNormalThresholdScale;
			float _NormalEdgeWeight;
			float _PulseSpeed;
			float _PulseAmount;
			float _QuestPulseSpeed;
			float _QuestPulseAmount;
			float4x4 _ClipToView;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 viewSpaceDir : TEXCOORD1;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
				output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
				float4 clipPos = output.positionCS;
				output.viewSpaceDir = mul(_ClipToView, clipPos).xyz;
				return output;
			}

			float SampleRaw(Texture2D tex, SamplerState samp, float2 uv)
			{
				return SAMPLE_TEXTURE2D(tex, samp, uv).r;
			}

			float SampleNeighborhood(Texture2D tex, SamplerState samp, float2 uv, float scalePixels, bool dilate)
			{
				int radius = (int)round(max(scalePixels, 0.0));
				float2 texel = _HoverOutlineMask_TexelSize.xy;
				float mask = dilate ? 0.0 : 1.0;
				for (int y = -radius; y <= radius; y++)
				{
					for (int x = -radius; x <= radius; x++)
					{
						float sample = SampleRaw(tex, samp, uv + float2(x, y) * texel);
						mask = dilate ? max(mask, sample) : min(mask, sample);
					}
				}
				return mask;
			}

			float ComputeMaskSilhouette(Texture2D tex, SamplerState samp, float2 uv, float widthPixels)
			{
				float scale = max(widthPixels, 1.0);
				float2 texelSize = _HoverOutlineMask_TexelSize.xy;
				float halfScaleFloor = floor(scale * 0.5);
				float halfScaleCeil = ceil(scale * 0.5);

				float2 bottomLeftUV = uv - texelSize * halfScaleFloor;
				float2 topRightUV = uv + texelSize * halfScaleCeil;
				float2 bottomRightUV = uv + float2(texelSize.x * halfScaleCeil, -texelSize.y * halfScaleFloor);
				float2 topLeftUV = uv + float2(-texelSize.x * halfScaleFloor, texelSize.y * halfScaleCeil);

				float m0 = SampleRaw(tex, samp, bottomLeftUV);
				float m1 = SampleRaw(tex, samp, topRightUV);
				float m2 = SampleRaw(tex, samp, bottomRightUV);
				float m3 = SampleRaw(tex, samp, topLeftUV);

				float d0 = m1 - m0;
				float d1 = m3 - m2;
				float edge = sqrt(d0 * d0 + d1 * d1);
				return edge > 0.1 ? 1.0 : 0.0;
			}

			float ComputeRoyStanEdge(float2 uv, float3 viewSpaceDir)
			{
				float scale = max(_Scale, 1.0);
				float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
				float halfScaleFloor = floor(scale * 0.5);
				float halfScaleCeil = ceil(scale * 0.5);

				float2 bottomLeftUV = uv - texelSize * halfScaleFloor;
				float2 topRightUV = uv + texelSize * halfScaleCeil;
				float2 bottomRightUV = uv + float2(texelSize.x * halfScaleCeil, -texelSize.y * halfScaleFloor);
				float2 topLeftUV = uv + float2(-texelSize.x * halfScaleFloor, texelSize.y * halfScaleCeil);

				float depth0 = SampleSceneDepth(bottomLeftUV);
				float depth1 = SampleSceneDepth(topRightUV);
				float depth2 = SampleSceneDepth(bottomRightUV);
				float depth3 = SampleSceneDepth(topLeftUV);

				float depthFiniteDifference0 = depth1 - depth0;
				float depthFiniteDifference1 = depth3 - depth2;
				float edgeDepth = sqrt(depthFiniteDifference0 * depthFiniteDifference0 + depthFiniteDifference1 * depthFiniteDifference1) * 100.0;

				float3 normal0 = SampleSceneNormals(bottomLeftUV);
				float3 normal1 = SampleSceneNormals(topRightUV);
				float3 normal2 = SampleSceneNormals(bottomRightUV);
				float3 normal3 = SampleSceneNormals(topLeftUV);

				float3 normalFiniteDifference0 = normal1 - normal0;
				float3 normalFiniteDifference1 = normal3 - normal2;
				float edgeNormalMag = sqrt(dot(normalFiniteDifference0, normalFiniteDifference0) + dot(normalFiniteDifference1, normalFiniteDifference1));
				float edgeNormal = edgeNormalMag > _NormalThreshold ? 1.0 : 0.0;

				float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, normal0);
				float NdotV = 1.0 - dot(normalize(viewNormal), normalize(-viewSpaceDir));
				float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / max(1.0 - _DepthNormalThreshold, 1e-5));
				float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1.0;
				float depthThreshold = _DepthThreshold * depth0 * normalThreshold;
				edgeDepth = edgeDepth > depthThreshold ? 1.0 : 0.0;

				float normalEdge = edgeNormal * _NormalEdgeWeight;
				return max(edgeDepth, normalEdge);
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float dilate = max(_MaskDilatePixels, _Scale);
				float hoverMask = SampleNeighborhood(_HoverOutlineMask, sampler_HoverOutlineMask, input.uv, dilate, true);
				float questMask = SampleNeighborhood(_QuestOutlineMask, sampler_QuestOutlineMask, input.uv, dilate, true);
				if (max(hoverMask, questMask) <= 0.001)
					discard;

				float sceneEdge = ComputeRoyStanEdge(input.uv, input.viewSpaceDir);
				float hoverEdge = max(
					ComputeMaskSilhouette(_HoverOutlineMask, sampler_HoverOutlineMask, input.uv, _Scale),
					hoverMask > 0.001 ? sceneEdge : 0.0);
				float questEdge = max(
					ComputeMaskSilhouette(_QuestOutlineMask, sampler_QuestOutlineMask, input.uv, _Scale),
					(questMask > 0.001 && hoverMask <= 0.001) ? sceneEdge : 0.0);
				float edge = max(hoverEdge, questEdge);
				clip(edge - 0.5);

				bool useHover = hoverEdge > 0.5;
				float pulseSpeed = useHover ? _PulseSpeed : _QuestPulseSpeed;
				float pulseAmount = useHover ? _PulseAmount : _QuestPulseAmount;
				float pulse = 1.0 + sin(_Time.y * pulseSpeed * 6.2831853) * pulseAmount;

				float4 color = useHover ? _OutlineColor : _QuestOutlineColor;
				float intensity = useHover ? _OutlineIntensity : _QuestOutlineIntensity;
				float hdr = useHover ? _HdrBoost : _QuestHdrBoost;
				float alpha = saturate(color.a * intensity * pulse);
				float3 rgb = color.rgb * hdr * pulse;
				return half4(rgb, alpha);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
