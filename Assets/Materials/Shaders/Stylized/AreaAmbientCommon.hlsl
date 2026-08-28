#ifndef DRAGONLOOT_AREA_AMBIENT_COMMON_INCLUDED
#define DRAGONLOOT_AREA_AMBIENT_COMMON_INCLUDED

TEXTURE3D(_DragonLoot_AreaAmbientTex);
SAMPLER(sampler_DragonLoot_AreaAmbientTex);

float3 _DragonLoot_AreaAmbientOrigin;
float3 _DragonLoot_AreaAmbientSize;
float _DragonLoot_AreaAmbientIntensity;
float _DragonLoot_AreaAmbientMix;
float _DragonLoot_AreaAmbientAlbedoInfluence;
float _DragonLoot_AreaAmbientEnabled;

half3 DragonLootSampleAreaAmbient(float3 positionWS)
{
    if (_DragonLoot_AreaAmbientEnabled <= 0.5)
        return half3(0, 0, 0);

    float3 worldSize = max(_DragonLoot_AreaAmbientSize, 1e-5);
    float3 uvw = (positionWS - _DragonLoot_AreaAmbientOrigin) / worldSize;

    float3 distOutside = max(-uvw, uvw - 1.0);
    float outside = max(max(distOutside.x, distOutside.y), distOutside.z);
    if (outside > 0.02)
        return half3(0, 0, 0);

    float edgeFade = 1.0 - saturate(outside / 0.02);

    half3 irradiance = SAMPLE_TEXTURE3D_LOD(
        _DragonLoot_AreaAmbientTex,
        sampler_DragonLoot_AreaAmbientTex,
        saturate(uvw),
        0).rgb;

    return irradiance * half(_DragonLoot_AreaAmbientIntensity) * half(edgeFade);
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

#endif
