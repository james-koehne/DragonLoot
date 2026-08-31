#ifndef DRAGONLOOT_AREA_AMBIENT_COMMON_INCLUDED
#define DRAGONLOOT_AREA_AMBIENT_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

TEXTURE3D(_DragonLoot_AreaAmbientTex);
SAMPLER(sampler_DragonLoot_AreaAmbientTex);

float3 _DragonLoot_AreaAmbientOrigin;
float3 _DragonLoot_AreaAmbientSize;
float _DragonLoot_AreaAmbientIntensity;
float _DragonLoot_AreaAmbientMix;
float _DragonLoot_AreaAmbientAlbedoInfluence;
float _DragonLoot_AreaAmbientFogInfluence;
float _DragonLoot_AreaAmbientEnabled;

float4 DragonLootSampleAreaAmbientData(float3 positionWS)
{
    if (_DragonLoot_AreaAmbientEnabled <= 0.5)
        return float4(0, 0, 0, 0);

    float3 worldSize = max(_DragonLoot_AreaAmbientSize, 1e-5);
    float3 uvw = (positionWS - _DragonLoot_AreaAmbientOrigin) / worldSize;

    float3 distOutside = max(-uvw, uvw - 1.0);
    float outside = max(max(distOutside.x, distOutside.y), distOutside.z);
    if (outside > 0.02)
        return float4(0, 0, 0, 0);

    float edgeFade = 1.0 - saturate(outside / 0.02);

    float4 texel = SAMPLE_TEXTURE3D_LOD(
        _DragonLoot_AreaAmbientTex,
        sampler_DragonLoot_AreaAmbientTex,
        saturate(uvw),
        0);

    float3 rgb = texel.rgb * _DragonLoot_AreaAmbientIntensity * edgeFade;
    float fogWeight = texel.a * edgeFade;
    return float4(rgb, fogWeight);
}

half3 DragonLootSampleAreaAmbient(float3 positionWS)
{
    return half3(DragonLootSampleAreaAmbientData(positionWS).rgb);
}

half3 DragonLootApplyAreaAmbient(half3 albedo, float3 positionWS, half occlusion)
{
    half3 area = DragonLootSampleAreaAmbient(positionWS);
    if (dot(area, area) <= 1e-8h)
        return half3(0, 0, 0);

    half albedoInfluence = half(_DragonLoot_AreaAmbientAlbedoInfluence);
    half3 contribution = lerp(area, area * albedo, albedoInfluence);
    return contribution * half(_DragonLoot_AreaAmbientMix) * occlusion;
}

half3 DragonLootMixFog(half3 color, half fogCoord, float3 positionWS)
{
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    half fogFactor = saturate(fogCoord);
    half3 fogColor = unity_FogColor.rgb;
    half master = half(_DragonLoot_AreaAmbientFogInfluence);
    if (master > 1e-4h && _DragonLoot_AreaAmbientEnabled > 0.5)
    {
        float4 area = DragonLootSampleAreaAmbientData(positionWS);
        half fogTint = half(area.a) * master;
        if (fogTint > 1e-4h)
            fogColor = lerp(fogColor, half3(area.rgb), fogTint);
    }
    return lerp(color, fogColor, fogFactor);
#else
    return color;
#endif
}

#endif
