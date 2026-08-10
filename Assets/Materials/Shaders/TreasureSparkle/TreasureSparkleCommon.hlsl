#ifndef DRAGONLOOT_TREASURE_SPARKLE_COMMON_INCLUDED
#define DRAGONLOOT_TREASURE_SPARKLE_COMMON_INCLUDED

float TreasureSparkleHash11(float p)
{
	float n = sin(p * 127.1) * 43758.5453;
	return frac(n);
}

float TreasureSparkleHash13(float3 p)
{
	return TreasureSparkleHash11(dot(p, float3(127.1, 311.7, 74.7)));
}

float2 TreasureSparkleHash23(float3 p)
{
	return float2(
		TreasureSparkleHash11(dot(p, float3(127.1, 311.7, 74.7))),
		TreasureSparkleHash11(dot(p, float3(269.5, 183.3, 246.1))));
}

float3 TreasureSparkleHash33(float3 p)
{
	return float3(
		TreasureSparkleHash11(dot(p, float3(127.1, 311.7, 74.7))),
		TreasureSparkleHash11(dot(p, float3(269.5, 183.3, 246.1))),
		TreasureSparkleHash11(dot(p, float3(113.5, 271.9, 124.6))));
}

float3 TreasureSparkleDirection(float3 rnd)
{
	float z = rnd.z * 2.0 - 1.0;
	float a = rnd.x * 6.2831853;
	float r = sqrt(max(1.0 - z * z, 0.0));
	return normalize(float3(cos(a) * r, sin(a) * r, z));
}

float TreasureSparkleScreenEdgeFade(float2 uv, float edge)
{
	if (edge <= 1e-5)
		return 1.0;
	float2 d = min(uv, 1.0 - uv);
	float m = min(d.x, d.y);
	return smoothstep(0.0, edge, m);
}

float TreasureSparkleSourceDensityMultiplier(float mask, float gemMul, float artifactMul)
{
	if (mask >= 0.875)
		return artifactMul;
	if (mask >= 0.625)
		return gemMul;
	return 1.0;
}

float3 TreasureSparkleSafeNormalize(float3 v)
{
	float lenSq = dot(v, v);
	return lenSq > 1e-8 ? v * rsqrt(lenSq) : float3(0, 1, 0);
}

#endif
