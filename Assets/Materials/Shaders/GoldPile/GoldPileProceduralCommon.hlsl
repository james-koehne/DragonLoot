#ifndef DRAGONLOOT_GOLDPILE_PROCEDURAL_COMMON_INCLUDED
#define DRAGONLOOT_GOLDPILE_PROCEDURAL_COMMON_INCLUDED

#include "GoldPileProceduralInput.hlsl"

float ProcHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 ProcHash22(float2 p)
{
    float n = ProcHash21(p);
    return float2(n, ProcHash21(p + n + 19.19));
}

float4 ProcHash44(float2 p)
{
    float a = ProcHash21(p);
    float b = ProcHash21(p + 17.13);
    float c = ProcHash21(p + 41.71);
    float d = ProcHash21(p + 63.37);
    return float4(a, b, c, d);
}

half3 ProcHueShift(half3 color, half shift)
{
    half3 k = half3(0.57735, 0.57735, 0.57735);
    half cosA = cos(shift);
    half sinA = sin(shift);
    return color * cosA + cross(k, color) * sinA + k * dot(k, color) * (1.0h - cosA);
}

float SampleProcDeformHeight(float2 uv)
{
    float center = SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv, 0).r;
    float blur = (float)_DeformSampleBlur;
    if (blur <= 0.001)
        return center;

	// Cross blur in deform UV; blur amount is in heightfield texels.
    // Mesh UVs are texel centers ((i+0.5)/res), so one texel step is 1/res.
    float uvStep = rcp(max(_DeformResolution, 1.0)) * blur;
    float sum = center * 2.0;
    sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv + float2(uvStep, 0), 0).r;
    sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv - float2(uvStep, 0), 0).r;
    sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv + float2(0, uvStep), 0).r;
    sum += SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv - float2(0, uvStep), 0).r;
    return sum * (1.0 / 6.0);
}

float3 ApplyProcRuntimeDeform(float3 positionOS, float2 uv)
{
    if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
    {
        float h = SampleProcDeformHeight(uv);
        float worldH = h * _DeformScale;
        if (worldH < _GroundLevelHeight)
            positionOS.y = -1000.0;
        else
            positionOS.y += worldH;
    }
    return positionOS;
}

void ProcClipBelowGround(float2 deformUV)
{
    if (_DeformEnabled < 0.5 || _DeformScale <= 0.0)
        return;

    float h = SampleProcDeformHeight(deformUV) * _DeformScale;
    clip(h - _GroundLevelHeight);
}

half ProcLodFactor(float dist)
{
    // Continuous: 0 = near, 1 = mid, 2 = far. Feature weights fade — no dual-path blend.
    if (_DisableLod > 0.5h)
        return 0.0h;

    float nearD = (float)_LodNear;
    float midD = max((float)_LodMid, nearD + 0.01);
    float farD = max((float)_LodFar, midD + 0.01);

    half toMid = (half)smoothstep(nearD, midD, dist);
    half toFar = (half)smoothstep(midD, farD, dist);
    return toMid + toFar;
}

// 1 through mid, fades to 0 across mid→far (bump / dirt / fresnel / coin normals).
half ProcLodDetailFade(half lodBand)
{
    return saturate(2.0h - lodBand);
}

// Scales hash tint/value sparkle; stronger _LodNoiseFade kills variation sooner with distance.
half ProcLodNoiseKeep(half lodBand)
{
    return 1.0h - saturate(lodBand * 0.5h) * saturate(_LodNoiseFade);
}

float2 ProcRotate2(float2 v, float angle)
{
    float s;
    float c;
    sincos(angle, s, c);
    return float2(v.x * c - v.y * s, v.x * s + v.y * c);
}

float ProcHash31(float3 p)
{
    return ProcHash21(p.xy + float2(p.z * 19.19, p.z * 7.13));
}

float3 ProcHash33(float3 p)
{
    return float3(
        ProcHash21(p.xy + 17.13),
        ProcHash21(p.yz + 41.71),
        ProcHash21(p.zx + 63.37));
}

half ProcSchlickFresnel(half ndotv, half power)
{
    half inv = 1.0h - saturate(ndotv);
    return pow(inv, max(power, 0.01h));
}

// Stable tangent frame from surface normal (for circular discs on slopes).
void ProcSurfaceFrame(float3 normalWS, out float3 tangentWS, out float3 bitangentWS)
{
    float3 n = SafeNormalize(normalWS);
    float3 up = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangentWS = SafeNormalize(cross(up, n));
    bitangentWS = cross(n, tangentWS);
}

