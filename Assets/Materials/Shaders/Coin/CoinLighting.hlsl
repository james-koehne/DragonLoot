#ifndef DRAGONLOOT_COIN_LIGHTING_INCLUDED
#define DRAGONLOOT_COIN_LIGHTING_INCLUDED

#include "../Stylized/StylizedLightingCommon.hlsl"
#include "CoinCommon.hlsl"

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

void CoinInitializeBakedGIData(Varyings input, inout InputData inputData)
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
}

void CoinInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
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
    CoinInitializeBakedGIData(input, inputData);
}

Varyings CoinLitVert(Attributes input)
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

half4 CoinLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
    half occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g, _OcclusionStrength);

    half3 albedo = albedoSample.rgb;
    half metallic = saturate(maskSample.r * _Metallic);
    half smoothness = saturate(maskSample.a * _Smoothness);
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);

#if defined(_EDGEWEAR_ON)
    half edgeWear = SAMPLE_TEXTURE2D(_EdgeWearMap, sampler_EdgeWearMap, input.uv).r * _EdgeWearStrength;
    // Worn edges: slightly less metal, cooler/darker, rougher.
    albedo = lerp(albedo, albedo * half3(0.72h, 0.68h, 0.55h), saturate(edgeWear));
    metallic = saturate(metallic * (1.0h - edgeWear * 0.35h));
    smoothness = saturate(smoothness * (1.0h - edgeWear * 0.45h));
#endif

#if defined(_DIRT_ON)
    half dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, input.uv).r * _DirtStrength;
    albedo = lerp(albedo, albedo * half3(0.55h, 0.42h, 0.28h), saturate(dirt));
    metallic = saturate(metallic * (1.0h - dirt * 0.55h));
    smoothness = saturate(smoothness * (1.0h - dirt * 0.4h));
    occlusion = saturate(occlusion * (1.0h - dirt * 0.2h));
#endif

    // Keep metals readable — AO must not crush them to black.
    occlusion = lerp(occlusion, max(occlusion, 0.7h), metallic);

    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedo;
    surfaceData.metallic = metallic;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = normalTS;
    surfaceData.emission = 0;
    surfaceData.occlusion = occlusion;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData;
    CoinInitializeInputData(input, normalWS, inputData);

    // Lift ambient for metals so probe/HDRI gaps never go pure black.
    half3 ambientFloor = albedo * _ReflectionFloor;
    inputData.bakedGI = max(inputData.bakedGI, ambientFloor * metallic);

    half4 color = DragonLootFragmentStylized(inputData, surfaceData);

    // Reflection floor on final lit result (probes + specular + ambient).
    color.rgb = max(color.rgb, ambientFloor * metallic);

    // Warm grazing Fresnel — view-dependent only, scales with existing light (not constant emission).
    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnel = CoinSchlickFresnel(ndotv, _FresnelPower);
    half litGate = saturate(Luminance(color.rgb) * 2.0h + _ReflectionFloor);
    color.rgb += _FresnelColor.rgb * fresnel * _FresnelIntensity * metallic * litGate;

    color.rgb = DragonLootMixFog(color.rgb, inputData.fogCoord, inputData.positionWS);
    return color;
}

#endif
