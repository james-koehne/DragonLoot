#ifndef DRAGONLOOT_GOLDPILE_COMMON_INCLUDED
#define DRAGONLOOT_GOLDPILE_COMMON_INCLUDED

#include "GoldPileInput.hlsl"

float GoldPileHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 GoldPileHash22(float2 p)
{
    float n = GoldPileHash21(p);
    return float2(n, GoldPileHash21(p + n + 19.19));
}

half3 GoldPileHueShift(half3 color, half shift)
{
    half3 k = half3(0.57735, 0.57735, 0.57735);
    half cosA = cos(shift);
    half sinA = sin(shift);
    return color * cosA + cross(k, color) * sinA + k * dot(k, color) * (1.0h - cosA);
}

float2 GoldPileObjectHeightUV(float3 positionOS)
{
    float tiling = _WorldTiling * max(_HeightMapTiling, 0.0001);
    return positionOS.xz * tiling + 0.5;
}

float SampleGoldPileHeightOS(float3 positionOS)
{
    if (_HeightMapEnabled < 0.5)
        return 0.5;

    float2 uv = GoldPileObjectHeightUV(positionOS);
    float h = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, uv, 0).r;
    if (_HeightMapInvert > 0.5)
        h = 1.0 - h;
    return h;
}

float3 ApplyGoldPileRuntimeDeform(float3 positionOS, float2 uv)
{
    if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
    {
        float h = SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, uv, 0).r;
        float worldH = h * _DeformScale;
        if (worldH < _GroundLevelHeight)
            positionOS.y = -1000.0;
        else
            positionOS.y += worldH;
    }
    return positionOS;
}

// Discards fragments where the runtime heightfield is below ground level (empty floor).
void GoldPileClipBelowGround(float2 deformUV)
{
    if (_DeformEnabled < 0.5 || _DeformScale <= 0.0)
        return;

    float h = SAMPLE_TEXTURE2D_LOD(_DeformMap, sampler_DeformMap, deformUV, 0).r * _DeformScale;
    clip(h - _GroundLevelHeight);
}

float3 ApplyGoldPileVertexDisplacement(float3 positionOS, float2 uv)
{
    // Runtime pile shape (0..1 absolute height) takes priority when enabled.
    if (_DeformEnabled > 0.5 && _DeformScale > 0.0)
        return ApplyGoldPileRuntimeDeform(positionOS, uv);

    if (_HeightMapEnabled < 0.5 || _HeightScale * _HeightAmount <= 0.0)
        return positionOS;

    float hAuthored = SampleGoldPileHeightOS(positionOS);
    positionOS.y += (hAuthored - 0.5) * _HeightScale * _HeightAmount;
    return positionOS;
}

float3 ApplyGoldPileVertexDisplacement(float3 positionOS)
{
    return ApplyGoldPileVertexDisplacement(positionOS, GoldPileObjectHeightUV(positionOS));
}

// View direction in the world XZ planar tangent frame matching GetGoldPilePlanarUV / triUV.uvY.
// Mesh tangents do not match world-space UVs, so they must not drive parallax.
float3 GoldPileWorldPlanarViewDirTS(float3 positionWS)
{
    float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
    // U = world X, V = world Z, N = world Y (top-down planar mapping)
    return float3(viewWS.x, viewWS.z, viewWS.y);
}

float3 GoldPileWorldPlanarLightDirTS(float3 lightDirWS)
{
    return float3(lightDirWS.x, lightDirWS.z, lightDirWS.y);
}

struct GoldPileTriplanarUV
{
    float2 uvX;
    float2 uvY;
    float2 uvZ;
    half3 weights;
};

GoldPileTriplanarUV GetGoldPileTriplanarUV(float3 positionWS, float3 normalWS)
{
    GoldPileTriplanarUV data;
    float3 absN = abs(normalWS);
    absN = pow(absN, max(_TriplanarSharpness, 0.01));
    absN /= max(absN.x + absN.y + absN.z, 1e-5);

    float3 scaled = positionWS * _WorldTiling;
    data.uvX = scaled.zy;
    data.uvY = scaled.xz;
    data.uvZ = scaled.xy;
    data.weights = half3(absN);
    return data;
}