struct ProcCoinSurface
{
    half3 albedo;
    half3 normalTS;
    half3 normalWS;
    half metallic;
    half smoothness;
    half occlusion;
    half coverage;
    half burial;
};

struct ProcCoinWinner
{
    float3 cell;
    float2 local;
    float3 planar;
    float3 uvT;
    float3 rotT;
    float3 rotB;
    float3 coinFacing;
    float2 rnd2;
    float rnd3;
    half mask;
    half burial;
    half priority;
};

// Analytic coin height in 0..1 (disc radius space). Raised rim, recessed curved face.
float ProcCoinHeight01(float distNorm)
{
    float r = saturate(distNorm);
    float rimW = max((float)_RimBevelWidth, 0.02);
    float rimAmp = lerp(0.3, 1.0, saturate((float)_RimBevelStrength / 1.5));

    // Subtle dome across the face, kept below the rim peak.
    float face = 0.52 + 0.14 * (1.0 - r * r);

    // Raised bevel near the outer edge.
    float rimT = saturate((r - (1.0 - rimW)) / rimW);
    float rim = smoothstep(0.0, 0.32, rimT) * (1.0 - smoothstep(0.65, 1.05, rimT));
    rim *= rimAmp;

    float h = lerp(face, 1.0, rim);
    h *= 1.0 - smoothstep(0.98, 1.02, distNorm);
    return saturate(h);
}

#if defined(_POM_ON)
float2 ProcCoinApplyPom(
    float2 local,
    float3 viewDirWS,
    float3 rotT,
    float3 rotB,
    float3 coinFacing,
    float radius,
    float diameter,
    half detailFade)
{
    float pomHeight = (float)_PomHeight;
    if (pomHeight <= 1e-5 || detailFade <= 0.001h)
        return local;

    float heightScale = pomHeight * diameter;
    float3 viewTS = float3(
        dot(viewDirWS, rotT),
        dot(viewDirWS, rotB),
        dot(viewDirWS, coinFacing));

    float viewZ = max(abs(viewTS.z), 0.08);
    float2 viewXY = float2(viewTS.x, viewTS.y);
    float2 maxOffset = -viewXY / viewZ * heightScale;

    int maxSteps = (int)clamp(round((float)_PomSteps), 4.0, 12.0);
    float grazing = 1.0 - saturate(abs(viewTS.z));
    float stepF = lerp(4.0, (float)maxSteps, grazing) * (float)detailFade;
    int steps = (int)clamp(round(stepF), 4.0, (float)maxSteps);

    float2 delta = maxOffset / (float)steps;
    float layerDepth = rcp((float)steps);

    float2 currLocal = local;
    float currLayer = 0.0;
    float currH = ProcCoinHeight01(length(currLocal) / max(radius, 1e-4));

    [loop]
    for (int i = 0; i < 12; i++)
    {
        if (i >= steps)
            break;
        if (currLayer >= currH)
            break;
        currLocal += delta;
        currLayer += layerDepth;
        currH = ProcCoinHeight01(length(currLocal) / max(radius, 1e-4));
    }

    return currLocal;
}

// Cheap grazing rim expand during neighbour search (no POM march).
float2 ProcCoinSearchLocal(
    float2 local,
    float3 viewDirWS,
    float3 nWS,
    float3 uvT,
    float3 uvB,
    float diameter,
    half detailFade)
{
    float pomHeight = (float)_PomHeight;
    if (pomHeight <= 1e-5 || detailFade <= 0.001h)
        return local;

    float3 vPlanar = viewDirWS - nWS * dot(viewDirWS, nWS);
    float2 vL = float2(dot(vPlanar, uvT), dot(vPlanar, uvB));
    float vLenSq = dot(vL, vL);
    if (vLenSq <= 1e-10)
        return local;

    float2 vDir = vL * rsqrt(vLenSq);
    float grazing = 1.0 - saturate(abs(dot(viewDirWS, nWS)));
    float rimH = pomHeight * diameter * lerp(0.25, 1.0, saturate((float)_RimBevelStrength));
    return local - vDir * (rimH * grazing);
}
#endif

