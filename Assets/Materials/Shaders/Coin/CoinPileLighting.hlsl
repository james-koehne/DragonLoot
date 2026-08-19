#ifndef DRAGONLOOT_COIN_PILE_LIGHTING_INCLUDED
#define DRAGONLOOT_COIN_PILE_LIGHTING_INCLUDED

#include "../Stylized/StylizedLightingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#define COIN_PILE_FORCE_LOD_DITHER
#include "CoinPileLodDither.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Metallic;
    half _Smoothness;
    half _BumpScale;
    half _ReflectionFloor;
    half4 _FresnelColor;
    half _FresnelIntensity;
    half _FresnelPower;
    half _TintVariation;
    half _ValueVariation;
    half _Cutoff;
    half _Cull;
CBUFFER_END

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);

float CoinPileHash11(float p)
{
    float3 p3 = frac(float3(p, p, p) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float CoinPileInstanceSeed()
{
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float seed = dot(origin, float3(12.9898, 78.233, 37.719));
#if defined(UNITY_INSTANCING_ENABLED)
    seed += (float)unity_InstanceID * 19.19;
#endif
    return CoinPileHash11(seed);
}

half CoinPileSchlickFresnel(half ndotv, half power)
{
    half inv = 1.0h - saturate(ndotv);
    return pow(inv, max(power, 0.01h));
}

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
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
    nointerpolation float instanceSeed : TEXCOORD5;
    half3  vertexSH   : TEXCOORD6;
    half3  vertexLighting : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings CoinPileLitVert(Attributes input)
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
    output.instanceSeed = CoinPileInstanceSeed();

    OUTPUT_SH(output.normalWS, output.vertexSH);

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    output.vertexLighting = VertexLighting(posInputs.positionWS, normalInputs.normalWS);
#else
    output.vertexLighting = half3(0, 0, 0);
#endif

    return output;
}

half4 CoinPileLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    CoinPileApplyLodDither(input.positionWS, input.positionCS);

    float seed = input.instanceSeed;
    half tint = 1.0h + ((half)CoinPileHash11(seed) * 2.0h - 1.0h) * _TintVariation;
    half value = 1.0h + ((half)CoinPileHash11(seed + 17.13) * 2.0h - 1.0h) * _ValueVariation;

    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);

    half3 albedo = albedoSample.rgb * tint * value;
    half warm = ((half)CoinPileHash11(seed + 31.71) * 2.0h - 1.0h) * 0.03h;
    albedo += half3(warm, warm * 0.35h, -warm);

    half metallic = saturate(maskSample.r * _Metallic);
    half smoothness = saturate(maskSample.a * _Smoothness);

    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
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
    surfaceData.occlusion = 1;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    // Pile draws with receiveShadows=false — never sample the shadow map.
    inputData.shadowCoord = float4(0, 0, 0, 0);
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = input.vertexLighting;
    inputData.bakedGI = SAMPLE_GI(0, input.vertexSH, normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    // Metals crush to black without ambient / probes — lift before PBR.
    half3 ambientFloor = albedo * _ReflectionFloor;
    inputData.bakedGI = max(inputData.bakedGI, ambientFloor);

    half4 color = DragonLootFragmentStylized(inputData, surfaceData, 0.0h);
    color.rgb = max(color.rgb, ambientFloor * metallic);

    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnel = CoinPileSchlickFresnel(ndotv, _FresnelPower);
    half litGate = saturate(Luminance(color.rgb) * 2.0h + _ReflectionFloor);
    color.rgb += _FresnelColor.rgb * fresnel * _FresnelIntensity * metallic * litGate;

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
}

#endif
