#ifndef DRAGONLOOT_GOLDPILE_STYLIZED_INCLUDED
#define DRAGONLOOT_GOLDPILE_STYLIZED_INCLUDED

half3 GoldPileSaturation(half3 color, half amount)
{
    half luma = dot(color, half3(0.299h, 0.587h, 0.114h));
    return lerp(half3(luma, luma, luma), color, amount);
}

half3 GoldPileSoftContrast(half3 color, half amount)
{
    // amount 1 = unchanged; >1 punches midtones; <1 flattens.
    half3 pushed = color - 0.5h;
    return saturate(pushed * amount + 0.5h);
}

// Discrete warm specular band — reads as painted metal, not GGX chrome.
half GoldPileSpecRamp(half ndoth, half threshold, half softness)
{
    half soft = max(softness, 0.001h);
    return smoothstep(threshold - soft, threshold + soft, ndoth);
}

half3 ApplyGoldPileStylized(
    half3 litColor,
    half3 albedo,
    half3 normalWS,
    half3 viewDirWS,
    half3 lightDirWS,
    half3 lightColor,
    half lightAtten,
    half heightSample,
    half metallic,
    half occlusion)
{
    half3 n = normalize(normalWS);
    half3 v = normalize(viewDirWS);
    half3 l = normalize(lightDirWS);

    half ndotv = saturate(dot(n, v));
    half ndotl = saturate(dot(n, l));
    half3 halfDir = SafeNormalize(l + v);
    half ndoth = saturate(dot(n, halfDir));

    // Soften photoreal metal toward warmer painted albedo.
    half softMetal = saturate(_StylizedMetalSoftness);
    half3 painted = albedo * (0.35h + ndotl * 0.65h) * lightColor * lightAtten;
    painted = lerp(painted, painted * occlusion, 0.5h);
    half3 graded = lerp(litColor, painted, softMetal);

    // Keep some of the PBR result, pull toward readable albedo lighting.
    graded = lerp(graded, painted, saturate(_AlbedoMix) * 0.5h);

    // Warm grade + saturation punch.
    half3 warmed = lerp(graded, graded * _StylizedWarmTint.rgb, saturate(_StylizedWarmStrength));
    warmed = GoldPileSaturation(warmed, _StylizedSaturation);
    warmed = GoldPileSoftContrast(warmed, _StylizedContrast);

    // Crevice dirt from steep slopes + low height (valleys between coins).
    half edge = GoldPileEdgeMask(n, heightSample);
    half crevice = saturate(edge * _CreviceStrength);
    warmed = lerp(warmed, warmed * _CreviceColor.rgb, crevice);

    // Cartoon specular lobe.
    half ramp = GoldPileSpecRamp(ndoth, _SpecRampThreshold, _SpecRampSoftness);
    ramp *= ndotl * lightAtten;
    ramp *= saturate(0.4h + metallic);
    half3 spec = _SpecRampColor.rgb * ramp * _SpecRampIntensity;

    // Rim glow for silhouette readability.
    half rim = pow(1.0h - ndotv, max(_RimPower, 0.01h));
    rim *= _RimIntensity;
    half3 rimCol = _RimColor.rgb * rim * (0.35h + lightAtten * 0.65h);

    return warmed + spec + rimCol;
}

#endif