// World-XYZ coin lattice:
// Each coin has a fixed 3D center. Discs are measured in metres in the surface
// plane through that center — no axis projection, no stretch.
ProcCoinSurface SampleProcVirtualCoins(
    float3 positionWS,
    float3 surfaceNormalWS,
    float3 viewDirWS,
    float camDist,
    half lodBand)
{
    ProcCoinSurface result;
    result.albedo = _GapColor.rgb;
    result.normalTS = half3(0, 0, 1);
    result.normalWS = SafeNormalize(surfaceNormalWS);
    result.metallic = _GapMetallic;
    result.smoothness = _GapSmoothness;
    result.occlusion = 1;
    result.coverage = 0;
    result.burial = 1;

    float density = max((float)_CoinDensity, 1e-4);
    // Cubic cells sized so flat tops keep a similar packing to the old per-m² density.
    float cellSize = rcp(sqrt(density));
    float diameter = max((float)_CoinDiameter, 0.001);
    float radius = diameter * 0.5;
    // Only coins whose centers lie near the surface plane can paint it.
    float slice = max(radius * 1.25, cellSize * 0.55);

    float3 nWS = SafeNormalize(surfaceNormalWS);
    float3 tangentWS;
    float3 bitangentWS;
    ProcSurfaceFrame(nWS, tangentWS, bitangentWS);

    half detailFade = ProcLodDetailFade(lodBand);
    half noiseKeep = ProcLodNoiseKeep(lodBand);
    half useNormals = detailFade;
    half useDirt = detailFade;

    half dirtSample = 1.0h;
    if (useDirt > 0.001h)
        dirtSample = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, positionWS.xz * 0.35).r;

    float3 worldScaled = positionWS / cellSize;
    float3 baseCell = floor(worldScaled);

    bool hasCoin = false;
    ProcCoinWinner winner = (ProcCoinWinner)0;
    winner.priority = -1.0h;

    // Neighbourhood stays fixed; LOD fades features instead of changing loop cost mid-frame.
    const int extent = 1;

    [loop]
    for (int oz = -extent; oz <= extent; oz++)
    {
        [loop]
        for (int oy = -extent; oy <= extent; oy++)
        {
            [loop]
            for (int ox = -extent; ox <= extent; ox++)
            {
                float3 cell = baseCell + float3(ox, oy, oz);
                float3 rnd = ProcHash33(cell);
                float rndW = ProcHash31(cell + 11.17);
                float rnd3 = ProcHash31(cell + 53.97);

                float3 jitter = (rnd * 2.0 - 1.0) * _CellJitter * 0.5;
                // Fixed world-space center — never tied to the shaded pixel.
                float3 centerWS = (cell + 0.5 + jitter) * cellSize;

                float3 toPixel = positionWS - centerWS;
                float outOfPlane = dot(toPixel, nWS);
                // Coin must intersect the surface slab around this pixel.
                if (abs(outOfPlane) > slice)
                    continue;

                float3 planar = toPixel - nWS * outOfPlane;

                float angle = (rndW * 2.0 - 1.0) * _RotationRandomness * 3.14159265;
                float s;
                float c;
                sincos(angle, s, c);
                // Footprint / UVs stay in the surface plane so stamps stay circular.
                float3 uvT = tangentWS * c + bitangentWS * s;
                float3 uvB = -tangentWS * s + bitangentWS * c;

                float2 local = float2(dot(planar, uvT), dot(planar, uvB));
#if defined(_POM_ON)
                float2 searchLocal = ProcCoinSearchLocal(local, viewDirWS, nWS, uvT, uvB, diameter, detailFade);
#else
                float2 searchLocal = local;
#endif
                float distNorm = length(searchLocal) / max(radius, 1e-4);
                half mask = 1.0h - smoothstep(0.98h, 1.02h, (half)distNorm);
                if (mask < 0.001h)
                    continue;

                // Disc local → texture: scale zooms into the face, center shifts off-centered atlases.
                float2 discUV = local / max(diameter, 1e-4); // -0.5..0.5 over the coin disc
                float2 coinUV = discUV / max((float)_CoinUVScale, 0.001) + _CoinUVCenter.xy;
                if (coinUV.x < 0.0 || coinUV.x > 1.0 || coinUV.y < 0.0 || coinUV.y > 1.0)
                    continue;

                half burial = saturate((half)rnd.z * 0.85h + 0.15h) * _BurialAmount;
                // Per-coin layer from cell hash — winner is constant across the whole disc overlap.
                half priority = (half)ProcHash31(cell + 29.53);
                // Tiny mask bias only for AA edge ties on the same layer (never splits two coins mid-face).
                priority += mask * 1e-4h;
                if (hasCoin && priority <= winner.priority)
                    continue;

                // Per-coin lighting orientation: tilt facing away from the mound normal.
                float3 tiltRnd = ProcHash33(cell + 3.17);
                float3 tiltDir = tiltRnd * 2.0 - 1.0;
                tiltDir = tiltDir - nWS * dot(tiltDir, nWS);
                float tiltLenSq = dot(tiltDir, tiltDir);
                if (tiltLenSq > 1e-8)
                    tiltDir *= rsqrt(tiltLenSq);
                else
                    tiltDir = tangentWS;
                float tiltAmt = (float)_CoinTilt * (0.35 + 0.65 * rnd3);
                float3 coinFacing = SafeNormalize(nWS + tiltDir * tiltAmt);

                float3 coinT;
                float3 coinB;
                ProcSurfaceFrame(coinFacing, coinT, coinB);
                float3 rotT = coinT * c + coinB * s;
                float3 rotB = -coinT * s + coinB * c;

                hasCoin = true;
                winner.cell = cell;
                winner.local = local;
                winner.planar = planar;
                winner.uvT = uvT;
                winner.rotT = rotT;
                winner.rotB = rotB;
                winner.coinFacing = coinFacing;
                winner.rnd2 = ProcHash22(cell.xy + cell.z + 91.17);
                winner.rnd3 = rnd3;
                winner.mask = mask;
                winner.burial = burial;
                winner.priority = priority;
            }
        }
    }

    half3 gapAlbedo = _GapColor.rgb;
    if (useDirt > 0.001h && _DirtStrength > 0.001h)
    {
        half gapDark = saturate(0.55h + dirtSample * 0.25h);
        gapAlbedo = _GapColor.rgb * lerp(1.0h, gapDark, saturate(_DirtStrength) * useDirt);
    }

    if (!hasCoin)
    {
        result.albedo = gapAlbedo;
        result.occlusion = saturate(1.0h - _AOStrength * 0.65h);
        result.metallic = _GapMetallic;
        result.smoothness = _GapSmoothness;
        result.normalWS = nWS;
        result.coverage = 0;
        result.burial = 1;
        return result;
    }

    // Shade winning coin once (POM + texture samples).
    float2 shadeLocal = winner.local;
