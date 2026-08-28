#ifndef DRAGONLOOT_GEM_LIGHTING_INCLUDED
#define DRAGONLOOT_GEM_LIGHTING_INCLUDED

#include "../Stylized/StylizedLightingCommon.hlsl"
#include "GemCommon.hlsl"

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
    nointerpolation float instanceSeed : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD8;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void GemInitializeBakedGIData(Varyings input, inout InputData inputData)
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

void GemInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
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
    GemInitializeBakedGIData(input, inputData);
}

Varyings GemLitVert(Attributes input)
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
    output.instanceSeed = GemInstanceSeed();

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(posInputs);
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    OUTPUT_SH4(posInputs.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(posInputs.positionWS), output.vertexSH, output.probeOcclusion);
    return output;
}

half GemSteppedLightAmount(half3 normalWS, Light light, half steps)
{
    half ndotl = saturate(dot(normalWS, light.direction));
    half directLight = ndotl * light.shadowAttenuation * light.distanceAttenuation;
    half stepCount = max(steps, 2.0h);
    return floor(directLight * stepCount) / max(stepCount - 1.0h, 1.0h);
}

void GemAccumulateLight(
    half3 normalWS,
    Light light,
    uint meshRenderingLayers,
    half steps,
    inout half steppedLight,
    inout half3 lightColorAccum)
{
#ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        return;
#endif
    half amount = GemSteppedLightAmount(normalWS, light, steps);
    steppedLight = max(steppedLight, amount);
    lightColorAccum += light.color * amount;
}

half4 GemLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    GemVariation variation = GemBuildVariation(input.instanceSeed);

    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    half occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g, _OcclusionStrength);

    half3 albedo = albedoSample.rgb;
    GemApplyColorVariation(albedo, variation);

    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);

#if defined(_SCRATCHES_ON)
    half scratch = SAMPLE_TEXTURE2D(_ScratchMap, sampler_ScratchMap, input.uv).r * _ScratchStrength;
    albedo *= 1.0h - scratch * 0.08h;
#endif

#if defined(_EDGEWEAR_ON)
    half edgeWear = SAMPLE_TEXTURE2D(_EdgeWearMap, sampler_EdgeWearMap, input.uv).r * _EdgeWearStrength;
    albedo = lerp(albedo, albedo * half3(0.85h, 0.82h, 0.78h), saturate(edgeWear));
#endif

    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));

    InputData inputData;
    GemInitializeInputData(input, normalWS, inputData);

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    half steps = max(_LightSteps, 2.0h);

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    half steppedLight = 0.0h;
    half3 lightColorAccum = half3(0, 0, 0);
    GemAccumulateLight(inputData.normalWS, mainLight, meshRenderingLayers, steps, steppedLight, lightColorAccum);

#if defined(_ADDITIONAL_LIGHTS)
    uint lightsCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light dirLight = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        GemAccumulateLight(inputData.normalWS, dirLight, meshRenderingLayers, steps, steppedLight, lightColorAccum);
    }
#endif
    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        GemAccumulateLight(inputData.normalWS, light, meshRenderingLayers, steps, steppedLight, lightColorAccum);
    LIGHT_LOOP_END
#endif

    half ambientLight = saturate(GemLuminance(inputData.bakedGI));
    half lightAmount = saturate(steppedLight + ambientLight * 0.35h);
    half3 blendedLightColor = steppedLight > 1e-4h
        ? saturate(lightColorAccum / max(steppedLight, 1e-4h))
        : mainLight.color;

    float3 positionOS = TransformWorldToObject(input.positionWS);
    half3 normalOS = normalize(TransformWorldToObjectDir(inputData.normalWS));
    half facet = GemFacetPattern(positionOS, normalOS);
    half facetContrast = (facet * 2.0h - 1.0h) * _FacetStrength;

    half shade = saturate((1.0h - _ShadowStrength) + lightAmount * _ShadowStrength + facetContrast * 0.3h);
    half3 deepColor = _InternalColor.rgb * variation.brightnessScale;
    half3 color = lerp(deepColor, albedo, shade);
    color *= lerp(0.72h, 1.16h, facet);
    color *= occlusion * GemInclusionDarken(input.positionWS);
    color += DragonLootApplyAreaAmbient(albedo, input.positionWS, occlusion);
    color *= lerp(half3(1, 1, 1), blendedLightColor, steppedLight);

    half ndotv = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half3 halfDir = SafeNormalize(mainLight.direction + inputData.viewDirectionWS);
    half ndoth = saturate(dot(inputData.normalWS, halfDir));
    half shineEdge = min(_ShineThreshold + 0.08h, 0.999h);
    half shine = smoothstep(_ShineThreshold, shineEdge, ndoth);
    shine *= pow(ndoth, max(_ShineSize * 0.25h, 1.0h)) * _ShineIntensity;
    shine *= max(steppedLight, 0.25h);

    half3 normalVS = mul((half3x3)UNITY_MATRIX_V, inputData.normalWS);
    half graphicFacing = saturate(dot(normalVS, normalize(half3(-0.65h, 0.7h, 0.3h))));
    half graphicShine = smoothstep(0.82h, 0.9h, graphicFacing) * _ShineIntensity * 0.55h;

    half rim = GemSchlickFresnel(ndotv, _RimPower) * _RimIntensity;
    color += _FresnelColor.rgb * (shine + graphicShine + rim);

    color = MixFog(color, inputData.fogCoord);
    return half4(color, 1.0h);
}

#endif
