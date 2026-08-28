#ifndef DRAGONLOOT_STYLIZED_LIGHTING_COMMON_INCLUDED
#define DRAGONLOOT_STYLIZED_LIGHTING_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "AreaAmbientCommon.hlsl"

// Globals from StylizedLightingDefinition.ApplyToGlobals()
float _DragonLoot_FalloffExponent;
float _DragonLoot_FalloffEdgeSoftness;
float _DragonLoot_DiffuseWrap;
float _DragonLoot_DirectLightScale;
float _DragonLoot_SpecularIntensity;
float _DragonLoot_SpecularSize;
float4 _DragonLoot_SpecularColor;
float _DragonLoot_EnableSpecular;
float4 _DragonLoot_RimColor;
float _DragonLoot_RimIntensity;
float _DragonLoot_RimPower;
float4 _DragonLoot_ShadowColor;
float _DragonLoot_ShadowStrength;
float _DragonLoot_ReceiveShadows;
float _DragonLoot_AmbientIntensity;
float4 _DragonLoot_AmbientTint;
float _DragonLoot_AmbientFloor;
float _DragonLoot_Saturation;
float _DragonLoot_Contrast;
float _DragonLoot_EnvironmentReflections;
float _DragonLoot_ReflectionIntensity;

// Unset globals are 0 — use defaults so Scene view / first frames aren't black.
bool DragonLootGlobalsUnset()
{
    return _DragonLoot_DirectLightScale <= 1e-4 && _DragonLoot_FalloffExponent <= 1e-4;
}

float DragonLootGlobalFalloffExponent()
{
    float v = _DragonLoot_FalloffExponent;
    return v > 1e-4 ? v : 1.0;
}

float DragonLootGlobalFalloffEdgeSoftness()
{
    float v = _DragonLoot_FalloffEdgeSoftness;
    return v > 1e-4 ? v : 0.35;
}

float DragonLootGlobalDirectLightScale()
{
    float v = _DragonLoot_DirectLightScale;
    return v > 1e-4 ? v : 1.0;
}

float DragonLootGlobalAmbientIntensity()
{
    if (DragonLootGlobalsUnset())
        return 1.0;
    return max(_DragonLoot_AmbientIntensity, 0.0);
}

float DragonLootGlobalSaturation()
{
    float v = _DragonLoot_Saturation;
    return v > 1e-4 ? v : 1.0;
}

float DragonLootGlobalContrast()
{
    float v = _DragonLoot_Contrast;
    return v > 1e-4 ? v : 1.0;
}

float DragonLootGlobalSpecularSize()
{
    float v = _DragonLoot_SpecularSize;
    return v > 1e-4 ? v : 48.0;
}

struct DragonLootStylizedSurface
{
    half3 albedo;
    half metallic;
    half smoothness;
    half occlusion;
    half3 emission;
    half3 normalWS;
    half3 viewDirWS;
    half rimIntensityScale;
    half specularIntensityScale;
    half wrapOverride; // < 0 = use global
    half receiveShadows;
    half reflections;
};

float DragonLootSoftDistanceAttenuation(float distanceSqr, half2 distanceAttenuation)
{
    // Directional lights pack atten.x = 0 so URP DistanceAttenuation returns ~1.
    if (distanceAttenuation.x <= 0.0)
        return DistanceAttenuation(distanceSqr, distanceAttenuation);

    float oneOverRangeSqr = max(distanceAttenuation.x, 1e-6);
    float range = rsqrt(oneOverRangeSqr);
    float distance = sqrt(distanceSqr);
    float distance01 = saturate(1.0 - (distance / max(range, 1e-4)));

    float exponent = max(DragonLootGlobalFalloffExponent(), 0.01);
    float atten = pow(distance01, exponent);

    float edge = max(DragonLootGlobalFalloffEdgeSoftness(), 1e-3);
    atten *= smoothstep(0.0, edge, distance01);
    return atten;
}

Light DragonLootGetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPositionWS = _AdditionalLightsBuffer[perObjectLightIndex].position;
    half3 color = _AdditionalLightsBuffer[perObjectLightIndex].color.rgb;
    half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[perObjectLightIndex].attenuation;
    half4 spotDirection = _AdditionalLightsBuffer[perObjectLightIndex].spotDirection;
    uint lightLayerMask = _AdditionalLightsBuffer[perObjectLightIndex].layerMask;