float2 GetGoldPilePlanarUV(float3 positionWS)
{
    return positionWS.xz * _WorldTiling;
}

half4 SampleGoldPileTriplanarAlbedo(GoldPileTriplanarUV uv)
{
#if defined(_TRIPLANAR)
    half4 cx = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.uvX);
    half4 cy = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.uvY);
    half4 cz = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.uvZ);
    return cx * uv.weights.x + cy * uv.weights.y + cz * uv.weights.z;
#else
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv.uvY);
#endif
}

half4 SampleGoldPileTriplanarTex(TEXTURE2D_PARAM(tex, samplerTex), GoldPileTriplanarUV uv)
{
#if defined(_TRIPLANAR)
    half4 cx = SAMPLE_TEXTURE2D(tex, samplerTex, uv.uvX);
    half4 cy = SAMPLE_TEXTURE2D(tex, samplerTex, uv.uvY);
    half4 cz = SAMPLE_TEXTURE2D(tex, samplerTex, uv.uvZ);
    return cx * uv.weights.x + cy * uv.weights.y + cz * uv.weights.z;
#else
    return SAMPLE_TEXTURE2D(tex, samplerTex, uv.uvY);
#endif
}

half3 SampleGoldPileCoinNormalTS(GoldPileTriplanarUV uv, half scale)
{
#if defined(_TRIPLANAR)
    half3 nx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv.uvX), scale);
    half3 ny = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv.uvY), scale);
    half3 nz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv.uvZ), scale);
    return normalize(nx * uv.weights.x + ny * uv.weights.y + nz * uv.weights.z);
#else
    return UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv.uvY), scale);
#endif
}

half GoldPileLodFactor(float dist)
{
    // 0 = near (full), 1 = mid, 2 = far
    if (_DisableLod > 0.5h)
        return 0.0h;

    float midStart = _LodNear;
    float farStart = max(_LodFar, _LodNear + 0.01);
    if (dist < midStart)
        return 0.0h;
    if (dist < farStart)
        return 1.0h;
    return 2.0h;
}

half GoldPilePomFade(float dist)
{
    float fade = saturate(1.0 - (dist - _PomFadeStart) / max(_PomFadeEnd - _PomFadeStart, 0.01));
    return fade;
}

// Remap packed height into a sharp 0..1 relief field (1 = raised / toward camera).
// Matches Unity height maps: white = high, black = low.
// https://docs.unity3d.com/Manual/StandardShaderMaterialParameterHeightMap.html
half RemapGoldPilePomHeight(half raw)
{
    if (_PomInvertHeight > 0.5h)
        raw = 1.0h - raw;

    raw = saturate((raw - 0.5h) * _PomHeightContrast + 0.5h + _PomHeightBias);
    return raw;
}

half SampleGoldPileHeightMapRaw(float2 uv)
{
    if (_HeightMapEnabled < 0.5h)
        return 0.5h;

    // Same UV space as planar / triUV.uvY (world XZ * tiling), then optional height-only scale.
    float2 heightUV = uv * max(_HeightMapTiling, 0.0001);
    half h = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, heightUV).r;
    if (_HeightMapInvert > 0.5h)
        h = 1.0h - h;

    h = pow(saturate(h), max(_HeightMapPower, 0.01h));
    // Intensity scales relief around mid-grey; allow >1 to exaggerate.
    h = saturate(0.5h + (h - 0.5h) * _HeightMapIntensity);
    return h;
}

half SampleGoldPileCoinNormalHeight(float2 uv)
{
    half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), 1.0h);
    half hNormal = saturate(nTS.z * 0.5h + 0.5h);
    return saturate(hNormal - length(nTS.xy) * 0.35h);
}