#if defined(_POM_ON)
    shadeLocal = ProcCoinApplyPom(
        winner.local,
        viewDirWS,
        winner.rotT,
        winner.rotB,
        winner.coinFacing,
        radius,
        diameter,
        detailFade);
#endif
    float distNorm = length(shadeLocal) / max(radius, 1e-4);
    float2 discUV = shadeLocal / max(diameter, 1e-4);
    float2 coinUV = discUV / max((float)_CoinUVScale, 0.001) + _CoinUVCenter.xy;
    coinUV = saturate(coinUV);

    half exposed = saturate(1.0h - winner.burial);

    half copperW = saturate(_CopperAmount);
    half silverW = saturate(_SilverAmount);
    half goldW = max(0.001h, 1.0h - copperW - silverW);
    half metalPick = (half)ProcHash31(winner.cell + 77.7) * (goldW + copperW + silverW);
    half3 metalTint = _BaseColor.rgb;
    half metalMet = _Metallic;
    half metalSm = _Smoothness;
    half4 albedoSample;
    if (metalPick < silverW)
    {
        albedoSample = SAMPLE_TEXTURE2D(_SilverBaseMap, sampler_SilverBaseMap, coinUV);
        metalTint = _SilverColor.rgb;
        metalMet = 0.95h;
        metalSm = 0.80h;
    }
    else if (metalPick < silverW + copperW)
    {
        albedoSample = SAMPLE_TEXTURE2D(_CopperBaseMap, sampler_CopperBaseMap, coinUV);
        metalTint = _CopperColor.rgb;
        metalMet = 0.85h;
        metalSm = 0.55h;
    }
    else
    {
        albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, coinUV);
    }

    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, coinUV);

    half tint = 1.0h + ((half)winner.rnd2.x * 2.0h - 1.0h) * _TintVariation * noiseKeep;
    half value = 1.0h + ((half)winner.rnd2.y * 2.0h - 1.0h) * _ValueVariation * noiseKeep;
    half3 albedo = albedoSample.rgb * metalTint * tint * value;
    half warm = ((half)winner.rnd3 * 2.0h - 1.0h) * 0.03h * noiseKeep;
    albedo += half3(warm, warm * 0.35h, -warm);

    half metallic = saturate(maskSample.r * metalMet);
    half smoothness = saturate(maskSample.a * metalSm);

    half rimWidth = max(_RimBevelWidth, 0.001h);
    half rimFactor = smoothstep(1.0h - rimWidth, 1.0h, (half)distNorm);

    if (_RimAoStrength > 0.001h)
    {
        half rimAo = rimFactor * _RimAoStrength;
        albedo *= saturate(1.0h - rimAo * 0.55h);
        smoothness = saturate(smoothness * (1.0h - rimAo * 0.4h));
    }

    if (_EdgeHighlightStrength > 0.001h && detailFade > 0.001h)
    {
        half edgeWidth = max(_EdgeWidth, 0.001h);
        half edgeCenter = 1.0h - edgeWidth * 0.5h;
        half rim = saturate(1.0h - abs((half)distNorm - edgeCenter) / edgeWidth);
        rim = rim * rim;
        albedo += albedo * rim * _EdgeHighlightStrength * 0.35h * exposed * detailFade * noiseKeep;
    }

    half3 normalTS = half3(0, 0, 1);
    half3 coinNormalWS = winner.coinFacing;
    if (useNormals > 0.001h)
    {
        normalTS = UnpackNormalScale(
            SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, coinUV),
            _BumpScale * _CoinNormalStrength * useNormals);
        coinNormalWS = SafeNormalize(
            normalTS.x * winner.rotT + normalTS.y * winner.rotB + normalTS.z * winner.coinFacing);
        coinNormalWS = SafeNormalize(lerp(winner.coinFacing, coinNormalWS, useNormals));
        normalTS = lerp(half3(0, 0, 1), normalTS, useNormals);
    }

    // Height-gradient tilt so recessed face / rim curve catch light.