#else
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half3 color = _AdditionalLightsColor[perObjectLightIndex].rgb;
    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];
    half4 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex];
    uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[perObjectLightIndex]);
#endif

    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));

    float attenuation = DragonLootSoftDistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy);
    attenuation *= AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

    Light light;
    light.direction = lightDirection;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0;
    light.color = color;
    light.layerMask = lightLayerMask;
    return light;
}

Light DragonLootGetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
{
#if USE_CLUSTER_LIGHT_LOOP
    int lightIndex = i;
#else
    int lightIndex = GetPerObjectLightIndex(i);
#endif
    Light light = DragonLootGetAdditionalPerObjectLight(lightIndex, positionWS);

#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    half4 occlusionProbeChannels = _AdditionalLightsBuffer[lightIndex].occlusionProbeChannels;
#else
    half4 occlusionProbeChannels = _AdditionalLightsOcclusionProbes[lightIndex];
#endif
    light.shadowAttenuation = AdditionalLightShadow(lightIndex, positionWS, light.direction, shadowMask, occlusionProbeChannels);

#if defined(_LIGHT_COOKIES)
    real3 cookieColor = SampleAdditionalLightCookie(lightIndex, positionWS);
    light.color *= cookieColor;
#endif
    return light;
}

Light DragonLootGetAdditionalLight(uint i, InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = DragonLootGetAdditionalLight(i, inputData.positionWS, shadowMask);

#if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_AMBIENT_OCCLUSION))
    {
        light.color *= aoFactor.directAmbientOcclusion;
    }
#endif
    return light;
}

half DragonLootResolveWrap(half wrapOverride)
{
    half globalWrap = half(_DragonLoot_DiffuseWrap);
    if (DragonLootGlobalsUnset())
        globalWrap = 0.35h;
    return wrapOverride >= 0.0h ? wrapOverride : globalWrap;
}

half DragonLootWrappedNdotL(half3 normalWS, half3 lightDirWS, half wrap)
{
    half ndotl = dot(normalWS, lightDirWS);
    half w = saturate(wrap);
    return saturate((ndotl + w) / (1.0h + w));
}

half3 DragonLootApplyShadowTint(half shadowAttenuation, half receiveShadows)
{
    half globalReceive = (_DragonLoot_ReceiveShadows > 0.5 || DragonLootGlobalsUnset()) ? 1.0h : 0.0h;
    half useShadows = receiveShadows * globalReceive;
#if defined(_RECEIVE_SHADOWS_OFF)
    useShadows = 0.0h;
#endif
    half strength = _DragonLoot_ShadowStrength > 1e-4 ? half(_DragonLoot_ShadowStrength) : 1.0h;
    half shadow = lerp(1.0h, shadowAttenuation, useShadows * strength);
    half3 tint = half3(_DragonLoot_ShadowColor.rgb);
    if (dot(tint, tint) < 1e-6h)
        tint = half3(0.15h, 0.18h, 0.28h);
    half3 shadowCol = lerp(half3(1, 1, 1), tint, (1.0h - shadow));
    return lerp(half3(shadow, shadow, shadow), shadowCol, useShadows);
}

half3 DragonLootShadeLight(DragonLootStylizedSurface s, Light light)
{
    half wrap = DragonLootResolveWrap(s.wrapOverride);
    half diffuseTerm = DragonLootWrappedNdotL(s.normalWS, light.direction, wrap);

    half3 shadowMul = DragonLootApplyShadowTint(light.shadowAttenuation, s.receiveShadows);
    half atten = light.distanceAttenuation;
    half3 radiance = light.color * atten * shadowMul * half(DragonLootGlobalDirectLightScale());

    half3 diffuseColor = lerp(s.albedo, s.albedo * 0.35h, s.metallic);
    half3 color = diffuseColor * diffuseTerm * radiance;

    half enableSpec = (_DragonLoot_EnableSpecular > 0.5 || DragonLootGlobalsUnset()) ? 1.0h : 0.0h;
    half3 halfDir = SafeNormalize(light.direction + s.viewDirWS);
    half ndoth = saturate(dot(s.normalWS, halfDir));
    half specPower = max(DragonLootGlobalSpecularSize(), 1.0);
    half spec = pow(ndoth, specPower);
    half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), s.albedo, s.metallic);
    half3 specTint = half3(_DragonLoot_SpecularColor.rgb);
    if (dot(specTint, specTint) < 1e-8h)
        specTint = half3(1, 1, 1);
    half3 specCol = f0 * specTint;
    half specIntensity = _DragonLoot_SpecularIntensity > 1e-4 ? half(_DragonLoot_SpecularIntensity) : 0.45h;
    color += specCol * spec * radiance * diffuseTerm
        * specIntensity * s.specularIntensityScale * enableSpec;

    return color;
}