// Height field for parallax. When height map is off, returns flat mid (no parallax relief).
half SampleGoldPilePomHeight(float2 uv)
{
    if (_HeightMapEnabled < 0.5h)
        return 0.5h;

    half hMap = SampleGoldPileHeightMapRaw(uv);
    half blended = hMap;
    if (_PomNormalHeight > 0.001h)
    {
        half hNormal = SampleGoldPileCoinNormalHeight(uv);
        blended = lerp(hMap, hNormal, _PomNormalHeight);
    }
    return RemapGoldPilePomHeight(blended);
}

struct GoldPilePomResult
{
    float2 uv;
    float2 uvDelta;
    half height;
    half selfShadow;
};

GoldPilePomResult GoldPileParallaxOcclusion(float2 uv, float3 viewDirTS, float3 lightDirTS, float fade)
{
    GoldPilePomResult result;
    result.uv = uv;
    result.uvDelta = 0;
    result.height = 1;
    result.selfShadow = 1;

    if (fade <= 0.001 || _Parallax <= 1e-5)
        return result;

    // Stabilize grazing angles — huge deltas cause the "ghost sheet" smear.
    float3 vd = viewDirTS;
    vd.z = max(vd.z, 0.15);
    vd = normalize(vd);

    // Scale matches Unity height-map parallax amplitude.
    float intensityGate = max((float)_HeightMapIntensity, 0.0);
    float parallaxScale = _Parallax * fade * intensityGate;
    if (parallaxScale <= 1e-6)
        return result;

    int steps = (int)clamp(_POMSteps, 8.0, 32.0);
    steps = (int)max(8.0, round(steps * saturate(0.35 + fade)));

    float layerDepth = 1.0 / steps;
    float currentLayerDepth = 0.0;
    float2 deltaUV = (vd.xy / vd.z) * parallaxScale;
    deltaUV /= steps;

    float2 currentUV = uv;
    float currentDepthMapValue = SampleGoldPilePomHeight(currentUV);

    [loop]
    for (int i = 0; i < 32; i++)
    {
        if (i >= steps)
            break;
        if (currentLayerDepth >= currentDepthMapValue)
            break;

        currentUV -= deltaUV;
        currentDepthMapValue = SampleGoldPilePomHeight(currentUV);
        currentLayerDepth += layerDepth;
    }

    float2 prevUV = currentUV + deltaUV;
    float after = currentDepthMapValue - currentLayerDepth;
    float before = SampleGoldPilePomHeight(prevUV) - currentLayerDepth + layerDepth;
    float weight = saturate(after / (after - before + 1e-5));
    result.uv = lerp(currentUV, prevUV, weight);
    result.uvDelta = result.uv - uv;
    result.height = SampleGoldPilePomHeight(result.uv);

    // Soft self-shadow along light — sells carved coin edges without a second pass.
    if (_PomSelfShadow > 0.001h && lightDirTS.z > 0.05)
    {
        float3 ld = normalize(float3(lightDirTS.xy, max(lightDirTS.z, 0.1)));
        float2 shadowStep = (ld.xy / ld.z) * parallaxScale / 8.0;
        float shadowDepth = result.height;
        half shadow = 1.0h;

        [unroll]
        for (int s = 1; s <= 8; s++)
        {
            float2 sUV = result.uv + shadowStep * s;
            float sampleH = SampleGoldPilePomHeight(sUV);
            shadowDepth += layerDepth * (steps / 8.0);
            if (sampleH > shadowDepth)
            {
                half penumbra = saturate((sampleH - shadowDepth) * 4.0h);
                shadow = min(shadow, 1.0h - penumbra);
            }
        }

        result.selfShadow = lerp(1.0h, shadow, _PomSelfShadow * fade);
    }

    return result;
}

