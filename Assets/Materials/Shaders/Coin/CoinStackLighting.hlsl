#ifndef DRAGONLOOT_COIN_STACK_LIGHTING_INCLUDED
#define DRAGONLOOT_COIN_STACK_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "CoinStackBand.hlsl"
#include "CoinStackSeamClip.hlsl"
#include "CoinStackSparkle.hlsl"

#if defined(_COIN_STACK_MULTI)
#include "CoinStackMulti.hlsl"
#else
TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
#endif

half CoinStackSchlickFresnel(half ndotv, half power)
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
    float3 normalOS   : TEXCOORD8;
    float  stackY01   : TEXCOORD9;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings CoinStackLitVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float instanceSeed = CoinStackInstanceSeed();

    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = normalInputs.normalWS;
    output.normalOS = input.normalOS;

    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInputs.tangentWS, sign);
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
    output.instanceSeed = instanceSeed;

    output.stackY01 = CoinStackComputeStackY01(input.positionOS.y);

    OUTPUT_SH(output.normalWS, output.vertexSH);

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    output.vertexLighting = VertexLighting(posInputs.positionWS, normalInputs.normalWS);
#else
    output.vertexLighting = half3(0, 0, 0);
#endif

    return output;
}

half4 CoinStackLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float seed = input.instanceSeed;
    half tint = 1.0h + ((half)CoinStackHash11(seed) * 2.0h - 1.0h) * _TintVariation;
    half value = 1.0h + ((half)CoinStackHash11(seed + 17.13) * 2.0h - 1.0h) * _ValueVariation;

    float coinCount = max((float)_CoinCount, 1.0);
    bool isCap = CoinStackIsCap(input.normalOS);

    float2 sampleUv = input.uv;
    half bandShade = 1.0h;
    CoinStackBandData bands = (CoinStackBandData)0;
    CoinStackSeamView seam = (CoinStackSeamView)0;

    if (!isCap)
    {
        bands = CoinStackEvaluateBands(input.stackY01, coinCount);
        bandShade = bands.bandShade;
        seam = CoinStackEvaluateSeamView(
            input.positionWS,
            input.normalOS,
            input.stackY01,
            coinCount,
            seed,
            bands.grooveMask,
            bands.ridgeMask);
        CoinStackClipSeamSide(seam);
    }
    else
    {
        float2 radial = input.uv * 2.0 - 1.0;
        float rim = saturate(length(radial));
        bandShade = lerp(1.0h, 0.92h, (half)smoothstep(0.75, 1.0, rim));
    }

#if defined(_COIN_STACK_MULTI)
    int typeId = CoinStackResolveTypeId(input.stackY01, coinCount, isCap, input.normalOS);
    half4 albedoSample = CoinStackSampleAlbedoMulti(sampleUv, typeId);
    half4 maskSample = CoinStackSampleMaskMulti(sampleUv, typeId);
    half3 fresnelRgb = CoinStackTypeFresnelRgb(typeId);
#else
    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, sampleUv) * _BaseColor;
    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, sampleUv);
    half3 fresnelRgb = _FresnelColor.rgb;
#endif

    half seamAO = CoinStackSeamSoftAO(seam);
    half3 albedo = albedoSample.rgb * tint * value * bandShade * seamAO;
    half warm = ((half)CoinStackHash11(seed + 31.71) * 2.0h - 1.0h) * 0.03h;
    albedo += half3(warm, warm * 0.35h, -warm);

    half metallic = saturate(maskSample.r * _Metallic);
    half smoothness = saturate(maskSample.a * _Smoothness);
    // Push toward polished metal without forcing a hard 1.0 clamp look.
    smoothness = saturate(lerp(smoothness, 1.0h, saturate(_ShineBoost) * 0.55h));

    if (!isCap)
    {
        half grooveSmooth = lerp((half)_GrooveSmoothnessScale, (half)_RidgeSmoothnessScale, (half)bands.ridgeMask);
        smoothness *= grooveSmooth;
        metallic *= lerp((half)_GrooveMetallicScale, 1.0h, (half)bands.ridgeMask);
    }

    half bumpScale = isCap ? _BumpScale : _SideBumpScale;
#if defined(_COIN_STACK_MULTI)
    half3 normalTS = CoinStackSampleNormalTSMulti(sampleUv, typeId, bumpScale);
#else
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, sampleUv), bumpScale);
#endif
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);

    half3 normalWS;
    if (isCap)
    {
        normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
    }
    else
    {
        float3 nOS = CoinStackComputeSideNormalOS(input.normalOS, input.stackY01, coinCount);
        half3 nProcWS = NormalizeNormalPerPixel(TransformObjectToWorldNormal(nOS));
        half3 nBumpWS = TransformTangentToWorld(normalTS, tangentToWorld);
        normalWS = NormalizeNormalPerPixel(normalize(nProcWS + nBumpWS - dot(nBumpWS, nProcWS) * nProcWS));
        normalWS = CoinStackApplySeamNormalWS(normalWS, seam);
    }

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedo;
    surfaceData.metallic = metallic;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = normalTS;
    surfaceData.emission = 0;
    surfaceData.occlusion = seamAO;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.shadowCoord = float4(0, 0, 0, 0);
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = input.vertexLighting;
    inputData.bakedGI = SAMPLE_GI(0, input.vertexSH, normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    half3 ambientFloor = albedo * _ReflectionFloor;
    inputData.bakedGI = max(inputData.bakedGI, ambientFloor);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = max(color.rgb, ambientFloor * metallic);

    Light mainLight = GetMainLight();
    half3 lightDirWS = mainLight.direction;
    half3 halfDir = SafeNormalize(lightDirWS + inputData.viewDirectionWS);
    half ndoth = saturate(dot(inputData.normalWS, halfDir));
    half specularPeek = pow(ndoth, max(_SpecularPower, 8.0h));
    half shineMul = 1.0h + saturate(_ShineBoost);
    color.rgb += albedo * specularPeek * _SpecularIntensity * metallic * shineMul * mainLight.color * mainLight.distanceAttenuation;

    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnel = CoinStackSchlickFresnel(ndotv, _FresnelPower);
    half litGate = saturate(Luminance(color.rgb) * 2.0h + _ReflectionFloor);
    half fresnelAmt = _FresnelIntensity * shineMul;
    color.rgb += fresnelRgb * fresnel * fresnelAmt * metallic * litGate;

    color.rgb += CoinStackSparkle(
        input.positionWS,
        inputData.normalWS,
        inputData.viewDirectionWS,
        lightDirWS,
        metallic);

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
}

#endif