half3 DragonLootEvaluateRim(DragonLootStylizedSurface s, half lightGate)
{
    half rimPower = _DragonLoot_RimPower > 1e-4 ? half(_DragonLoot_RimPower) : 3.0h;
    half rimIntensity = _DragonLoot_RimIntensity;
    half3 rimColor = half3(_DragonLoot_RimColor.rgb);
    if (dot(rimColor, rimColor) < 1e-6h)
        rimColor = half3(1.0h, 0.95h, 0.85h);

    half ndotv = saturate(dot(s.normalWS, s.viewDirWS));
    half rim = pow(1.0h - ndotv, max(rimPower, 0.01h));
    return rimColor * rim * rimIntensity * s.rimIntensityScale * lightGate;
}

half3 DragonLootSaturation(half3 color, half amount)
{
    half luma = dot(color, half3(0.299h, 0.587h, 0.114h));
    return lerp(half3(luma, luma, luma), color, amount);
}

half3 DragonLootSoftContrast(half3 color, half amount)
{
    // Contrast around mid-grey without clamping HDR (URP Bloom needs values > threshold).
    half3 pushed = color - 0.5h;
    return max(pushed * amount + 0.5h, 0.0h);
}

half3 DragonLootGrade(half3 color)
{
    color = DragonLootSaturation(color, half(DragonLootGlobalSaturation()));
    color = DragonLootSoftContrast(color, half(DragonLootGlobalContrast()));
    return color;
}

half3 DragonLootSampleReflections(DragonLootStylizedSurface s, InputData inputData)
{
    half envOn = (_DragonLoot_EnvironmentReflections > 0.5 || DragonLootGlobalsUnset()) ? 1.0h : 0.0h;
    half enable = (envOn > 0.5h && s.reflections > 0.5h) ? 1.0h : 0.0h;

    half3 reflectVector = reflect(-s.viewDirWS, s.normalWS);
    half perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(s.smoothness);
    half3 irradiance = GlossyEnvironmentReflection(
        reflectVector,
        inputData.positionWS,
        perceptualRoughness,
        s.occlusion,
        inputData.normalizedScreenSpaceUV);

    half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), s.albedo, s.metallic);
    half ndotv = saturate(dot(s.normalWS, s.viewDirWS));
    half fresnel = Pow4(1.0h - ndotv);
    half3 reflectance = lerp(f0, saturate(s.smoothness + f0), fresnel);
    half reflIntensity = _DragonLoot_ReflectionIntensity > 1e-4 ? half(_DragonLoot_ReflectionIntensity) : 0.35h;
    return irradiance * reflectance * reflIntensity * enable;
}

void DragonLootAddLightContribution(
    DragonLootStylizedSurface s,
    Light light,
    uint meshRenderingLayers,
    inout half3 color,
    inout half lightGate)
{
#ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        return;
#endif
    color += DragonLootShadeLight(s, light);
    lightGate = max(lightGate, saturate(light.distanceAttenuation * light.shadowAttenuation));
}