void ApplyGoldPileColourVariation(float2 worldXZ, inout half3 albedo, inout half smoothness)
{
    float h = GoldPileHash21(floor(worldXZ * 4.0));
    half hue = (h * 2.0h - 1.0h) * _HueVariation;
    half bright = 1.0h + (h - 0.5h) * 2.0h * _BrightnessVariation;
    half rough = (GoldPileHash21(worldXZ * 7.13) - 0.5h) * 2.0h * _RoughnessVariation;

    albedo = GoldPileHueShift(albedo, hue * 6.283185h) * bright;
    smoothness = saturate(smoothness - rough);
}

half GoldPileEdgeMask(float3 normalWS, float heightSample)
{
    half steep = 1.0h - saturate(abs(normalWS.y));
    half thin = 1.0h - heightSample;
    return saturate((steep * 0.65h + thin * 0.35h) * _EdgeDirtStrength);
}

half GoldPileSparkleCell(
    float2 cell,
    float2 cellUV,
    float2 worldXZScaled,
    half coverage,
    half sizeExp,
    half3 normalWS,
    float3 viewDirWS,
    float3 lightDirWS,
    half metallic,
    half intensity,
    half flicker,
    float speed,
    half fresnelAmt)
{
    float rnd = GoldPileHash21(cell);
    float rnd2 = GoldPileHash21(cell + 17.13);

    // Soft coverage instead of hard step — avoids on/off cell popping.
    half threshold = 1.0h - saturate(coverage);
    half softGate = max(0.02h, _SparkleSoftness * 0.2h);
    half mask = smoothstep(threshold - softGate, threshold + softGate, rnd);
    if (mask < 0.001h)
        return 0;

    float2 center = float2(rnd, rnd2);
    float d = distance(cellUV, center);

    // Screen-space minimum radius so glints never shrink below ~N pixels (kills crawl noise).
    float2 fw = fwidth(worldXZScaled);
    float pixelMetric = max(fw.x, fw.y);
    float authoredRadius = lerp(0.08, 0.35, saturate(rnd2)) * lerp(0.55h, 1.35h, _SparkleSoftness);
    float minRadius = max(_SparkleMinPixelWidth * pixelMetric, 1e-4);
    float radius = max(authoredRadius, minRadius);
    // Conserve energy when we inflate sub-pixel sparkles.
    float energy = saturate((authoredRadius * authoredRadius) / (radius * radius + 1e-6));
    energy = lerp(1.0, energy, _SparkleStability);

    half edge = saturate(1.0h - d / radius);
    // Soft gaussian-ish falloff; higher softness = broader tails.
    half falloffExp = lerp(3.5h, 1.25h, _SparkleSoftness);
    half sparkleSpot = pow(edge, falloffExp) * mask;

    float t = _TimeParameters.x * speed + rnd * 6.283185;
    half pulse = sin(t) * 0.5h + 0.5h;
    // Stability damps temporal flicker so motion AA isn't fighting the animation.
    half flickerAmt = flicker * (1.0h - _SparkleStability * 0.75h);
    pulse = lerp(1.0h, pulse * pulse, flickerAmt);

    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half nh = saturate(dot(normalWS, halfDir));
    half nv = saturate(dot(normalWS, viewDirWS));
    // Soften specular lobe slightly when expanded for AA.
    half sizeAdj = sizeExp * lerp(1.0h, 0.65h, saturate(minRadius / (authoredRadius + 1e-4)));
    half spec = pow(nh, max(sizeAdj, 4.0h));
    half viewGlint = pow(nv, max(sizeAdj, 4.0h) * 0.35h);
    half fresnel = pow(1.0h - nv, 3.0h) * fresnelAmt;

    half glitter = (spec + viewGlint * 0.35h + fresnel * spec) * sparkleSpot * pulse;
    glitter *= saturate(0.35h + metallic);
    return glitter * intensity * energy;
}

