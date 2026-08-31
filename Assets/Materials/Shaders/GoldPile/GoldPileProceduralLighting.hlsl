#ifndef DRAGONLOOT_GOLDPILE_PROCEDURAL_LIGHTING_INCLUDED
#define DRAGONLOOT_GOLDPILE_PROCEDURAL_LIGHTING_INCLUDED

#include "../Stylized/StylizedLightingCommon.hlsl"
#include "GoldPileProceduralCommon.hlsl"

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
    half   fogFactor  : TEXCOORD3;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD4;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD6;
#endif
    float2 deformUV : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void ProcInitializeBakedGIData(Varyings input, inout InputData inputData)
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

void ProcInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
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
    ProcInitializeBakedGIData(input, inputData);
}

Varyings GoldPileProcLitVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionOS = ApplyProcRuntimeDeform(input.positionOS.xyz, input.texcoord);
    VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = normalInputs.normalWS;

    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInputs.tangentWS, sign);

    output.deformUV = input.texcoord;
    output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(posInputs);
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    OUTPUT_SH4(
        posInputs.positionWS,
        output.normalWS.xyz,
        GetWorldSpaceNormalizeViewDir(posInputs.positionWS),
        output.vertexSH,
        output.probeOcclusion);
    return output;
}

half4 GoldPileProcLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ProcClipBelowGround(input.deformUV);

    float3 positionWS = input.positionWS;
    float3 geomNormalWS = normalize(input.normalWS);
    float camDist = distance(positionWS, GetCameraPositionWS());
    half lodBand = ProcLodFactor(camDist);
    half detailFade = ProcLodDetailFade(lodBand);

    half3 heightfieldNormalWS = ProcDeformNormalWS(input.deformUV, geomNormalWS);
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
    ProcCoinSurface coin = SampleProcVirtualCoins(positionWS, heightfieldNormalWS, viewDirWS, camDist, lodBand);

    half3 detailedNormalWS = lerp(heightfieldNormalWS, coin.normalWS, detailFade);

    // Fully far: colour + basic lighting only (same cost profile as before).
    if (detailFade <= 0.001h)
    {
        SurfaceData surfaceFar = (SurfaceData)0;
        surfaceFar.albedo = coin.albedo;
        surfaceFar.metallic = coin.metallic;
        surfaceFar.specular = half3(0, 0, 0);
        surfaceFar.smoothness = coin.smoothness;
        surfaceFar.normalTS = half3(0, 0, 1);
        surfaceFar.emission = 0;
        surfaceFar.occlusion = 1;
        surfaceFar.alpha = 1;

        InputData inputFar;
        ProcInitializeInputData(input, heightfieldNormalWS, inputFar);
        // Match Coin Pile: lift ambient for all materials, then floor lit metals.
        half3 ambientFloorFar = coin.albedo * _ReflectionFloor;
        inputFar.bakedGI = max(inputFar.bakedGI, ambientFloorFar);
        half4 colorFar = DragonLootFragmentStylized(inputFar, surfaceFar);
        colorFar.rgb = max(colorFar.rgb, ambientFloorFar * coin.metallic);

        Light pixelLightFar = GetMainLight(
            inputFar.shadowCoord,
            inputFar.positionWS,
            inputFar.shadowMask);
        colorFar.rgb += ProcDistantCoinPixels(
            positionWS,
            input.positionCS,
            input.deformUV,
            heightfieldNormalWS,
            inputFar.viewDirectionWS,
            pixelLightFar.direction,
            camDist);

        colorFar.rgb = DragonLootMixFog(colorFar.rgb, inputFar.fogCoord, positionWS);
        return colorFar;
    }

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = coin.albedo;
    surfaceData.metallic = coin.metallic;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = coin.smoothness;
    surfaceData.normalTS = lerp(half3(0, 0, 1), coin.normalTS, detailFade);
    surfaceData.emission = 0;
    surfaceData.occlusion = lerp(1.0h, coin.occlusion, detailFade);
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData;
    ProcInitializeInputData(input, detailedNormalWS, inputData);

    // Match DragonLoot/Coin Pile ambient / fresnel response.
    half3 ambientFloor = coin.albedo * _ReflectionFloor;
    inputData.bakedGI = max(inputData.bakedGI, ambientFloor);

    half4 color = DragonLootFragmentStylized(inputData, surfaceData);
    color.rgb = max(color.rgb, ambientFloor * coin.metallic);

    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnel = ProcSchlickFresnel(ndotv, _FresnelPower) * detailFade;
    half litGate = saturate(Luminance(color.rgb) * 2.0h + _ReflectionFloor);
    color.rgb += _FresnelColor.rgb * fresnel * _FresnelIntensity * coin.metallic * litGate;

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    color.rgb += ProcDistantCoinPixels(
        positionWS,
        input.positionCS,
        input.deformUV,
        detailedNormalWS,
        inputData.viewDirectionWS,
        mainLight.direction,
        camDist);

    color.rgb = DragonLootMixFog(color.rgb, inputData.fogCoord, inputData.positionWS);
    return color;
}

#endif
