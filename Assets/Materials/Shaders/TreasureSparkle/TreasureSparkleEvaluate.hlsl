#ifndef DRAGONLOOT_TREASURE_SPARKLE_EVALUATE_INCLUDED
#define DRAGONLOOT_TREASURE_SPARKLE_EVALUATE_INCLUDED

#include "TreasureSparkleCommon.hlsl"

struct TreasureSparkleParams
{
	float cellSize;
	float density;
	float maxActive;
	float seed;
	float targetPixelSize;
	float glintTexSize;
	float useGlintTex;
	float glintTexBlend;
	float minSize;
	float maxSize;
	float screenDensityBlend;
	float screenDensityRefPixels;
	float3 sparkleColor;
	float baseGlint;
	float litBoost;
	float brightnessJitter;
	float coreHotness;
	float coinFacetJitter;
	float metallicSharpness;
	float lightKillStrength;
	float softLobeAmount;
	float baseCoverage;
	float specularBoost;
	float specularDensity;
	float minIntensity;
	float alignmentThreshold;
	float fadeAngle;
	float viewCrossfade;
	float viewCrossfadeRefDistance;
	float viewSwapStrength;
	float viewSwapSharpness;
	float viewSwapThreshold;
	float nearGlintStart;
	float nearGlintFull;
	float farGlintStart;
	float farGlintEnd;
	float nearDensityScale;
	float farDensityScale;
	float nearIntensityFloor;
	float gemDensityMul;
	float artifactDensityMul;
	float screenEdgeFade;
	float twinkleStrength;
	float twinkleSpeed;
	float twinkleDesync;
	float bloomContribution;
	float surfaceSlack;
	float time;
	float3 cameraPosition;
	float3 lightDirWS;
	float4x4 viewProj;
	float4x4 invViewProj;
	float2 resolution;
	float tanHalfFovY;
};

struct TreasureSparkleGlint
{
	float3 centerWS;
	float intensity;
	float sizeJitter;
	bool valid;
};

float2 TreasureSparkleProjectWorldToScreenUV(float3 positionWS, float4x4 viewProj)
{
	float4 clip = mul(viewProj, float4(positionWS, 1.0));
	float2 ndc = clip.xy / max(abs(clip.w), 1e-6);
	#if UNITY_UV_STARTS_AT_TOP
	ndc.y = -ndc.y;
	#endif
	return ndc * 0.5 + 0.5;
}

float3 TreasureSparkleReconstructWorldPosition(float2 uv, float rawDepth, float4x4 invViewProj)
{
	#if UNITY_REVERSED_Z
	float deviceDepth = rawDepth;
	#else
	float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
	#endif

	float4 clipPos = float4(uv * 2.0 - 1.0, deviceDepth, 1.0);
	#if UNITY_UV_STARTS_AT_TOP
	clipPos.y = -clipPos.y;
	#endif

	float4 worldPos = mul(invViewProj, clipPos);
	return worldPos.xyz / max(worldPos.w, 1e-6);
}