// Soft blanket of fine glitter across the whole surface (not sparse cells only).
half GoldPileSparkleGlobal(float3 positionWS, float3 normalWS, float3 viewDirWS, float3 lightDirWS, half metallic, half intensityMul)
{
    if (_SparkleGlobalIntensity <= 0.001h)
        return 0;

    float density = max((float)_SparkleGlobalDensity, 0.01);
    float2 uv = positionWS.xz * density;

    // Limit density when features would be sub-pixel — reduces crawling noise.
    float2 fw = fwidth(uv);
    float pixelMetric = max(fw.x, fw.y);
    float densityScale = saturate(1.0 / max(pixelMetric * _SparkleMinPixelWidth, 1.0));
    densityScale = lerp(1.0, densityScale, _SparkleStability);
    uv *= densityScale;

    float2 cell = floor(uv);
    float2 f = frac(uv);

    float n0 = GoldPileHash21(cell);
    float n1 = GoldPileHash21(cell + float2(1, 0));
    float n2 = GoldPileHash21(cell + float2(0, 1));
    float n3 = GoldPileHash21(cell + float2(1, 1));
    float2 ff = f * f * (3.0 - 2.0 * f);
    float noise = lerp(lerp(n0, n1, ff.x), lerp(n2, n3, ff.x), ff.y);

    // Soften micro contrast with screen derivatives.
    float noiseFw = fwidth(noise);
    float microSharp = lerp(6.0, 2.0, _SparkleSoftness);
    half micro = pow(saturate(noise), microSharp);
    micro = lerp(micro, smoothstep(0.35 - noiseFw, 0.85 + noiseFw, noise), _SparkleSoftness);

    float t = _TimeParameters.x * _SparkleGlobalSpeed * (1.0 - _SparkleStability * 0.5) + noise * 6.283185;
    half pulse = sin(t) * 0.5h + 0.5h;
    half flickerAmt = _SparkleFlicker * (1.0h - _SparkleStability * 0.75h);
    pulse = lerp(1.0h, pulse, flickerAmt);

    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half nh = saturate(dot(normalWS, halfDir));
    half nv = saturate(dot(normalWS, viewDirWS));
    half sizeExp = max(_SparkleGlobalSize, 4.0h) * lerp(1.0h, 0.7h, _SparkleSoftness);
    half spec = pow(nh, sizeExp);

    half glitter = spec * micro * pulse;
    glitter *= saturate(0.25h + metallic);
    glitter *= _SparkleGlobalIntensity * intensityMul;
    glitter *= 1.0h + pow(nv, max(_SparkleFacingPower, 0.01h)) * _SparkleFacingIntensity * 0.35h;
    return glitter;
}

half3 GoldPileSparkle(float3 positionWS, float3 normalWS, float3 viewDirWS, float3 lightDirWS, half metallic, float camDist)
{
    if (_SparkleEnabled < 0.5h)
        return 0;

    float nearStart = _SparkleNearFadeStart;
    float nearEnd = max(_SparkleNearFadeEnd, nearStart + 0.01);
    half nearFactor = saturate((camDist - nearStart) / (nearEnd - nearStart));
    half intensityMul = lerp(_SparkleNearIntensity, 1.0h, nearFactor);
    half coverageMul = lerp(_SparkleNearCoverage, 1.0h, nearFactor);
    if (intensityMul <= 0.001h && _SparkleGlobalIntensity <= 0.001h)
        return 0;

    half nv = saturate(dot(normalWS, viewDirWS));
    // Smaller angle between normal and view (facing the surface) => larger nv.
    half facing = pow(nv, max(_SparkleFacingPower, 0.01h));

    half coverage = saturate(_SparkleCoverage * coverageMul);
    coverage = saturate(coverage * (1.0h + facing * _SparkleFacingCoverage));
    half intensity = _SparkleIntensity * intensityMul * (1.0h + facing * _SparkleFacingIntensity);

    half sparse = 0;
    if (intensity > 0.001h && coverage > 0.001h)
    {
        float density = max((float)_SparkleDensity, 0.01);
        float2 worldScaled = positionWS.xz * density;
        // Stability: don't allow cell size to go far below a pixel.
        float2 fw = fwidth(worldScaled);
        float pixelMetric = max(fw.x, fw.y);
        float densityScale = saturate(1.0 / max(pixelMetric * _SparkleMinPixelWidth * 0.5, 1.0));
        densityScale = lerp(1.0, densityScale, _SparkleStability);
        worldScaled *= densityScale;

        float2 cell = floor(worldScaled);
        float2 cellUV = frac(worldScaled);
        sparse = GoldPileSparkleCell(
            cell,
            cellUV,
            worldScaled,
            coverage,
            max(_SparkleSize, 4.0h),
            normalWS,
            viewDirWS,
            lightDirWS,
            metallic,
            intensity,
            _SparkleFlicker,
            _SparkleSpeed,
            _SparkleFresnel);
    }

    half globalLayer = GoldPileSparkleGlobal(
        positionWS,
        normalWS,
        viewDirWS,
        lightDirWS,
        metallic,
        intensityMul);

    return _SparkleColor.rgb * (sparse + globalLayer);
}

