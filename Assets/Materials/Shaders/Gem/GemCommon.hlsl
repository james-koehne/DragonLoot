#ifndef DRAGONLOOT_GEM_COMMON_INCLUDED
#define DRAGONLOOT_GEM_COMMON_INCLUDED

#include "GemInput.hlsl"

float GemHash11(float p)
{
    float3 p3 = frac(float3(p, p, p) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float GemHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float GemHash31(float3 p)
{
    float3 p3 = frac(p * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

half3 GemHueShift(half3 color, half shift)
{
    half3 k = half3(0.57735, 0.57735, 0.57735);
    half cosA = cos(shift);
    half sinA = sin(shift);
    return color * cosA + cross(k, color) * sinA + k * dot(k, color) * (1.0h - cosA);
}

half GemLuminance(half3 c)
{
    return dot(c, half3(0.2126h, 0.7152h, 0.0722h));
}

half3 GemAdjustSaturation(half3 color, half saturationScale)
{
    half luma = GemLuminance(color);
    return lerp(half3(luma, luma, luma), color, saturationScale);
}

// Unique per GPU-instanced matrix and per MeshRenderer transform.
float GemInstanceSeed()
{
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float seed = dot(origin, float3(12.9898, 78.233, 37.719));
#if defined(UNITY_INSTANCING_ENABLED)
    seed += (float)unity_InstanceID * 19.19;
#endif
    return GemHash11(seed);
}

struct GemVariation
{
    half brightnessScale;
    half saturationScale;
    half hueShift;
    half sparkleScale;
};

GemVariation GemBuildVariation(float seed)
{
    GemVariation v;
    half b = (half)GemHash11(seed);
    half s = (half)GemHash11(seed + 17.13);
    half h = (half)GemHash11(seed + 31.71);
    half sp = (half)GemHash11(seed + 79.31);

    v.brightnessScale = 1.0h + (b * 2.0h - 1.0h) * _BrightnessVariation;
    v.saturationScale = 1.0h + (s * 2.0h - 1.0h) * _SaturationVariation;
    // _HueVariation is authored as a fraction of the colour wheel (0.05 = ±5%).
    v.hueShift = (h * 2.0h - 1.0h) * _HueVariation * 6.283185h;
    v.sparkleScale = 1.0h + (sp * 2.0h - 1.0h) * _SparkleVariation;
    return v;
}

void GemApplyColorVariation(inout half3 color, GemVariation v)
{
    color = GemHueShift(color, v.hueShift);
    color = GemAdjustSaturation(color, v.saturationScale);
    color *= v.brightnessScale;
}

half GemSchlickFresnel(half ndotv, half power)
{
    half inv = 1.0h - saturate(ndotv);
    return pow(inv, max(power, 0.01h));
}

// Overlapping object-space planes create stable, angular colour breaks without textures.
half GemFacetPattern(float3 positionOS, half3 normalOS)
{
    float scale = max((float)_FacetScale, 1.0);
    float3 p = positionOS * scale;
    half planeA = (half)frac(p.x + p.y * 0.63 + p.z * 0.37);
    half planeB = (half)frac(-p.x * 0.71 + p.y * 0.43 + p.z);
    half cells = step(0.52h, planeA) * 0.55h + step(0.64h, planeB) * 0.45h;
    half orientation = saturate(dot(abs(normalOS), half3(0.2h, 0.75h, 0.4h)));
    return saturate(cells * 0.7h + orientation * 0.3h);
}

half GemInclusionDarken(float3 positionWS)
{
    if (_InclusionStrength <= 0.001h)
        return 1.0h;

    float3 cell = floor(positionWS * 18.0);
    half h = (half)GemHash31(cell);
    // Sparse dark flecks only.
    half fleck = smoothstep(0.92h, 0.98h, h);
    return 1.0h - fleck * _InclusionStrength * 0.35h;
}

// Sparse, hard glints reinforce the illustrated rather than physically refractive look.
half GemSparkle(
    float3 positionWS,
    half3 normalWS,
    half3 viewDirWS,
    half3 lightDirWS,
    float2 uv,
    half intensityScale)
{
    half intensity = _SparkleIntensity * intensityScale;
    if (intensity <= 0.001h)
        return 0.0h;

    float density = max((float)_SparkleDensity, 0.5);
    float3 cell = floor(positionWS * density);
    half rnd = (half)GemHash31(cell + float3(2.1, 7.3, 4.7));
    half mask = step(0.82h, rnd);

    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half lightGlint = saturate(dot(normalWS, halfDir));
    lightGlint = pow(lightGlint, max(_SparkleSharpness, 1.0h));

    return lightGlint * mask * intensity;
}

#endif
