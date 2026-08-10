#ifndef DRAGONLOOT_ARTIFACT_LIGHTING_INCLUDED
#define DRAGONLOOT_ARTIFACT_LIGHTING_INCLUDED

#include "../Stylized/StylizedLightingCommon.hlsl"
#include "ArtifactInput.hlsl"

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

half ArtifactSchlickFresnel(half ndotv, half power)
{
    return pow(saturate(1.0h - ndotv), power);
}

void ArtifactInitializeBakedGIData(Varyings input, inout InputData inputData)
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

void ArtifactInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
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
    ArtifactInitializeBakedGIData(input, inputData);
}

// Light-anchored specular lobe. Smoothness tightens the highlight; MatCap props tint/scale it.
half3 ArtifactSpecularFromLight(
    Light light,
    half3 normalWS,
    half3 viewDirWS,
    half3 f0,
    half3 lobeTint,
    half specPower,
    half gate)
{
    half atten = light.distanceAttenuation * light.shadowAttenuation;
    half ndotl = saturate(dot(normalWS, light.direction));
    half3 radiance = light.color * atten * ndotl;
    if (Luminance(radiance) <= 0.0001h)
        return half3(0, 0, 0);

    half3 halfDir = SafeNormalize(light.direction + viewDirWS);
    half nh = saturate(dot(normalWS, halfDir));
    half lobe = pow(nh, specPower);

    return f0 * lobeTint * _MatCapColor.rgb * _MatCapIntensity * lobe * radiance * gate;
}

half3 ArtifactAccumulateSpecular(
    InputData inputData,
    half3 albedo,
    half metallic,
    half smoothness,
    half cleanGate)
{
    half gate = metallic * saturate(smoothness) * cleanGate;
    if (gate <= 0.0001h || _MatCapIntensity <= 0.0001h)
        return half3(0, 0, 0);

    half3 spec = half3(0, 0, 0);
    half4 shadowMask = inputData.shadowMask;
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, 1.0h);
    uint meshRenderingLayers = GetMeshRenderingLayer();

    half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
    // Smoothness drives lobe size (rough wide → sharp hot); MatCapPower scales artist control.
    half specPower = exp2(1.0h + 9.0h * saturate(smoothness));
    specPower = max(specPower * max(_MatCapPower, 0.5h), 1.0h);

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    // Optional tint map from main-light half vector so sampling stays light-anchored, not camera-locked.
    half3 mainHalf = SafeNormalize(mainLight.direction + inputData.viewDirectionWS);
    half3 halfVS = mul((half3x3)UNITY_MATRIX_V, mainHalf);
    float2 tintUV = halfVS.xy * 0.5 + 0.5;
    half3 tintTex = SAMPLE_TEXTURE2D(_MatCap, sampler_MatCap, tintUV).rgb;
    half texWeight = saturate(Luminance(tintTex) * 8.0h);
    half3 lobeTint = lerp(half3(1, 1, 1), tintTex, texWeight);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        spec += ArtifactSpecularFromLight(
            mainLight, inputData.normalWS, inputData.viewDirectionWS,
            f0, lobeTint, specPower, gate);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint lightsCount = GetAdditionalLightsCount();

#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light dirLight = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(dirLight.layerMask, meshRenderingLayers))
#endif
        {
            spec += ArtifactSpecularFromLight(
                dirLight, inputData.normalWS, inputData.viewDirectionWS,
                f0, lobeTint, specPower, gate);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            spec += ArtifactSpecularFromLight(
                light, inputData.normalWS, inputData.viewDirectionWS,
                f0, lobeTint, specPower, gate);
        }
    LIGHT_LOOP_END
#endif

    return spec;
}

Varyings ArtifactLitVert(Attributes input)
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

half4 ArtifactLitFrag(Varyings input) : SV_Target
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

    // Dirt coverage is driven by per-renderer MPB _DirtStrength (cleanliness).
    // Dirt mask polarity: black = dirt, white = clean. Unassigned map defaults to black (full coverage).
    half dirtStrength = saturate(_DirtStrength) * saturate(_DirtMapStrength);
    half dirtMask = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, input.uv).r;
    half coverage = 1.0h - dirtMask;
    coverage = saturate((coverage - 0.5h) * _DirtContrast + 0.5h);
    half dirt = saturate(dirtStrength * coverage);

    half3 muddy = albedo * _DirtAlbedoMultiply;
    half3 dirtyAlbedo = lerp(muddy, _DirtColor.rgb, 0.65h);
    albedo = lerp(albedo, dirtyAlbedo, dirt);
    metallic = saturate(metallic * (1.0h - dirt * _DirtMetallicLoss));
    smoothness = saturate(smoothness * (1.0h - dirt * _DirtSmoothnessLoss));
    occlusion = saturate(occlusion * (1.0h - dirt * 0.35h));

    half cleanGate = saturate(1.0h - dirt);
    smoothness = saturate(smoothness + _CleanSmoothnessBoost * cleanGate);

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
    ArtifactInitializeInputData(input, normalWS, inputData);

    half3 ambientFloor = albedo * _ReflectionFloor;
    inputData.bakedGI = max(inputData.bakedGI, ambientFloor * metallic);

    half4 color = DragonLootFragmentPBR(inputData, surfaceData);
    color.rgb = max(color.rgb, ambientFloor * metallic);

    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnel = ArtifactSchlickFresnel(ndotv, _FresnelPower);
    half litGate = saturate(Luminance(color.rgb) * 2.0h + _ReflectionFloor);
    half fresnelScale = (1.0h + _CleanFresnelBoost * cleanGate);
    half3 fresnelTerm = _FresnelColor.rgb * fresnel * _FresnelIntensity * metallic * litGate * cleanGate * fresnelScale;
    color.rgb += fresnelTerm;

    // Stylized specular follows lights (half-vector), tightness from smoothness.
    half3 specularTerm = ArtifactAccumulateSpecular(
        inputData, albedo, metallic, smoothness, cleanGate);
    color.rgb += specularTerm;

    // HDR shine / emissive punch from fresnel + specular contributions.
    half shineLuma = Luminance(fresnelTerm) + Luminance(specularTerm);
    half3 shineTerm = _ShineColor.rgb * _ShineBoost * shineLuma * cleanGate;
    color.rgb += shineTerm;

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
}

#endif