#if defined(_POM_ON)
    if (_PomHeight > 1e-5h && detailFade > 0.001h)
    {
        float eps = max(radius * 0.04, 1e-5);
        float hC = ProcCoinHeight01(distNorm);
        float hX = ProcCoinHeight01(length(shadeLocal + float2(eps, 0.0)) / max(radius, 1e-4));
        float hY = ProcCoinHeight01(length(shadeLocal + float2(0.0, eps)) / max(radius, 1e-4));
        float heightScale = (float)_PomHeight * diameter;
        float2 grad = float2(hC - hX, hC - hY) / eps * heightScale * (float)detailFade;
        coinNormalWS = SafeNormalize(coinNormalWS + winner.rotT * grad.x + winner.rotB * grad.y);
    }
#endif

    // Fake rim thickness: bend normals outward near the disc edge.
    half rimBevel = _RimBevelStrength * detailFade;
    if (rimBevel > 0.001h && rimFactor > 0.001h)
    {
        float planarLenSq = dot(winner.planar, winner.planar);
        float3 outward = planarLenSq > 1e-10
            ? winner.planar * rsqrt(planarLenSq)
            : winner.uvT;
        coinNormalWS = SafeNormalize(coinNormalWS + outward * (float)(rimFactor * rimBevel));
    }

    half occ = 1.0h;
    if (_RimAoStrength > 0.001h)
        occ = saturate(1.0h - rimFactor * _RimAoStrength);

    if (useDirt > 0.001h && _DirtStrength > 0.001h)
    {
        half dirt = saturate(dirtSample * _DirtStrength * 0.35h) * useDirt;
        albedo = lerp(albedo, albedo * half3(0.55h, 0.42h, 0.28h), dirt);
        metallic = saturate(metallic * (1.0h - dirt * 0.55h));
        smoothness = saturate(smoothness * (1.0h - dirt * 0.4h));
    }

    if (exposed < 0.999h)
        albedo = lerp(_GapColor.rgb, albedo, exposed);

    half cover = winner.mask;
    half metalCover = cover > 0.5h ? 1.0h : 0.0h;
    result.albedo = lerp(gapAlbedo, albedo, cover);
    result.normalTS = lerp(half3(0, 0, 1), normalTS, cover);
    result.normalWS = SafeNormalize(lerp(nWS, coinNormalWS, cover));
    result.metallic = lerp(_GapMetallic, metallic, metalCover);
    result.smoothness = lerp(_GapSmoothness, smoothness, cover);
    half gapOcc = saturate(1.0h - _AOStrength * 0.65h);
    result.occlusion = lerp(gapOcc, occ, metalCover);
    result.coverage = cover;
    result.burial = winner.burial;

    return result;
}

