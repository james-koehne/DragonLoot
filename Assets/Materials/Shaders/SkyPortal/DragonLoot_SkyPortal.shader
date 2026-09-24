// Sky Portal — cookie-masked opening with procedural stylized zenith sky (day/night via TimeOfDay globals).
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
		_SkyExposure("Sky Exposure", Range(0, 8)) = 1
		_SkyRotation("Sky Rotation", Range(0, 360)) = 0
		_SkyIntensity("Sky Intensity", Range(0, 10)) = 1
		[HDR] _SkyTint("Sky Tint", Color) = (1, 1, 1, 1)
		_HorizonLift("Horizon Lift", Range(0, 1)) = 0.2

		[Header(Day Colors)]
		[HDR] _DayZenithColor("Day Zenith", Color) = (0.25, 0.55, 0.95, 1)
		[HDR] _DayUpperColor("Day Upper", Color) = (0.45, 0.7, 0.98, 1)
		[HDR] _DayLowerColor("Day Lower", Color) = (0.7, 0.85, 1.0, 1)

		[Header(Sunset Colors)]
		[HDR] _SunsetZenithColor("Sunset Zenith", Color) = (0.35, 0.25, 0.55, 1)
		[HDR] _SunsetUpperColor("Sunset Upper", Color) = (0.95, 0.4, 0.55, 1)
		[HDR] _SunsetLowerColor("Sunset Lower", Color) = (1.0, 0.55, 0.3, 1)

		[Header(Night Colors)]
		[HDR] _NightZenithColor("Night Zenith", Color) = (0.02, 0.03, 0.12, 1)
		[HDR] _NightUpperColor("Night Upper", Color) = (0.04, 0.05, 0.18, 1)
		[HDR] _NightLowerColor("Night Lower", Color) = (0.08, 0.07, 0.2, 1)

		[Header(Sun)]
		[HDR] _SunColor("Sun Color", Color) = (1.0, 0.92, 0.7, 1)
		_SunSize("Sun Size", Range(0.001, 0.2)) = 0.035
		_SunGlow("Sun Glow", Range(0, 8)) = 2.5
		_SunGlowPower("Sun Glow Power", Range(0.5, 16)) = 6

		[Header(Moon)]
		[HDR] _MoonColor("Moon Color", Color) = (0.75, 0.82, 1.0, 1)
		_MoonSize("Moon Size", Range(0.001, 0.2)) = 0.028
		_MoonGlow("Moon Glow", Range(0, 4)) = 1.2
		_MoonGlowPower("Moon Glow Power", Range(0.5, 16)) = 5

		[Header(Stars)]
		_StarDensity("Star Density", Range(0, 1)) = 0.55
		_StarBrightness("Star Brightness", Range(0, 4)) = 1.4
		_StarTwinkle("Star Twinkle", Range(0, 1)) = 0.35

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

			float _DragonLoot_TimeOfDay;
			float3 _DragonLoot_SunDirectionWS;
			float3 _DragonLoot_MoonDirectionWS;
			float _DragonLoot_DayFactor;
			float _DragonLoot_NightFactor;
			float _DragonLoot_SunsetFactor;

			CBUFFER_START(UnityPerMaterial)
				float4 _TextureMask_ST;
				float4 _NoiseTexture_ST;
				half _Feather;
				half _SkyExposure;
				half _SkyRotation;
				half _SkyIntensity;
				half4 _SkyTint;
				half _HorizonLift;
				half4 _DayZenithColor;
				half4 _DayUpperColor;
				half4 _DayLowerColor;
				half4 _SunsetZenithColor;
				half4 _SunsetUpperColor;
				half4 _SunsetLowerColor;
				half4 _NightZenithColor;
				half4 _NightUpperColor;
				half4 _NightLowerColor;
				half4 _SunColor;
				half _SunSize;
				half _SunGlow;
				half _SunGlowPower;
				half4 _MoonColor;
				half _MoonSize;
				half _MoonGlow;
				half _MoonGlowPower;
				half _StarDensity;
				half _StarBrightness;
				half _StarTwinkle;
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

			half3 GradientSkyColor(half elevation, half3 zenith, half3 upper, half3 lower)
			{
				// Cave openings look mostly upward: bias blend toward zenith/upper.
				half zenithWeight = smoothstep(0.35h, 0.95h, elevation);
				half upperWeight = smoothstep(0.0h, 0.55h, elevation);
				half3 mid = lerp(lower, upper, upperWeight);
				return lerp(mid, zenith, zenithWeight);
			}

			half Hash11(float p)
			{
				p = frac(p * 0.1031);
				p *= p + 33.33;
				p *= p + p;
				return frac(p);
			}

			half Hash31(float3 p)
			{
				float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
				p3 += dot(p3, p3.yzx + 33.33);
				return frac((p3.x + p3.y) * p3.z);
			}

			half3 SampleStars(float3 skyDirectionWS, half nightFactor)
			{
				if (nightFactor < 0.01h || _StarBrightness < 0.001h)
					return 0;

				// Dense cells; each occupied cell gets one star at a jittered direction.
				// Brightness uses fwidth so the disk is ~1 screen pixel.
				const float starGridScale = 280.0;
				float3 cell = floor(skyDirectionWS * starGridScale);
				half rnd = Hash31(cell);
				half threshold = 1.0h - _StarDensity * 0.22h;
				if (rnd < threshold)
					return 0;

				float3 jitter = float3(
					Hash31(cell + float3(17.1, 0.0, 0.0)),
					Hash31(cell + float3(0.0, 31.7, 0.0)),
					Hash31(cell + float3(0.0, 0.0, 47.3)));
				float3 starDir = normalize(cell + jitter * 0.85);
				half cosAngle = saturate(dot(skyDirectionWS, starDir));

				// Change in cos(angle) across one pixel ≈ angular size of one pixel.
				half pixelWidth = max(fwidth(cosAngle), 1e-5h);
				half sparkle = smoothstep(1.0h - pixelWidth, 1.0h, cosAngle);

				half twinkle = 1.0h;
				if (_StarTwinkle > 0.001h)
				{
					half phase = Hash11(rnd * 17.13 + _DragonLoot_TimeOfDay * 6.0);
					twinkle = lerp(1.0h, 0.55h + 0.45h * phase, _StarTwinkle);
				}

				half elevationFade = smoothstep(0.05h, 0.35h, skyDirectionWS.y);
				return sparkle * twinkle * _StarBrightness * nightFactor * elevationFade;
			}

			half SoftCelestialDisk(float3 skyDirectionWS, float3 celestialDirWS, half size, half glow, half glowPower)
			{
				if (celestialDirWS.y <= 0.0)
					return 0;

				half cosAngle = saturate(dot(skyDirectionWS, normalize(celestialDirWS)));
				half angular = acos(cosAngle);
				half disk = 1.0h - smoothstep(0.0h, size, angular);
				half halo = pow(saturate(1.0h - angular / max(size * 6.0h, 0.001h)), glowPower) * glow;
				return disk * 2.0h + halo;
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
				skyDirectionWS = normalize(skyDirectionWS);

				half elevation = saturate(skyDirectionWS.y);
				// Low Horizon Lift keeps cave views zenith-heavy when looking less straight up.
				half elevForGradient = saturate(lerp(elevation, pow(elevation, 0.65h), _HorizonLift));

				half dayFactor = saturate(_DragonLoot_DayFactor);
				half nightFactor = saturate(_DragonLoot_NightFactor);
				half sunsetFactor = saturate(_DragonLoot_SunsetFactor);
				// Stars only in deep night — not during dusk/dawn when nightFactor is still partial.
				half starNightFactor = smoothstep(0.65h, 0.95h, nightFactor);

				half3 daySky = GradientSkyColor(elevForGradient, _DayZenithColor.rgb, _DayUpperColor.rgb, _DayLowerColor.rgb);
				half3 sunsetSky = GradientSkyColor(elevForGradient, _SunsetZenithColor.rgb, _SunsetUpperColor.rgb, _SunsetLowerColor.rgb);
				half3 nightSky = GradientSkyColor(elevForGradient, _NightZenithColor.rgb, _NightUpperColor.rgb, _NightLowerColor.rgb);

				half3 sky = lerp(nightSky, daySky, dayFactor);
				sky = lerp(sky, sunsetSky, sunsetFactor);

				float3 sunDir = length(_DragonLoot_SunDirectionWS) > 0.001 ? normalize(_DragonLoot_SunDirectionWS) : float3(0, 1, 0);
				float3 moonDir = length(_DragonLoot_MoonDirectionWS) > 0.001 ? normalize(_DragonLoot_MoonDirectionWS) : float3(0, -1, 0);

				half sunAmount = SoftCelestialDisk(skyDirectionWS, sunDir, _SunSize, _SunGlow, _SunGlowPower);
				sky += _SunColor.rgb * sunAmount * dayFactor;

				half moonAmount = SoftCelestialDisk(skyDirectionWS, moonDir, _MoonSize, _MoonGlow, _MoonGlowPower);
				sky += _MoonColor.rgb * moonAmount * nightFactor;

				half sunGlare = SoftCelestialDisk(skyDirectionWS, sunDir, _SunSize * 2.0h, 1.0h, 2.0h);
				half moonGlare = SoftCelestialDisk(skyDirectionWS, moonDir, _MoonSize * 2.0h, 1.0h, 2.0h);
				half starMask = saturate(1.0h - sunGlare - moonGlare);
				sky += SampleStars(skyDirectionWS, starNightFactor) * starMask;

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
