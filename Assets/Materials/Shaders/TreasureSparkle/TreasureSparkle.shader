Shader "DragonLoot/Treasure Sparkle"
{
	Properties
	{
		[HDR] _SparkleColor ("Sparkle Color", Color) = (2.5, 2.1, 1.2, 1)
		_SparkleEnable ("Enable", Float) = 1
		_SparkleCellSize ("Cell Size", Float) = 0.08
		_SparkleDensity ("Density", Range(0.001, 1)) = 0.85
		_SparkleLayerCount ("Layer Count", Range(1, 3)) = 2
		_SparkleLayerScale ("Layer Scale", Range(1.2, 3)) = 1.7
		_SparkleMinSize ("Min Size", Range(0.25, 2)) = 0.85
		_SparkleMaxSize ("Max Size", Range(0.25, 2)) = 1.15
		_SparkleSpotFalloff ("Spot Falloff", Range(0.5, 4)) = 1.6
		_SparkleScreenSpaceBlend ("Screen Space Blend", Range(0, 1)) = 1
		_SparkleTargetPixelSize ("Target Pixel Size", Range(0.25, 6)) = 0.9
		_SparkleScreenDensityBlend ("Screen Density Blend", Range(0, 1)) = 1
		_SparkleScreenDensityRefPixels ("Screen Density Ref Pixels", Range(2, 48)) = 22
		_SparkleGlintTex ("Glint Texture", 2D) = "white" {}
		_SparkleUseGlintTex ("Use Glint Texture", Float) = 0
		_SparkleGlintTexBlend ("Glint Texture Blend", Range(0, 1)) = 1
		_SparkleGlintTexSize ("Glint Texture Size", Range(0.25, 12)) = 1.1
		_SparkleBaseGlint ("Base Glint", Range(0, 8)) = 0.03
		_SparkleLitBoost ("Lit Boost", Range(0, 16)) = 6
		_SparkleBrightnessJitter ("Brightness Jitter", Range(0, 1)) = 0.25
		_SparkleCoreHotness ("Core Hotness", Range(0, 1)) = 0.75
		_SparkleCoinFacetJitter ("Coin Facet Jitter", Range(0, 1.5)) = 0.95
		_SparkleCoinFacingThreshold ("Coin Facing Threshold", Range(0, 1)) = 0.05
		_SparkleCoinFacingPower ("Coin Facing Power", Range(0.5, 8)) = 1.25
		_SparkleViewCrossfade ("View Crossfade", Range(0.05, 0.9)) = 0.35
		_SparkleViewSwapStrength ("View Swap Strength", Range(0, 1)) = 0.9
		_SparkleViewSwapSharpness ("View Swap Sharpness", Range(1, 16)) = 6
		_SparkleViewSwapThreshold ("View Swap Threshold", Range(0, 0.95)) = 0.45
		_SparkleMetallicSharpness ("Metallic Sharpness", Range(2, 128)) = 64
		_SparkleLightKillStrength ("Light Kill Strength", Range(0, 1)) = 0.85
		_SparkleSoftLobeAmount ("Soft Lobe Amount", Range(0, 1)) = 0.1
		_SparkleSeed ("Seed", Float) = 17
		_SparkleAlignmentThreshold ("Alignment Threshold", Range(0, 1)) = 0.35
		_SparkleFadeAngle ("Fade Angle (rad)", Float) = 0.31
		_SparkleNearGlintStart ("Near Glint Start", Float) = 0.25
		_SparkleNearGlintFull ("Near Glint Full", Float) = 3
		_SparkleFarGlintStart ("Far Glint Start", Float) = 40
		_SparkleFarGlintEnd ("Far Glint End", Float) = 70
		_SparkleNearDensityScale ("Near Density Scale", Range(0, 1)) = 1
		_SparkleFarDensityScale ("Far Density Scale", Range(0, 2)) = 0.45
		_SparkleNearIntensityFloor ("Near Intensity Floor", Range(0, 0.5)) = 0
		_SparkleGemDensityMul ("Gem Density Mul", Range(1, 8)) = 2.5
		_SparkleArtifactDensityMul ("Artifact Density Mul", Range(1, 8)) = 3
		_SparkleFacingThreshold ("Facing Threshold", Range(0, 1)) = 0
		_SparkleSlopeLimit ("Slope Limit", Range(0, 1)) = 0
		_SparkleScreenEdgeFade ("Screen Edge Fade", Range(0, 0.25)) = 0.015
		_SparkleSilhouetteFade ("Silhouette Fade", Range(0, 1)) = 0.85
		_SparkleMaskErodePixels ("Mask Erode Pixels", Range(0, 3)) = 1
		_SparkleTwinkleStrength ("Twinkle Strength", Range(0, 1)) = 0.2
		_SparkleTwinkleSpeed ("Twinkle Speed", Float) = 0.85
		_SparkleTwinkleDesync ("Twinkle Desync", Range(0, 1)) = 0.85
		_SparkleBloomContribution ("Bloom Contribution", Range(0, 8)) = 4
		_SparkleMaxActive ("Max Active", Range(0.001, 1)) = 0.95
		_SparkleDebugMode ("Debug Mode", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "Sparkle"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend One One

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
			#include "TreasureSparkleCommon.hlsl"

			TEXTURE2D(_TreasureSparkleMask);
			float4 _TreasureSparkleMask_TexelSize;
			TEXTURE2D(_SparkleGlintTex);
			SAMPLER(sampler_SparkleGlintTex);

			float4 _SparkleColor;
			float _SparkleEnable;
			float _SparkleCellSize;
			float _SparkleDensity;
			float _SparkleLayerCount;
			float _SparkleLayerScale;
			float _SparkleMinSize;
			float _SparkleMaxSize;
			float _SparkleSpotFalloff;
			float _SparkleScreenSpaceBlend;
			float _SparkleTargetPixelSize;
			float _SparkleScreenDensityBlend;
			float _SparkleScreenDensityRefPixels;
			float _SparkleUseGlintTex;
			float _SparkleGlintTexBlend;
			float _SparkleGlintTexSize;
			float _SparkleBaseGlint;
			float _SparkleLitBoost;
			float _SparkleBrightnessJitter;
			float _SparkleCoreHotness;
			float _SparkleCoinFacetJitter;
			float _SparkleCoinFacingThreshold;
			float _SparkleCoinFacingPower;
			float _SparkleViewCrossfade;
			float _SparkleViewSwapStrength;
			float _SparkleViewSwapSharpness;
			float _SparkleViewSwapThreshold;
			float _SparkleMetallicSharpness;
			float _SparkleLightKillStrength;
			float _SparkleSoftLobeAmount;
			float _SparkleSeed;
			float _SparkleAlignmentThreshold;
			float _SparkleFadeAngle;
			float _SparkleNearGlintStart;
			float _SparkleNearGlintFull;
			float _SparkleFarGlintStart;
			float _SparkleFarGlintEnd;
			float _SparkleNearDensityScale;
			float _SparkleFarDensityScale;
			float _SparkleNearIntensityFloor;
			float _SparkleGemDensityMul;
			float _SparkleArtifactDensityMul;
			float _SparkleFacingThreshold;
			float _SparkleSlopeLimit;
			float _SparkleScreenEdgeFade;
			float _SparkleSilhouetteFade;
			float _SparkleMaskErodePixels;
			float _SparkleTwinkleStrength;
			float _SparkleTwinkleSpeed;
			float _SparkleTwinkleDesync;
			float _SparkleBloomContribution;
			float _SparkleMaxActive;
			float _SparkleDebugMode;
			float4x4 _SparkleViewProj;
			float4x4 _SparkleInvViewProj;
			float3 _SparkleCameraPosition;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
				output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
				return output;
			}

			float SampleMask(float2 uv)
			{
				return SAMPLE_TEXTURE2D(_TreasureSparkleMask, sampler_LinearClamp, uv).r;
			}

			// Mask encodes treasure kind: pile 0.25, coin 0.5, gem 0.75, artifact 1.0.
			float SourceDensityMultiplier(float mask)
			{
				if (mask >= 0.875)
					return _SparkleArtifactDensityMul;
				if (mask >= 0.625)
					return _SparkleGemDensityMul;
				return 1.0;
			}

			// Erode the binary mask so glints stay inside the silhouette instead of flickering on the outline.
			float SampleMaskInterior(float2 uv)
			{
				float2 texel = _TreasureSparkleMask_TexelSize.xy;
				int erode = (int)clamp(round(_SparkleMaskErodePixels), 0, 3);
				float m = SampleMask(uv);
				if (erode <= 0)
					return m;

				[unroll]
				for (int i = 1; i <= 3; i++)
				{
					if (i > erode)
						break;
					float2 o = texel * i;
					m = min(m, SampleMask(uv + float2(o.x, 0)));
					m = min(m, SampleMask(uv - float2(o.x, 0)));
					m = min(m, SampleMask(uv + float2(0, o.y)));
					m = min(m, SampleMask(uv - float2(0, o.y)));
					m = min(m, SampleMask(uv + o));
					m = min(m, SampleMask(uv - o));
					m = min(m, SampleMask(uv + float2(o.x, -o.y)));
					m = min(m, SampleMask(uv + float2(-o.x, o.y)));
				}
				return m;
			}

			// Fade near depth/normal discontinuities (aliased pile edges against background).
			float SilhouetteStability(float rawDepth, float3 normalWS)
			{
				float strength = saturate(_SparkleSilhouetteFade);
				if (strength <= 1e-4)
					return 1.0;

				float eye = LinearEyeDepth(rawDepth, _ZBufferParams);
				float depthEdge = fwidth(eye) / max(eye * 0.015, 1e-4);
				float normalEdge = length(fwidth(normalWS)) * 2.5;
				float edge = saturate(max(depthEdge, normalEdge));
				return lerp(1.0, 1.0 - smoothstep(0.08, 0.55, edge), strength);
			}

			float3 ReconstructWorldPosition(float2 uv, float rawDepth)
			{
				#if UNITY_REVERSED_Z
				float deviceDepth = rawDepth;
				#else
				float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
				#endif

				float4 clipPos = float4(uv * 2.0 - 1.0, deviceDepth, 1.0);
				#if UNITY_UV_STARTS_AT_TOP
				clipPos.y = -clipPos.y;
				#endif

				float4 worldPos = mul(_SparkleInvViewProj, clipPos);
				return worldPos.xyz / max(worldPos.w, 1e-6);
			}

			float2 ProjectWorldToScreenUV(float3 positionWS)
			{
				float4 clip = mul(_SparkleViewProj, float4(positionWS, 1.0));
				float2 ndc = clip.xy / max(abs(clip.w), 1e-6);
				#if UNITY_UV_STARTS_AT_TOP
				ndc.y = -ndc.y;
				#endif
				return ndc * 0.5 + 0.5;
			}

			float3 EvaluateLayer(
				float2 pixelUV,
				float3 positionWS,
				float3 normalWS,
				float3 viewDirWS,
				float3 lightDirWS,
				float cellSize,
				float layerSeed,
				float densityScale,
				out float outSpot,
				out float outLight,
				out float outCoinFacing)
			{
				outSpot = 0;
				outLight = 0;
				outCoinFacing = 0;

				float3 scaled = positionWS / max(cellSize, 0.02);
				float3 cell = floor(scaled);
				float3 seedBase = cell + float3(layerSeed, layerSeed * 1.7, layerSeed * 2.3);

				float occupancy = TreasureSparkleHash13(seedBase);

				float worldPerPixel = max(length(fwidth(positionWS)), 1e-5);
				float pixelsPerCell = max(cellSize, 0.02) / worldPerPixel;
				float densRef = max(_SparkleScreenDensityRefPixels, 1.0);
				// Stronger than quadratic — distant piles pack many cells into few pixels.
				float densMul = saturate(pixelsPerCell / densRef);
				densMul = densMul * densMul * densMul;
				densMul = lerp(1.0, densMul, saturate(_SparkleScreenDensityBlend));

				float coverageCap = min(_SparkleDensity * densityScale * densMul, _SparkleMaxActive);
				if (occupancy > coverageCap)
					return 0;

				float3 rnd = TreasureSparkleHash33(seedBase + 19.19);
				float2 rnd2 = TreasureSparkleHash23(seedBase + 7.7);

				float3 glintCenter = rnd * 0.85 + 0.075;
				float3 centerWS = (cell + glintCenter) * max(cellSize, 0.02);

				float4 centerClip = mul(_SparkleViewProj, float4(centerWS, 1.0));
				if (centerClip.w <= 1e-5)
					return 0;

				float2 centerUV = ProjectWorldToScreenUV(centerWS);
				float2 resolution = max(_TreasureSparkleMask_TexelSize.zw, float2(1.0, 1.0));
				float2 deltaPx = (pixelUV - centerUV) * resolution;

				float sizeJitter = lerp(_SparkleMinSize, _SparkleMaxSize, rnd2.x);
				float useTex = (_SparkleUseGlintTex > 0.5) ? saturate(_SparkleGlintTexBlend) : 0.0;
				float procDiameter = max(_SparkleTargetPixelSize, 0.25) * sizeJitter;
				float texDiameter = max(_SparkleGlintTexSize, 0.25) * sizeJitter;
				float stampDiameterPx = max(lerp(procDiameter, texDiameter, useTex), 0.25);
				float2 stampUV = deltaPx / stampDiameterPx + 0.5;

				float spot = 0;
				float3 stampTint = 1;
				if (useTex > 0.5)
				{
					// Flat screen-space sprite stamp — axis-aligned, fixed pixel size.
					if (stampUV.x < 0.0 || stampUV.x > 1.0 || stampUV.y < 0.0 || stampUV.y > 1.0)
						return 0;
					float4 texSample = SAMPLE_TEXTURE2D_LOD(_SparkleGlintTex, sampler_SparkleGlintTex, stampUV, 0);
					spot = max(texSample.a, max(texSample.r, max(texSample.g, texSample.b)));
					stampTint = max(texSample.rgb, texSample.a);
				}
				else
				{
					float glintDist = length(deltaPx) / max(stampDiameterPx * 0.5, 1e-5);
					spot = saturate(1.0 - glintDist);
					spot = pow(spot, max(_SparkleSpotFalloff, 0.5));
				}

				outSpot = spot;
				if (spot <= 1e-4)
					return 0;

				float2 fromCenter = stampUV - 0.5;
				float coreMask = saturate(1.0 - length(fromCenter) * 3.5);
				coreMask *= spot;

				float3 facetJitter = TreasureSparkleDirection(rnd);
				float3 facetNormal = normalize(normalWS + facetJitter * _SparkleCoinFacetJitter);

				float fadeWidth = max(_SparkleViewCrossfade, 0.05);
				float swapStrength = saturate(_SparkleViewSwapStrength);

				float3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
				float3 reflectDir = reflect(-lightDirWS, facetNormal);
				float nh = saturate(dot(facetNormal, halfDir));
				float rv = saturate(dot(reflectDir, viewDirWS));
				float sharpness = max(_SparkleMetallicSharpness, 2.0);
				float spec = max(pow(nh, sharpness), pow(rv, sharpness * 0.85));
				float soft = pow(max(nh, rv), max(sharpness * 0.22, 1.5)) * _SparkleSoftLobeAmount;
				float lightResp = max(spec, soft);

				float alignStart = _SparkleAlignmentThreshold;
				float alignEnd = saturate(alignStart + max(_SparkleFadeAngle, 0.05) + fadeWidth * 0.35);
				lightResp *= smoothstep(max(0.0, alignStart - fadeWidth * 0.15), alignEnd, max(nh, rv));
				outLight = lightResp;

				float3 preferView = TreasureSparkleDirection(float3(rnd2.y, rnd.z, rnd.x));
				float preferDot = saturate(dot(preferView, viewDirWS));
				float gateThresh = _SparkleViewSwapThreshold * swapStrength;
				float preferLobe = smoothstep(gateThresh, gateThresh + fadeWidth, preferDot);
				preferLobe = pow(preferLobe, max(_SparkleViewSwapSharpness, 1.0));
				float viewMod = lerp(1.0, preferLobe, swapStrength * 0.45);
				outCoinFacing = viewMod;

				float litTerm = _SparkleBaseGlint + _SparkleLitBoost * lightResp;
				float keep = lerp(1.0, saturate(lightResp * 1.5), _SparkleLightKillStrength);
				if (litTerm * keep <= 1e-5)
					return 0;

				float brightJitter = lerp(1.0 - _SparkleBrightnessJitter, 1.0 + _SparkleBrightnessJitter, rnd.x);
				float phase = rnd2.y * 6.2831853 * max(_SparkleTwinkleDesync, 0.01);
				float twinkle = sin(_TimeParameters.x * _SparkleTwinkleSpeed + phase) * 0.5 + 0.5;
				twinkle = lerp(1.0, twinkle, saturate(_SparkleTwinkleStrength));

				float intensity = spot * litTerm * keep * viewMod * brightJitter * twinkle;
				float3 whiteHot = float3(1.25, 1.18, 1.05);
				float coreMix = saturate(_SparkleCoreHotness) * coreMask;
				float peak = max(max(_SparkleColor.r, _SparkleColor.g), _SparkleColor.b);
				float3 tint = lerp(_SparkleColor.rgb, whiteHot * peak, coreMix) * stampTint;
				return tint * intensity;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				if (_SparkleEnable < 0.5)
					return half4(0, 0, 0, 0);

				float2 uv = input.uv;
				float mask = SampleMask(uv);

				int debugMode = (int)round(_SparkleDebugMode);
				if (debugMode == 1)
					return half4(mask.xxx, 1);

				if (mask < 0.01)
					return half4(0, 0, 0, 0);

				float rawDepth = SampleSceneDepth(uv);
				#if UNITY_REVERSED_Z
				if (rawDepth <= 1e-5)
					return half4(0, 0, 0, 0);
				#else
				if (rawDepth >= 0.99999)
					return half4(0, 0, 0, 0);
				#endif

				float3 positionWS = ReconstructWorldPosition(uv, rawDepth);
				float3 normalSample = SampleSceneNormals(uv);
				float normalLen = length(normalSample);
				float3 normalWS = normalLen > 1e-3 ? normalSample / normalLen : float3(0, 1, 0);
				float3 viewDirWS = normalize(_SparkleCameraPosition - positionWS);
				float camDist = distance(_SparkleCameraPosition, positionWS);

				float edgeFade = TreasureSparkleScreenEdgeFade(uv, _SparkleScreenEdgeFade);
				float nearFade = smoothstep(_SparkleNearGlintStart, _SparkleNearGlintFull, camDist);
				float farFade = 1.0 - smoothstep(_SparkleFarGlintStart, _SparkleFarGlintEnd, camDist);
				float distFade = saturate(nearFade * farFade);
				distFade = max(distFade, _SparkleNearIntensityFloor * farFade);

				// Extra occupancy thin with distance (on top of screen-density in EvaluateLayer).
				float densityScale = lerp(_SparkleNearDensityScale, 1.0, nearFade);
				densityScale = lerp(densityScale, _SparkleFarDensityScale, saturate(nearFade * farFade));
				float distThin = 1.0 / max(1.0 + camDist * 0.04, 1.0);
				densityScale *= distThin;
				densityScale *= SourceDensityMultiplier(mask);

				float facing = normalLen > 1e-3 ? saturate(dot(normalWS, viewDirWS)) : 1.0;
				if (facing < _SparkleFacingThreshold)
					return half4(0, 0, 0, 0);

				if (_SparkleSlopeLimit > 1e-4)
				{
					float upward = saturate(normalWS.y);
					if (upward < _SparkleSlopeLimit)
						return half4(0, 0, 0, 0);
				}

				Light mainLight = GetMainLight();
				float3 lightDirWS = mainLight.direction;
				if (dot(lightDirWS, lightDirWS) < 1e-6)
					lightDirWS = float3(0.35, 0.85, 0.35);

				if (debugMode == 5)
				{
					float heat = mask * distFade * densityScale * _SparkleDensity;
					return half4(heat, heat * 0.4, 0.08, 1);
				}

				if (debugMode == 2)
				{
					float cellSizeDbg = max(_SparkleCellSize, 0.02);
					float3 cellUV = frac(positionWS / cellSizeDbg);
					float2 grid = abs(cellUV.xz - 0.5) * 2.0;
					float gridEdge = max(grid.x, grid.y);
					float cellVis = smoothstep(0.9, 1.0, gridEdge);
					return half4(cellVis, mask * 0.35, 0.1, 1);
				}

				int layers = (int)clamp(round(_SparkleLayerCount), 1, 3);
				float3 rgb = 0;
				float debugSpot = 0;
				float debugLight = 0;
				float debugCoin = 0;
				float cellSize = max(_SparkleCellSize, 0.02);

				[unroll]
				for (int layer = 0; layer < 3; layer++)
				{
					if (layer >= layers)
						break;

					float scale = pow(max(_SparkleLayerScale, 1.2), layer);
					float layerSeed = _SparkleSeed + layer * 37.13;
					float spot, light, coin;
					float3 layerRgb = EvaluateLayer(
						uv,
						positionWS,
						normalWS,
						viewDirWS,
						lightDirWS,
						cellSize * scale,
						layerSeed,
						densityScale / scale,
						spot,
						light,
						coin);
					rgb += layerRgb * (layer == 0 ? 1.0 : 0.65);
					debugSpot = max(debugSpot, spot);
					debugLight = max(debugLight, light);
					debugCoin = max(debugCoin, coin);
				}

				if (debugMode == 3)
					return half4(debugSpot, debugSpot * 0.55, 0.1, 1);

				if (debugMode == 4)
					return half4(debugLight, debugCoin, debugSpot, 1);

				float fade = distFade * edgeFade * max(facing, 0.35);
				rgb *= fade * _SparkleBloomContribution;

				if (dot(rgb, rgb) <= 1e-8)
					return half4(0, 0, 0, 0);

				return half4(rgb, 0);
			}
			ENDHLSL
		}

		Pass
		{
			Name "UpsampleAdd"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend One One

			HLSLPROGRAM
			#pragma vertex VertUpsample
			#pragma fragment FragUpsample
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_BlitTexture);
			float4 _BlitScaleBias;

			struct AttributesUpsample
			{
				uint vertexID : SV_VertexID;
			};

			struct VaryingsUpsample
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			VaryingsUpsample VertUpsample(AttributesUpsample input)
			{
				VaryingsUpsample output;
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
				float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
				output.uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
				return output;
			}

			half4 FragUpsample(VaryingsUpsample input) : SV_Target
			{
				return half4(SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv).rgb, 0);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
