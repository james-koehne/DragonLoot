// Soft Volume — fake mist / smoke / god-ray hull for a semi-transparent mesh.
Shader "DragonLoot/SoftVolume"
{
	Properties
	{
		[Header(Color)]
		[HDR] _Color("Core Color", Color) = (0.55, 0.72, 0.85, 1)
		[HDR] _ColorMid("Mid Color", Color) = (0.45, 0.62, 0.78, 1)
		[HDR] _ColorRim("Rim Color", Color) = (0.75, 0.88, 1.0, 1)
		_Exposure("Exposure", Range(0, 8)) = 1.2
		_Saturation("Saturation", Range(0, 2)) = 1
		_Alpha("Alpha", Range(0, 2)) = 0.55
		_AlphaPow("Alpha Power", Range(0.25, 4)) = 1.1

		[Header(Density Thickness)]
		_Density("Density", Range(0, 4)) = 1.15
		_ThicknessPower("Thickness Power", Range(0.25, 8)) = 2
		[Toggle(_SOFTVOLUME_THICKNESS_INVERT)] _ThicknessInvert("Thickness Invert", Float) = 0
		_FresnelPower("Fresnel Power", Range(0.25, 8)) = 1.5
		_RimBoost("Rim Boost", Range(0, 4)) = 1.25
		_CenterBoost("Center Boost", Range(0, 4)) = 0.35

		[Header(Noise A)]
		_NoiseTexA("Noise A", 2D) = "gray" {}
		[KeywordEnum(UV, Object, World)] _NoiseSpaceA("Noise A Space", Float) = 2
		_NoiseScaleA("Noise A Scale", Float) = 0.08
		_NoiseScrollA("Noise A Scroll", Vector) = (0.015, 0.008, 0.01, 0)
		_NoiseContrastA("Noise A Contrast", Range(0.1, 8)) = 1.4
		_NoiseRemapMinA("Noise A Remap Min", Range(0, 1)) = 0.15
		_NoiseRemapMaxA("Noise A Remap Max", Range(0, 1)) = 0.85
		_NoiseStrengthA("Noise A Strength", Range(0, 2)) = 1

		[Header(Noise B)]
		[Toggle(_SOFTVOLUME_NOISE_B)] _NoiseBEnabled("Enable Noise B", Float) = 1
		_NoiseTexB("Noise B", 2D) = "gray" {}
		[KeywordEnum(UV, Object, World)] _NoiseSpaceB("Noise B Space", Float) = 2
		_NoiseScaleB("Noise B Scale", Float) = 0.14
		_NoiseScrollB("Noise B Scroll", Vector) = (-0.01, 0.012, -0.006, 0)
		_NoiseContrastB("Noise B Contrast", Range(0.1, 8)) = 1.2
		_NoiseRemapMinB("Noise B Remap Min", Range(0, 1)) = 0.2
		_NoiseRemapMaxB("Noise B Remap Max", Range(0, 1)) = 0.9
		_NoiseStrengthB("Noise B Strength", Range(0, 2)) = 0.75
		[KeywordEnum(Lerp, Multiply)] _NoiseBlend("Noise Blend", Float) = 1
		_NoiseWarpStrength("Noise Warp Strength", Range(0, 1)) = 0.15

		[Header(God Rays)]
		[Toggle(_SOFTVOLUME_RAYS)] _RayEnabled("Enable God Rays", Float) = 0
		[HDR] _RayColor("Ray Color", Color) = (1.0, 0.92, 0.75, 1)
		_RayDirection("Ray Direction", Vector) = (0, 1, 0, 0)
		_RaySharpness("Ray Sharpness", Range(0.5, 64)) = 8
		_RayDensity("Ray Density", Range(0.1, 32)) = 4
		_RayIntensity("Ray Intensity", Range(0, 4)) = 1.2
		_RayNoiseInfluence("Ray Noise Influence", Range(0, 1)) = 0.65

		[Header(Height Falloff)]
		[Toggle(_SOFTVOLUME_HEIGHT)] _HeightEnabled("Enable Height Falloff", Float) = 1
		[KeywordEnum(World, Object)] _HeightSpace("Height Space", Float) = 0
		_HeightMin("Height Min", Float) = 0
		_HeightMax("Height Max", Float) = 4
		_HeightSoftness("Height Softness", Range(0.01, 2)) = 0.35

		[Header(Radial Falloff)]
		[Toggle(_SOFTVOLUME_RADIAL)] _RadialEnabled("Enable Radial Falloff", Float) = 0
		_RadialCenter("Radial Center OS", Vector) = (0, 0, 0, 0)
		_RadialRadius("Radial Radius", Float) = 2
		_RadialSoftness("Radial Softness", Range(0.01, 2)) = 0.4

		[Header(Geometry Soften)]
		[Toggle(_SOFTVOLUME_GEOMETRY_SOFTEN)] _GeometrySoftenEnabled("Soften Near Geometry", Float) = 1
		_GeometrySoftenDistance("Geometry Soften Distance", Float) = 0.5
		_GeometrySoftenPower("Geometry Soften Power", Range(0.25, 8)) = 1
		_GeometrySoftenStrength("Geometry Soften Strength", Range(0, 1)) = 1

		[Header(Camera Fade)]
		_CameraFadeStart("Camera Fade Start", Float) = 0.35
		_CameraFadeEnd("Camera Fade End", Float) = 1.25

		[Header(Motion)]
		_PulseSpeed("Pulse Speed", Float) = 0.35
		_PulseAmount("Pulse Amount", Range(0, 1)) = 0.12
		_GlobalScroll("Global Scroll", Vector) = (0.01, 0.005, 0.008, 0)
		_TimeScale("Time Scale", Float) = 1
		_VertexJitter("Vertex Jitter", Range(0, 0.25)) = 0

		[Header(Render)]
		[Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
		[Enum(Off,0,Front,1,Back,2)] _Cull("Cull", Float) = 0
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
		_FogInfluence("Fog Influence", Range(0, 1)) = 0.65
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
			Name "SoftVolume"
			Tags { "LightMode" = "UniversalForward" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite Off
			Cull [_Cull]
			ZTest [_ZTest]

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing

			#pragma shader_feature_local _SOFTVOLUME_THICKNESS_INVERT
			#pragma shader_feature_local _SOFTVOLUME_NOISE_B
			#pragma shader_feature_local _SOFTVOLUME_RAYS
			#pragma shader_feature_local _SOFTVOLUME_HEIGHT
			#pragma shader_feature_local _SOFTVOLUME_RADIAL
			#pragma shader_feature_local _SOFTVOLUME_GEOMETRY_SOFTEN

			#pragma shader_feature_local _NOISESPACEA_UV _NOISESPACEA_OBJECT _NOISESPACEA_WORLD
			#pragma shader_feature_local _NOISESPACEB_UV _NOISESPACEB_OBJECT _NOISESPACEB_WORLD
			#pragma shader_feature_local _NOISEBLEND_LERP _NOISEBLEND_MULTIPLY
			#pragma shader_feature_local _HEIGHTSPACE_WORLD _HEIGHTSPACE_OBJECT

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

			TEXTURE2D(_NoiseTexA);
			SAMPLER(sampler_NoiseTexA);
			TEXTURE2D(_NoiseTexB);
			SAMPLER(sampler_NoiseTexB);

			CBUFFER_START(UnityPerMaterial)
				float4 _NoiseTexA_ST;
				float4 _NoiseTexB_ST;
				half4 _Color;
				half4 _ColorMid;
				half4 _ColorRim;
				half _Exposure;
				half _Saturation;
				half _Alpha;
				half _AlphaPow;
				half _Density;
				half _ThicknessPower;
				half _FresnelPower;
				half _RimBoost;
				half _CenterBoost;
				half _NoiseScaleA;
				float4 _NoiseScrollA;
				half _NoiseContrastA;
				half _NoiseRemapMinA;
				half _NoiseRemapMaxA;
				half _NoiseStrengthA;
				half _NoiseScaleB;
				float4 _NoiseScrollB;
				half _NoiseContrastB;
				half _NoiseRemapMinB;
				half _NoiseRemapMaxB;
				half _NoiseStrengthB;
				half _NoiseWarpStrength;
				half4 _RayColor;
				float4 _RayDirection;
				half _RaySharpness;
				half _RayDensity;
				half _RayIntensity;
				half _RayNoiseInfluence;
				half _HeightMin;
				half _HeightMax;
				half _HeightSoftness;
				float4 _RadialCenter;
				half _RadialRadius;
				half _RadialSoftness;
				half _GeometrySoftenDistance;
				half _GeometrySoftenPower;
				half _GeometrySoftenStrength;
				half _CameraFadeStart;
				half _CameraFadeEnd;
				half _PulseSpeed;
				half _PulseAmount;
				float4 _GlobalScroll;
				half _TimeScale;
				half _VertexJitter;
				half _FogInfluence;
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
				float3 positionOS : TEXCOORD1;
				float3 normalWS : TEXCOORD2;
				float2 uv : TEXCOORD3;
				half fogFactor : TEXCOORD4;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			half Remap01(half value, half minValue, half maxValue)
			{
				half range = max(maxValue - minValue, 1e-4h);
				return saturate((value - minValue) / range);
			}

			half ApplyContrast(half value, half contrast)
			{
				half mid = 0.5h;
				return saturate((value - mid) * contrast + mid);
			}

			half3 ApplySaturation(half3 color, half saturation)
			{
				half luma = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
				return lerp(half3(luma, luma, luma), color, saturation);
			}

			float2 BuildNoiseUV(float2 uv, float3 positionOS, float3 positionWS, float scale, float3 scroll, float time, bool useObject, bool useWorld)
			{
				float2 baseUV;
				if (useWorld)
				{
					baseUV = positionWS.xz;
				}
				else if (useObject)
				{
					baseUV = positionOS.xz;
				}
				else
				{
					baseUV = uv;
				}

				return baseUV * scale + scroll.xy * time + scroll.z * time * 0.37;
			}

			half SampleNoiseLayer(
				TEXTURE2D_PARAM(noiseTex, noiseSampler),
				float2 uv,
				float3 positionOS,
				float3 positionWS,
				float scale,
				float3 scroll,
				float time,
				half contrast,
				half remapMin,
				half remapMax,
				half strength,
				float2 warpOffset,
				bool useObject,
				bool useWorld)
			{
				float2 noiseUV = BuildNoiseUV(uv, positionOS, positionWS, scale, scroll, time, useObject, useWorld);
				noiseUV += warpOffset;
				half raw = SAMPLE_TEXTURE2D(noiseTex, noiseSampler, noiseUV).r;
				half shaped = ApplyContrast(Remap01(raw, remapMin, remapMax), contrast);
				return lerp(1.0h, shaped, strength);
			}

			half ApplyGeometrySoften(half alpha, float3 positionWS, float4 positionCS)
			{
				if (_GeometrySoftenDistance <= 0.0001h || _GeometrySoftenStrength <= 0.0001h)
					return alpha;

				float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
				float sceneRawDepth = SampleSceneDepth(normalizedScreenSpaceUV);
				float volumeEyeDepth = LinearEyeDepth(positionWS, GetWorldToViewMatrix());
				float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);

				float sceneInFront = volumeEyeDepth - sceneEyeDepth;
				if (sceneInFront <= 0.0)
					return alpha;

				half depthFade = 1.0h - saturate(sceneInFront / _GeometrySoftenDistance);
				depthFade = pow(max(depthFade, 0.0h), _GeometrySoftenPower);
				depthFade = lerp(1.0h, depthFade, _GeometrySoftenStrength);
				return alpha * depthFade;
			}

			half ApplyCameraFade(half alpha, float3 positionWS)
			{
				float cameraDistance = distance(GetCurrentViewPosition(), positionWS);
				half fade = saturate((cameraDistance - _CameraFadeStart) / max(_CameraFadeEnd - _CameraFadeStart, 1e-4h));
				return alpha * fade;
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 positionOS = input.positionOS.xyz;
				if (_VertexJitter > 0.0001h)
				{
					float t = _Time.y * _TimeScale;
					float jitter = sin(dot(positionOS, float3(12.9898, 78.233, 37.719)) + t * 3.1) * _VertexJitter;
					positionOS += input.normalOS * jitter;
				}

				VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
				VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

				output.positionCS = positionInputs.positionCS;
				output.positionWS = positionInputs.positionWS;
				output.positionOS = positionOS;
				output.normalWS = normalInputs.normalWS;
				output.uv = TRANSFORM_TEX(input.uv, _NoiseTexA);
				output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float time = _Time.y * _TimeScale;
				float3 globalScroll = _GlobalScroll.xyz * time;

				half3 normalWS = normalize(input.normalWS);
				half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
				half ndv = saturate(dot(normalWS, viewDirWS));

				half fresnel = pow(1.0h - ndv, _FresnelPower);
#if defined(_SOFTVOLUME_THICKNESS_INVERT)
				half thicknessTerm = pow(ndv, _ThicknessPower);
#else
				half thicknessTerm = pow(1.0h - ndv, _ThicknessPower);
#endif
				half opticalDepth = thicknessTerm * _Density;
				opticalDepth *= 1.0h + fresnel * _RimBoost;
				opticalDepth *= 1.0h + (1.0h - fresnel) * _CenterBoost;

				float2 uv = input.uv + globalScroll.xy;

				bool noiseAObject = false;
				bool noiseAWorld = false;
#if defined(_NOISESPACEA_OBJECT)
				noiseAObject = true;
#elif defined(_NOISESPACEA_WORLD)
				noiseAWorld = true;
#endif

				half noiseA = SampleNoiseLayer(
					TEXTURE2D_ARGS(_NoiseTexA, sampler_NoiseTexA),
					uv,
					input.positionOS + globalScroll,
					input.positionWS + globalScroll,
					_NoiseScaleA,
					_NoiseScrollA.xyz,
					time,
					_NoiseContrastA,
					_NoiseRemapMinA,
					_NoiseRemapMaxA,
					_NoiseStrengthA,
					float2(0.0, 0.0),
					noiseAObject,
					noiseAWorld);

				half noiseCombined = noiseA;
				float2 warp = (noiseA * 2.0 - 1.0) * _NoiseWarpStrength * 0.15;

#if defined(_SOFTVOLUME_NOISE_B)
				bool noiseBObject = false;
				bool noiseBWorld = false;
#if defined(_NOISESPACEB_OBJECT)
				noiseBObject = true;
#elif defined(_NOISESPACEB_WORLD)
				noiseBWorld = true;
#endif
				half noiseB = SampleNoiseLayer(
					TEXTURE2D_ARGS(_NoiseTexB, sampler_NoiseTexB),
					uv,
					input.positionOS + globalScroll,
					input.positionWS + globalScroll,
					_NoiseScaleB,
					_NoiseScrollB.xyz,
					time,
					_NoiseContrastB,
					_NoiseRemapMinB,
					_NoiseRemapMaxB,
					_NoiseStrengthB,
					warp,
					noiseBObject,
					noiseBWorld);

#if defined(_NOISEBLEND_MULTIPLY)
				noiseCombined = noiseA * noiseB;
#else
				noiseCombined = lerp(noiseA, noiseB, 0.5h);
#endif
#endif

				half density = opticalDepth * noiseCombined;

#if defined(_SOFTVOLUME_HEIGHT)
				half heightCoord;
#if defined(_HEIGHTSPACE_OBJECT)
				heightCoord = input.positionOS.y;
#else
				heightCoord = input.positionWS.y;
#endif
				// Dense near Height Max (top), fades out toward Height Min (bottom).
				half heightRange = max(_HeightMax - _HeightMin, 1e-4h);
				half heightT = saturate((heightCoord - _HeightMin) / heightRange);
				half soft = max(_HeightSoftness, 1e-3h);
				half invT = 1.0h - heightT;
				half heightMask = 1.0h - smoothstep(1.0h - soft, 1.0h, invT);
				heightMask *= smoothstep(0.0h, soft, invT + soft);
				density *= heightMask;
#endif

#if defined(_SOFTVOLUME_RADIAL)
				float3 radialOffset = input.positionOS - _RadialCenter.xyz;
				half radialDist = length(radialOffset);
				half radialT = 1.0h - saturate(radialDist / max(_RadialRadius, 1e-4h));
				half radialMask = smoothstep(0.0h, _RadialSoftness, radialT);
				density *= radialMask;
#endif

				half pulse = 1.0h + sin(time * _PulseSpeed * 6.2831853h) * _PulseAmount;
				density *= pulse;

				half alpha = saturate(pow(max(density, 0.0h), _AlphaPow) * _Alpha);
#if defined(_SOFTVOLUME_GEOMETRY_SOFTEN)
				alpha = ApplyGeometrySoften(alpha, input.positionWS, input.positionCS);
#endif
				alpha = ApplyCameraFade(alpha, input.positionWS);
				clip(alpha - 0.0001h);

				half midBlend = saturate(fresnel * 0.65h + noiseCombined * 0.35h);
				half3 color = lerp(_Color.rgb, _ColorMid.rgb, midBlend);
				color = lerp(color, _ColorRim.rgb, fresnel);
				color = ApplySaturation(color, _Saturation) * _Exposure;

#if defined(_SOFTVOLUME_RAYS)
				float3 rayDir = normalize(_RayDirection.xyz + 1e-5);
				float3 axisRef = abs(rayDir.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
				float3 rayTangent = normalize(cross(rayDir, axisRef));
				float along = dot(input.positionWS, rayDir);
				float side = dot(input.positionWS, rayTangent);
				half rayBands = pow(saturate(sin(along * _RayDensity) * 0.5h + 0.5h), max(_RaySharpness * 0.12h, 0.5h));
				half rayMask = pow(saturate(1.0h - abs(sin(side * _RayDensity * 0.35h))), _RaySharpness * 0.2h);
				half viewAlign = pow(1.0h - saturate(abs(dot(viewDirWS, rayDir))), 0.75h);
				half rayNoise = lerp(1.0h, noiseCombined, _RayNoiseInfluence);
				half rays = rayMask * rayBands * rayNoise * viewAlign * _RayIntensity;
				color += _RayColor.rgb * rays;
				alpha = saturate(alpha + rays * 0.25h * _RayIntensity);
#endif

				half fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
				half3 fogged = MixFog(color, fogCoord);
				color = lerp(color, fogged, _FogInfluence);

				return half4(color, alpha);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