half3 ProcDeformNormalWS(float2 deformUV, half3 fallbackNormalWS)
{
    if (_DeformEnabled < 0.5h || _DeformScale <= 0.0h || _DeformResolution <= 1.0)
        return fallbackNormalWS;

    float soften = saturate((float)_DeformNormalSoften);
    // Larger finite-difference step kills texel-scale creases in the lighting normal.
    float stepScale = 1.0 + soften * 3.0;
    float uvStep = rcp(max(_DeformResolution, 1.0)) * stepScale;
    half heightLeft = SAMPLE_TEXTURE2D(_DeformMap, sampler_DeformMap, deformUV - float2(uvStep, 0)).r;
    half heightRight = SAMPLE_TEXTURE2D(_DeformMap, sampler_DeformMap, deformUV + float2(uvStep, 0)).r;
    half heightDown = SAMPLE_TEXTURE2D(_DeformMap, sampler_DeformMap, deformUV - float2(0, uvStep)).r;
    half heightUp = SAMPLE_TEXTURE2D(_DeformMap, sampler_DeformMap, deformUV + float2(0, uvStep)).r;

    float worldStep = max(_DeformWorldSize * uvStep, 1e-4);
    float3 normalOS = SafeNormalize(float3(
        (heightLeft - heightRight) * _DeformScale,
        worldStep * 2.0,
        (heightDown - heightUp) * _DeformScale));
    half3 normalWS = SafeNormalize(TransformObjectToWorldNormal(normalOS));
    // Soften also blends toward mesh/up fallback so specular ignores micro-creases.
    half3 softTarget = SafeNormalize(fallbackNormalWS);
    return SafeNormalize(lerp(normalWS, softTarget, (half)soften));
}

