#ifndef DRAGONLOOT_ENVIRONMENT_LIT_LIGHTING_INCLUDED
#define DRAGONLOOT_ENVIRONMENT_LIT_LIGHTING_INCLUDED

#include "EnvironmentLitInput.hlsl"
#include "StylizedLightingCommon.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    half4  tangentWS  : TEXCOORD2;
    float2 uv         : TEXCOORD3;
    half   fogFactor  : TEXCOORD4;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD5;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD7;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void EnvironmentLitInitializeBakedGIData(Varyings input, inout InputData inputData)
{
#if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
#if defined(LIGHTMAP_ON)
    inputData.bakedGI = max(inputData.bakedGI, SampleSHPixel(half3(0, 0, 0), inputData.normalWS));
#endif
}

void EnvironmentLitInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    EnvironmentLitInitializeBakedGIData(input, inputData);
}

Varyings EnvironmentLitVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = normalInputs.normalWS;

    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInputs.tangentWS, sign);
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(posInputs);
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    OUTPUT_SH4(posInputs.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(posInputs.positionWS), output.vertexSH, output.probeOcclusion);
    return output;
}

half4 EnvironmentLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
    half occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g, _OcclusionStrength);
    half3 emissionMap = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;

    half3 albedo = albedoSample.rgb;
    half metallic = saturate(maskSample.r * _Metallic);
    half smoothness = saturate(maskSample.a * _Smoothness);
    half3 emission = emissionMap * _EmissionColor.rgb * _EmissionIntensity;

    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));

    InputData inputData;
    EnvironmentLitInitializeInputData(input, normalWS, inputData);

    // Unbaked environment GI is often ~0; lift with albedo so ambient tint/intensity reach shadows.
    inputData.bakedGI = max(inputData.bakedGI, albedo);

    DragonLootStylizedSurface surface;
    surface.albedo = albedo;
    surface.metallic = metallic;
    surface.smoothness = smoothness;
    surface.occlusion = occlusion;
    surface.emission = emission;
    surface.normalWS = inputData.normalWS;
    surface.viewDirWS = inputData.viewDirectionWS;
    surface.rimIntensityScale = _RimIntensityScale;
    surface.specularIntensityScale = _SpecularIntensityScale;
    surface.wrapOverride = _WrapOverride;
    surface.receiveShadows = _ReceiveShadowsLocal;
    surface.reflections = _ReflectionsLocal;

    half3 lit = DragonLootShadeSurface(inputData, surface);
    lit = DragonLootMixFog(lit, inputData.fogCoord, inputData.positionWS);
    return half4(lit, 1.0h);
}

#endif