// Distant coin impostor: world-anchored palette marks that add screen pixels
// in a compact spiral, reaching a complete 3x3 as the camera approaches.
half3 GoldPileDistantCoinPixels(
    float3 positionWS,
    float4 positionCS,
    float2 deformUV,
    float3 normalWS,
    float3 viewDirWS,
    float3 lightDirWS,
    half3 lightColor,
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

    float rnd = GoldPileHash21(cell);
    float rnd2 = GoldPileHash21(cell + 17.13);
    float rnd3 = GoldPileHash21(cell + 41.71);

    half coverage = saturate(_DistantCoinCoverage);
    if (rnd > coverage)
        return 0;

    // Stable world anchor inside the cell.
    float rnd4 = GoldPileHash21(cell + 63.37);
    float2 corner = float2(rnd2, rnd3);
    float2 growSign = float2(rnd4 > 0.5 ? 1.0 : -1.0, GoldPileHash21(cell + 91.17) > 0.5 ? 1.0 : -1.0);

    if (abs(determinant) <= 1e-7)
        return 0;

    // Solve the local Jacobian to locate the world anchor in screen pixels,
    // then snap it once to the hardware pixel grid. Unlike fwidth-sized marks,
    // this footprint cannot breathe or slide between pixels as scale changes.
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

    // Compact spiral: the first four pixels form the original 2x2, then the
    // remaining five fill its left and lower edges to complete a 3x3.
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
    half amount4 = max(_DistantCoinAmount4, 0.0h);
    half amount5 = max(_DistantCoinAmount5, 0.0h);
    half amount6 = max(_DistantCoinAmount6, 0.0h);
    half amount7 = max(_DistantCoinAmount7, 0.0h);
    half totalAmount = amount0 + amount1 + amount2 + amount3
        + amount4 + amount5 + amount6 + amount7;
    if (totalAmount <= 0.001h)
        return 0;

    // Relative weights only affect colour selection; anchors remain unchanged.
    half colourChoice = (half)rnd3 * totalAmount;
    half3 colour = _DistantCoinColor0.rgb;
    half run = amount0;
    if (colourChoice >= run) { colour = _DistantCoinColor1.rgb; run += amount1; }
    if (colourChoice >= run) { colour = _DistantCoinColor2.rgb; run += amount2; }
    if (colourChoice >= run) { colour = _DistantCoinColor3.rgb; run += amount3; }
    if (colourChoice >= run) { colour = _DistantCoinColor4.rgb; run += amount4; }
    if (colourChoice >= run) { colour = _DistantCoinColor5.rgb; run += amount5; }
    if (colourChoice >= run) { colour = _DistantCoinColor6.rgb; run += amount6; }
    if (colourChoice >= run) { colour = _DistantCoinColor7.rgb; }

    // Slight value jitter so a field of marks doesn't look uniform.
    half value = lerp(0.85h, 1.15h, (half)GoldPileHash21(cell + 7.77));
    colour *= value;

    half visibility = nearFade * topMask * mask;
    half3 result = colour * _DistantCoinIntensity * visibility;

    if (_DistantCoinShineEnabled > 0.5h && _DistantCoinShineIntensity > 0.001h)
    {
        half3 shineNormalWS = normalWS;

        // MPB textures do not update *_TexelSize, so resolve UV step from the
        // heightfield resolution written by GoldPileTerrainMesh.
        if (_DeformEnabled > 0.5h && _DeformScale > 0.0h && _DeformResolution > 1.0)
        {
            float uvStep = rcp(max(_DeformResolution - 1.0, 1.0));
            half heightLeft = SAMPLE_TEXTURE2D(
                _DeformMap,
                sampler_DeformMap,
                deformUV - float2(uvStep, 0)).r;
            half heightRight = SAMPLE_TEXTURE2D(
                _DeformMap,
                sampler_DeformMap,
                deformUV + float2(uvStep, 0)).r;
            half heightDown = SAMPLE_TEXTURE2D(
                _DeformMap,
                sampler_DeformMap,
                deformUV - float2(0, uvStep)).r;
            half heightUp = SAMPLE_TEXTURE2D(
                _DeformMap,
                sampler_DeformMap,
                deformUV + float2(0, uvStep)).r;

            float worldStep = max(_DeformWorldSize * uvStep, 1e-4);
            half gradientScale =
                _DistantCoinShineNormalStrength * max(_DeformScale, 0.0h);
            float3 shineNormalOS = SafeNormalize(float3(
                (heightLeft - heightRight) * gradientScale,
                worldStep * 2.0,
                (heightDown - heightUp) * gradientScale));
            shineNormalWS = SafeNormalize(
                TransformObjectToWorldNormal(shineNormalOS));
        }

        half3 viewDir = SafeNormalize(viewDirWS);
        half3 lightDir = SafeNormalize(lightDirWS);
        half3 halfDir = SafeNormalize(viewDir + lightDir);
        half ndotL = saturate(dot(shineNormalWS, lightDir));
        half ndotV = saturate(dot(shineNormalWS, viewDir));
        half ndotH = saturate(dot(shineNormalWS, halfDir));
        half metallic = saturate(_DistantCoinShineMetallic);
        half smoothness = saturate(_DistantCoinShineSmoothness);

        // Wrap lighting keeps single-pixel marks readable while still responding
        // to deform slopes from the heightfield normal.
        half wrap = saturate(ndotL * 0.65h + 0.35h);
        half3 lightRGB = max(lightColor, half3(0.08h, 0.08h, 0.08h));
        half3 ambient = colour * 0.2h;
        half3 diffuse = colour * lightRGB * wrap * (1.0h - metallic * 0.85h);

        half specularPower = exp2(lerp(3.0h, 11.0h, smoothness));
        half specularLobe = pow(max(ndotH, 1e-4h), specularPower);
        // Energy-ish compensation so smoother highlights stay bright on 1px marks.
        specularLobe *= lerp(0.35h, 2.5h, smoothness);
        half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), colour, metallic);
        half fresnel = pow(1.0h - ndotV, 5.0h);
        half3 specular = lightRGB * specularLobe * (f0 + (1.0h - f0) * fresnel);

        // Shine replaces the flat impostor colour so intensity / metallic /
        // smoothness clearly change what you see, not a buried specular add.
        half3 lit = ambient + diffuse + specular;
        half shineAmt = saturate(_DistantCoinShineIntensity);
        result = lerp(result, lit * _DistantCoinIntensity * visibility, shineAmt);
        // Extra intensity above 1 keeps boosting the metallic response.
        result += lit * visibility * max(_DistantCoinShineIntensity - 1.0h, 0.0h);
    }

    return result;
}

#endif