TreasureSparkleGlint TreasureSparkleEvaluateCell(
	int3 cell,
	float kindMaskHint,
	float sampledMask,
	float rawDepth,
	float3 normalSample,
	TreasureSparkleParams p)
{
	TreasureSparkleGlint result;
	result.centerWS = 0;
	result.intensity = 0;
	result.sizeJitter = 1;
	result.valid = false;

	float cellSize = max(p.cellSize, 0.02);
	float3 seedBase = (float3)cell + float3(p.seed, p.seed * 1.7, p.seed * 2.3);
	float occupancy = TreasureSparkleHash13(seedBase);

	float3 rnd = TreasureSparkleHash33(seedBase + 19.19);
	float2 rnd2 = TreasureSparkleHash23(seedBase + 7.7);
	float3 glintCenter = rnd * 0.85 + 0.075;
	float3 centerWS = ((float3)cell + glintCenter) * cellSize;

	float4 centerClip = mul(p.viewProj, float4(centerWS, 1.0));
	if (centerClip.w <= 1e-5)
		return result;

	float2 centerUV = TreasureSparkleProjectWorldToScreenUV(centerWS, p.viewProj);
	if (centerUV.x < 0.0 || centerUV.x > 1.0 || centerUV.y < 0.0 || centerUV.y > 1.0)
		return result;

	float mask = sampledMask > 0.01 ? sampledMask : kindMaskHint;
	// Soft mask edge — hard 0/1 at silhouettes flickered every frame while moving.
	float maskFade = smoothstep(0.02, 0.12, mask);
	if (maskFade <= 1e-4)
		return result;

	#if UNITY_REVERSED_Z
	if (rawDepth <= 1e-5)
		return result;
	#else
	if (rawDepth >= 0.99999)
		return result;
	#endif

	float3 surfaceWS = TreasureSparkleReconstructWorldPosition(centerUV, rawDepth, p.invViewProj);
	float slack = max(p.surfaceSlack, cellSize * 0.75);
	float surfaceDist = distance(surfaceWS, centerWS);
	// Soft reject — hard on/off at silhouette depth caused one-frame flicker while moving.
	float surfaceFade = 1.0 - smoothstep(slack * 0.55, slack, surfaceDist);
	if (surfaceFade <= 1e-4)
		return result;

	// Lift hashed XZ onto the visible surface height so glints sit on the pile, not the floor slab.
	float3 placeWS = float3(centerWS.x, surfaceWS.y, centerWS.z);
	float3 viewDirWS = TreasureSparkleSafeNormalize(p.cameraPosition - placeWS);
	float camDist = distance(p.cameraPosition, placeWS);

	float edgeFade = TreasureSparkleScreenEdgeFade(centerUV, p.screenEdgeFade);
	float nearFade = smoothstep(p.nearGlintStart, p.nearGlintFull, camDist);
	float farFade = 1.0 - smoothstep(p.farGlintStart, p.farGlintEnd, camDist);
	float distFade = saturate(nearFade * farFade);
	distFade = max(distFade, p.nearIntensityFloor * farFade);

	float densityScale = lerp(p.nearDensityScale, 1.0, nearFade);
	densityScale = lerp(densityScale, p.farDensityScale, saturate(nearFade * farFade));
	densityScale *= 1.0 / max(1.0 + camDist * 0.04, 1.0);
	densityScale *= TreasureSparkleSourceDensityMultiplier(mask, p.gemDensityMul, p.artifactDensityMul);

	float worldPerPixel = max(2.0 * camDist * p.tanHalfFovY / max(p.resolution.y, 1.0), 1e-5);
	float pixelsPerCell = cellSize / worldPerPixel;
	float densRef = max(p.screenDensityRefPixels, 1.0);
	float densMul = saturate(pixelsPerCell / densRef);
	densMul = densMul * densMul * densMul;
	densMul = lerp(1.0, densMul, saturate(p.screenDensityBlend));

	float coverageOpen = saturate(p.baseCoverage);
	float boost = saturate(p.specularBoost);

	float normalLen = length(normalSample);
	float3 normalWS = normalLen > 1e-3 ? normalSample / normalLen : float3(0, 1, 0);
	float3 lightDirWS = p.lightDirWS;
	if (dot(lightDirWS, lightDirWS) < 1e-6)
		lightDirWS = float3(0.35, 0.85, 0.35);
	lightDirWS = TreasureSparkleSafeNormalize(lightDirWS);

	float3 facetJitter = TreasureSparkleDirection(rnd);
	float3 facetNormal = TreasureSparkleSafeNormalize(normalWS + facetJitter * p.coinFacetJitter);

	// Far points need less view-dot travel for the same camera move — shrink fade width with distance.
	float crossfadeRef = max(p.viewCrossfadeRefDistance, 1.0);
	float crossfadeScale = saturate(crossfadeRef / max(camDist, 1e-3));
	crossfadeScale = max(crossfadeScale, 0.08);
	float fadeWidth = max(p.viewCrossfade * crossfadeScale, 0.005);
	float swapStrength = saturate(p.viewSwapStrength);
	float3 halfDir = TreasureSparkleSafeNormalize(lightDirWS + viewDirWS);
	float3 reflectDir = reflect(-lightDirWS, facetNormal);
	float nh = saturate(dot(facetNormal, halfDir));
	float rv = saturate(dot(reflectDir, viewDirWS));
	float sharpness = max(p.metallicSharpness, 2.0);
	float spec = max(pow(nh, sharpness), pow(rv, sharpness * 0.85));
	float soft = pow(max(nh, rv), max(sharpness * 0.22, 1.5)) * p.softLobeAmount;
	float lightResp = max(spec, soft);

	float alignStart = p.alignmentThreshold;
	float alignEnd = saturate(alignStart + max(p.fadeAngle, 0.05) + fadeWidth * 0.35);
	lightResp *= smoothstep(max(0.0, alignStart - fadeWidth * 0.15), alignEnd, max(nh, rv));

	// Occupancy: baseCoverage opens pile-wide spawn; specularDensity packs extra glints into hotspots.
	float coverageCap = min(p.density * densityScale * densMul, p.maxActive);
	float specularGate = saturate(lightResp * 1.5);
	float hotspotMul = 1.0 + boost * max(p.specularDensity, 0.0) * specularGate;
	float cellCap = max(coverageCap * (1.0 + coverageOpen * lerp(1.0, specularGate, boost)) * hotspotMul, 1e-4);
	float occupancyFade = 1.0 - smoothstep(cellCap * 0.8, max(cellCap * 1.2, cellCap + 1e-4), occupancy);
	if (occupancyFade <= 1e-4)
		return result;

	float3 preferView = TreasureSparkleDirection(float3(rnd2.y, rnd.z, rnd.x));
	float preferDot = saturate(dot(preferView, viewDirWS));
	float gateThresh = p.viewSwapThreshold * swapStrength;
	float preferLobe = smoothstep(gateThresh, gateThresh + fadeWidth, preferDot);
	preferLobe = pow(preferLobe, max(p.viewSwapSharpness, 1.0));
	float viewMod = lerp(1.0, preferLobe, swapStrength * 0.45);

	// Intensity: residual ambient from baseCoverage + specular hotspots scaled by specularBoost.
	float ambient = coverageOpen * 0.35;
	float specular = p.litBoost * lightResp * lerp(0.35, 1.0, boost);
	float litTerm = ambient + specular;
	float kill = p.lightKillStrength * (1.0 - coverageOpen);
	float keep = lerp(1.0, saturate(lightResp * 1.5), kill);

	float sizeJitter = lerp(p.minSize, p.maxSize, rnd2.x);

	float brightJitter = lerp(1.0 - p.brightnessJitter, 1.0 + p.brightnessJitter, rnd.x);
	float phase = rnd2.y * 6.2831853 * max(p.twinkleDesync, 0.01);
	float twinkle = sin(p.time * p.twinkleSpeed + phase) * 0.5 + 0.5;
	twinkle = lerp(1.0, twinkle, saturate(p.twinkleStrength));

	float facing = normalLen > 1e-3 ? saturate(dot(normalWS, viewDirWS)) : 1.0;
	float fade = distFade * edgeFade * max(facing, 0.35) * surfaceFade * occupancyFade * maskFade;
	float intensity = litTerm * keep * viewMod * brightJitter * twinkle * fade;

	float minI = saturate(p.minIntensity);
	if (intensity < minI)
		return result;

	result.centerWS = placeWS;
	result.intensity = intensity;
	result.sizeJitter = sizeJitter;
	result.valid = true;
	return result;
}

#endif