// Distant coin impostor: world-anchored palette marks that add screen pixels
// in a compact spiral, reaching a complete 3x3 as the camera approaches.
half3 ProcDistantCoinPixels(
    float3 positionWS,
    float4 positionCS,
    float2 deformUV,
    float3 normalWS,
    float3 viewDirWS,
    float3 lightDirWS,
    float camDist)
{
    float density = max((float)_DistantCoinDensity, 0.01);
    float2 worldUV = positionWS.xz * density;

    // Evaluate derivatives before any per-fragment branches. They form a local
    // world-to-screen Jacobian used to project a fixed world anchor to pixels.
    float2 worldDx = ddx(worldUV);
    float2 worldDy = ddy(worldUV);
    float determinant = worldDx.x * worldDy.y - worldDy.x * worldDx.y;

    if (_DistantCoinEnabled < 0.5h || _DistantCoinIntensity <= 0.001h)
        return 0;

    float fadeStart = max((float)_DistantCoinFadeStart, (float)_DistantCoinFadeEnd + 0.01);
    float fadeEnd = (float)_DistantCoinFadeEnd;
    half nearFade = saturate((camDist - fadeEnd) / (fadeStart - fadeEnd));
    if (nearFade <= 0.001h)
        return 0;

    half topMask = saturate(normalWS.y);
    topMask = lerp(1.0h, topMask, saturate(_DistantCoinTopMask));
    if (topMask <= 0.001h)
        return 0;

    float growFar = max((float)_DistantCoinGrowFar, (float)_DistantCoinGrowNear + 0.01);
    float growNear = max((float)_DistantCoinGrowNear, fadeStart);
    // 0 at/beyond growFar (1px), 1 at growNear (selected maximum).
    half growT = saturate((growFar - camDist) / (growFar - growNear));

    float2 cell = floor(worldUV);
    float2 f = frac(worldUV);

    float rnd = ProcHash21(cell);
    float rnd2 = ProcHash21(cell + 17.13);
    float rnd3 = ProcHash21(cell + 41.71);

    half coverage = saturate(_DistantCoinCoverage);
    if (rnd > coverage)
        return 0;

    // Stable world anchor inside the cell.
    float rnd4 = ProcHash21(cell + 63.37);
    float2 corner = float2(rnd2, rnd3);
    float2 growSign = float2(rnd4 > 0.5 ? 1.0 : -1.0, ProcHash21(cell + 91.17) > 0.5 ? 1.0 : -1.0);

    if (abs(determinant) <= 1e-7)
        return 0;

    // Solve the local Jacobian to locate the world anchor in screen pixels,
    // then snap it once to the hardware pixel grid.
    float2 anchorDeltaUV = f - corner;
    float invDeterminant = rcp(determinant);
    float2 anchorDeltaPixels = float2(
        (anchorDeltaUV.x * worldDy.y - anchorDeltaUV.y * worldDy.x) * invDeterminant,
        (worldDx.x * anchorDeltaUV.y - worldDx.y * anchorDeltaUV.x) * invDeterminant);
    float2 anchorPixel = floor(positionCS.xy - anchorDeltaPixels) + 0.5;
    float2 fragmentPixel = floor(positionCS.xy) + 0.5;
    float2 localPixel = (fragmentPixel - anchorPixel) * growSign;

    int px = (int)round(localPixel.x);
    int py = (int)round(localPixel.y);
    if (px < -1 || px > 1 || py < -1 || py > 1)
        return 0;

    // Compact spiral: the first four pixels form a 2x2, then the remaining
    // five fill its left and lower edges to complete a 3x3.
    int pixelOrder = 0;
    if (px == 0 && py == 0)
        pixelOrder = 1;
    else if (px == 1 && py == 0)
        pixelOrder = 2;
    else if (px == 1 && py == 1)
        pixelOrder = 3;
    else if (px == 0 && py == 1)
        pixelOrder = 4;
    else if (px == -1 && py == 1)
        pixelOrder = 5;
    else if (px == -1 && py == 0)
        pixelOrder = 6;
    else if (px == -1 && py == -1)
        pixelOrder = 7;
    else if (px == 0 && py == -1)
        pixelOrder = 8;
    else if (px == 1 && py == -1)
        pixelOrder = 9;

    int maxPixels = (int)clamp(round(_DistantCoinMaxPixels), 1.0h, 9.0h);
    int stageI = min(1 + (int)floor(growT * maxPixels), maxPixels);
    half mask = pixelOrder > 0 && pixelOrder <= stageI ? 1.0h : 0.0h;

    if (mask <= 0.001h)
        return 0;

    half amount0 = max(_DistantCoinAmount0, 0.0h);
    half amount1 = max(_DistantCoinAmount1, 0.0h);
    half amount2 = max(_DistantCoinAmount2, 0.0h);
    half amount3 = max(_DistantCoinAmount3, 0.0h);
    half totalAmount = amount0 + amount1 + amount2 + amount3;
    if (totalAmount <= 0.001h)
        return 0;

    // Relative weights only affect colour selection; anchors remain unchanged.
    half colourChoice = (half)rnd3 * totalAmount;
    half3 colour = _DistantCoinColor0.rgb;
    half run = amount0;
    if (colourChoice >= run) { colour = _DistantCoinColor1.rgb; run += amount1; }
    if (colourChoice >= run) { colour = _DistantCoinColor2.rgb; run += amount2; }
    if (colourChoice >= run) { colour = _DistantCoinColor3.rgb; }

    // Slight value jitter so a field of marks doesn't look uniform.
    half value = lerp(0.85h, 1.15h, (half)ProcHash21(cell + 7.77));
    colour *= value;

    half visibility = nearFade * topMask * mask;

    // Optional: concentrate marks near where a metallic specular lobe would fire.
    if (_DistantCoinMetalFocusEnabled > 0.5h && _DistantCoinMetalFocus > 0.001h)
    {
        half3 focusNormalWS = ProcDeformNormalWS(deformUV, normalWS);
        half3 viewDir = SafeNormalize(viewDirWS);
        half3 lightDir = SafeNormalize(lightDirWS);
        half3 halfDir = SafeNormalize(viewDir + lightDir);
        half ndotH = saturate(dot(focusNormalWS, halfDir));
        half focusPower = max(_DistantCoinMetalFocusPower, 1.0h);
        half shineMask = pow(max(ndotH, 1e-4h), focusPower);
        visibility *= lerp(1.0h, shineMask, saturate(_DistantCoinMetalFocus));
        if (visibility <= 0.001h)
            return 0;
    }

    return colour * _DistantCoinIntensity * visibility;
}

#endif