half3 DragonLootShadeSurface(InputData inputData, DragonLootStylizedSurface s)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, s.occlusion);
    uint meshRenderingLayers = GetMeshRenderingLayer();

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, s.normalWS, inputData.bakedGI);

    half3 ambientTint = half3(_DragonLoot_AmbientTint.rgb);
    if (DragonLootGlobalsUnset() && dot(ambientTint, ambientTint) < 1e-6h)
        ambientTint = half3(1, 1, 1);
    half ambientIntensity = half(DragonLootGlobalAmbientIntensity());
    half ambientFloor = _DragonLoot_AmbientFloor;
    half3 ambient = inputData.bakedGI * ambientTint * ambientIntensity * s.occlusion;
    ambient = max(ambient, s.albedo * ambientTint * ambientIntensity * ambientFloor);
#if defined(_SCREEN_SPACE_OCCLUSION)
    ambient *= aoFactor.indirectAmbientOcclusion;
#endif
    ambient += DragonLootApplyAreaAmbient(s.albedo, inputData.positionWS, s.occlusion);

    half3 color = ambient;
    half lightGate = 0.0h;

    DragonLootAddLightContribution(s, mainLight, meshRenderingLayers, color, lightGate);

#if defined(_ADDITIONAL_LIGHTS)
    uint lightsCount = GetAdditionalLightsCount();

    // Forward+: additional directional lights are a separate index range.
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light dirLight = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        DragonLootAddLightContribution(s, dirLight, meshRenderingLayers, color, lightGate);
    }
#endif

    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        DragonLootAddLightContribution(s, light, meshRenderingLayers, color, lightGate);
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    color += inputData.vertexLighting * s.albedo;
#endif

    color += DragonLootSampleReflections(s, inputData);
    color += DragonLootEvaluateRim(s, saturate(lightGate + 0.25h));
    color = DragonLootGrade(color);
    color += s.emission;
    return color;
}

void DragonLootInitStylizedSurface(
    InputData inputData,
    SurfaceData surfaceData,
    half receiveShadows,
    out DragonLootStylizedSurface s)
{
    s = (DragonLootStylizedSurface)0;
    s.albedo = surfaceData.albedo;
    s.metallic = surfaceData.metallic;
    s.smoothness = surfaceData.smoothness;
    s.occlusion = surfaceData.occlusion;
    s.emission = surfaceData.emission;
    s.normalWS = inputData.normalWS;
    s.viewDirWS = inputData.viewDirectionWS;
    s.rimIntensityScale = 1.0h;
    s.specularIntensityScale = 1.0h;
    s.wrapOverride = -1.0h;
    s.receiveShadows = receiveShadows;
    s.reflections = 1.0h;
}

half4 DragonLootFragmentStylized(InputData inputData, SurfaceData surfaceData, half receiveShadows)
{
    DragonLootStylizedSurface s;
    DragonLootInitStylizedSurface(inputData, surfaceData, receiveShadows, s);
    return half4(DragonLootShadeSurface(inputData, s), surfaceData.alpha);
}

half4 DragonLootFragmentStylized(InputData inputData, SurfaceData surfaceData)
{
    return DragonLootFragmentStylized(inputData, surfaceData, 1.0h);
}

/// <summary>
/// URP PBR lighting with DragonLoot soft punctual falloff (same BRDF, custom distance atten).
/// </summary>
half4 DragonLootFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
#if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
#else
    bool specularHighlightsOff = false;
#endif
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

#if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
        return debugColor;
#endif

    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(
        brdfData,
        brdfDataClearCoat,
        surfaceData.clearCoatMask,
        inputData.bakedGI,
        aoFactor.indirectAmbientOcclusion,
        inputData.positionWS,
        inputData.normalWS,
        inputData.viewDirectionWS,
        inputData.normalizedScreenSpaceUV);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(
            brdfData,
            brdfDataClearCoat,
            mainLight,
            inputData.normalWS,
            inputData.viewDirectionWS,
            surfaceData.clearCoatMask,
            specularHighlightsOff);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(
                brdfData,
                brdfDataClearCoat,
                light,
                inputData.normalWS,
                inputData.viewDirectionWS,
                surfaceData.clearCoatMask,
                specularHighlightsOff);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = DragonLootGetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(
                brdfData,
                brdfDataClearCoat,
                light,
                inputData.normalWS,
                inputData.viewDirectionWS,
                surfaceData.clearCoatMask,
                specularHighlightsOff);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
#endif

#if REAL_IS_HALF
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

#endif
