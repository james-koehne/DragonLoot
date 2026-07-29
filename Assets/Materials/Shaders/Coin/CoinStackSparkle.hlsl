#ifndef DRAGONLOOT_COIN_STACK_SPARKLE_INCLUDED
#define DRAGONLOOT_COIN_STACK_SPARKLE_INCLUDED

float CoinStackHash21(float2 p)
{
    return CoinStackHash11(dot(p, float2(127.1, 311.7)));
}

// Sparse metallic glints; world-anchored so stacks don't crawl under camera motion.
half3 CoinStackSparkle(float3 positionWS, half3 normalWS, half3 viewDirWS, half3 lightDirWS, half metallic)
{
    if (_SparkleEnabled < 0.5h || _SparkleIntensity <= 0.001h)
        return half3(0, 0, 0);

    float density = max((float)_SparkleDensity, 0.5);
    float3 scaled = positionWS * density;
    float3 cell = floor(scaled);
    float rnd = CoinStackHash11(dot(cell, float3(12.9898, 78.233, 37.719)));
    float rnd2 = CoinStackHash21(cell.xy + cell.z * 19.19);

    half coverage = saturate(_SparkleCoverage);
    half threshold = 1.0h - coverage;
    half mask = smoothstep(threshold - 0.04h, threshold + 0.04h, (half)rnd);
    if (mask < 0.001h)
        return half3(0, 0, 0);

    half3 halfDir = SafeNormalize(lightDirWS + viewDirWS);
    half nh = saturate(dot(normalWS, halfDir));
    half nv = saturate(dot(normalWS, viewDirWS));
    half glint = pow(nh, max(_SparkleSharpness, 1.0h));
    glint *= lerp(0.65h, 1.0h, pow(nv, 2.0h));

    float t = _TimeParameters.x * (float)_SparkleSpeed + rnd2 * 6.283185;
    half pulse = sin((half)t) * 0.5h + 0.5h;
    pulse = lerp(1.0h, pulse * pulse, saturate(_SparkleFlicker));

    half glitter = glint * mask * pulse * _SparkleIntensity;
    glitter *= saturate(0.35h + metallic);
    return _SparkleColor.rgb * glitter;
}

#endif
