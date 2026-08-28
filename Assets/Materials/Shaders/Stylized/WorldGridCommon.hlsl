#ifndef DRAGONLOOT_WORLD_GRID_COMMON_INCLUDED
#define DRAGONLOOT_WORLD_GRID_COMMON_INCLUDED

#include "EnvironmentLitGridInput.hlsl"

float WorldGridLineMask1D(float coord, float deriv, float cellSize)
{
    float cell = abs(frac(coord - 0.5) - 0.5);
    float halfWidth = max((_GridLineWidth * 0.5) / cellSize, 1e-5);
    float aa = max(deriv, 1e-5);
    return 1.0 - saturate(cell / (halfWidth + aa));
}

float WorldGridLineMask2D(float2 coord, float2 deriv, float cellSize)
{
    float lineX = WorldGridLineMask1D(coord.x, deriv.x, cellSize);
    float lineY = WorldGridLineMask1D(coord.y, deriv.y, cellSize);
    return saturate(max(lineX, lineY));
}

float3 WorldGridTriplanarWeights(float3 normalWS)
{
    float3 weights = abs(normalWS);
    weights = pow(weights, 4.0);
    return weights / max(dot(weights, 1.0), 1e-5);
}

float WorldGridTriplanarMask(float3 positionWS, float3 normalWS)
{
    float cellSize = max(_GridCellSize, 1e-5);
    float3 coord = (positionWS + _GridOffset.xyz) / cellSize;
    float3 weights = WorldGridTriplanarWeights(normalize(normalWS));
    float3 deriv = fwidth(coord);

    float maskX = WorldGridLineMask2D(coord.yz, deriv.yz, cellSize);
    float maskY = WorldGridLineMask2D(coord.xz, deriv.xz, cellSize);
    float maskZ = WorldGridLineMask2D(coord.xy, deriv.xy, cellSize);

    return saturate(maskX * weights.x + maskY * weights.y + maskZ * weights.z);
}

half3 WorldGridBlendAlbedo(half3 baseAlbedo, float3 positionWS, float3 normalWS)
{
    if (_GridEnabled < 0.5)
        return baseAlbedo;

    float gridMask = WorldGridTriplanarMask(positionWS, normalWS);
    half3 gridAlbedo = lerp(_GridCellColor.rgb, _GridLineColor.rgb, gridMask);
    half strength = saturate(_GridStrength);
    return lerp(baseAlbedo, gridAlbedo, strength);
}

#endif
