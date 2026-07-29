#ifndef DRAGONLOOT_GOLDPILE_LIGHTING_INCLUDED
#define DRAGONLOOT_GOLDPILE_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "GoldPileCommon.hlsl"
#if defined(GOLDPILE_STYLIZED)
#include "GoldPileStylized.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    half4  tangentWS  : TEXCOORD2;
    float3 viewDirTS  : TEXCOORD3;
    float2 uv         : TEXCOORD4;
    half   fogFactor  : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD6;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD8;
#endif
    float2 deformUV : TEXCOORD9;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void GoldPileInitializeBakedGIData(Varyings input, inout InputData inputData)
{
#if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif
}

void GoldPileInitializeInputData(Varyings input, half3 normalWS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = NormalizeNormalPerPixel(normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    GoldPileInitializeBakedGIData(input, inputData);
}

Varyings GoldPileLitVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(GOLDPILE_STYLIZED)
    float3 positionOS = ApplyGoldPileRuntimeDeform(input.positionOS.xyz, input.texcoord);
#else
    float3 positionOS = ApplyGoldPileVertexDisplacement(input.positionOS.xyz, input.texcoord);
#endif
    VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = normalInputs.normalWS;

    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInputs.tangentWS, sign);

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
    half3 bitangent = sign * cross(normalInputs.normalWS, normalInputs.tangentWS);
    half3x3 tangentToWorld = half3x3(normalInputs.tangentWS, bitangent, normalInputs.normalWS);
    output.viewDirTS = half3(
        dot(viewDirWS, tangentToWorld[0]),
        dot(viewDirWS, tangentToWorld[1]),
        dot(viewDirWS, tangentToWorld[2]));

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.deformUV = input.texcoord;
    output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(posInputs);
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
    OUTPUT_SH4(posInputs.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(posInputs.positionWS), output.vertexSH, output.probeOcclusion);
    return output;
}

half4 GoldPileLitFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    GoldPileClipBelowGround(input.deformUV);

    float3 positionWS = input.positionWS;
    float3 normalWS = normalize(input.normalWS);
    float camDist = distance(positionWS, GetCameraPositionWS());

#if defined(GOLDPILE_STYLIZED)
    // Stylized path is intentionally predictable: planar sampling with only the
    // runtime deform shape; no authored displacement, POM, triplanar, sparkles, or LOD.
    float2 sampleUV = GetGoldPilePlanarUV(positionWS);
    half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, sampleUV) * _BaseColor;
    half4 maskSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, sampleUV);
    half occlusion = lerp(
        1.0h,
        SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, sampleUV).g,
        _OcclusionStrength);
    half metallic = maskSample.r * _Metallic;
    half smoothness = maskSample.a * _Smoothness;
    ApplyGoldPileColourVariation(positionWS.xz, albedoSample.rgb, smoothness);

    half3 coinNormalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, sampleUV),
        _BumpScale * _CoinNormalStrength);
    half3 pileNormalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_PileNormalMap, sampler_PileNormalMap, sampleUV),
        _PileNormalStrength);
    half3 t = coinNormalTS + half3(0, 0, 1);
    half3 u = pileNormalTS * half3(-1, -1, 1);
    half3 blendedTS = normalize(t * dot(t, u) - u * t.z);

#if defined(_DETAIL_ON)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, sampleUV).r;
    detailMask = saturate(detailMask + GoldPileEdgeMask(normalWS, 0.5h));
    half3 detailAlbedo = SAMPLE_TEXTURE2D(
        _DetailAlbedoMap,
        sampler_DetailAlbedoMap,
        sampleUV * 2.0).rgb;
    albedoSample.rgb = lerp(
        albedoSample.rgb,
        albedoSample.rgb * detailAlbedo * _DetailAlbedoScale,
        detailMask);

    half3 detailN = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, sampleUV * 2.0),
        _DetailNormalScale);
    half3 t2 = blendedTS + half3(0, 0, 1);
    half3 u2 = detailN * half3(-1, -1, 1);
    blendedTS = normalize(t2 * dot(t2, u2) - u2 * t2.z);
#endif

    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(
        input.tangentWS.xyz,
        bitangent,
        input.normalWS.xyz);
    half3 detailedNormalWS = NormalizeNormalPerPixel(
        TransformTangentToWorld(blendedTS, tangentToWorld));

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedoSample.rgb;
    surfaceData.metallic = metallic;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = blendedTS;
    surfaceData.emission = 0;
    surfaceData.occlusion = occlusion;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData;
    GoldPileInitializeInputData(input, detailedNormalWS, inputData);
    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    Light mainLight = GetMainLight(
        inputData.shadowCoord,
        inputData.positionWS,
        inputData.shadowMask);

    color.rgb = ApplyGoldPileStylized(
        color.rgb,
        albedoSample.rgb,
        detailedNormalWS,
        inputData.viewDirectionWS,
        mainLight.direction,
        mainLight.color,
        mainLight.distanceAttenuation * mainLight.shadowAttenuation,
        0.5h,
        metallic,
        occlusion);

    color.rgb += GoldPileDistantCoinPixels(
        positionWS,
        input.positionCS,
        input.deformUV,
        normalWS,
        inputData.viewDirectionWS,
        mainLight.direction,
        // Keep a lit floor so metallic shine still responds in soft shadow.
        mainLight.color * max(
            mainLight.distanceAttenuation * mainLight.shadowAttenuation,
            0.35h),
        camDist);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
#else
    half lodBand = GoldPileLodFactor(camDist);

    GoldPileTriplanarUV triUV = GetGoldPileTriplanarUV(positionWS, normalWS);
#if !defined(_TRIPLANAR)
    triUV.uvY = GetGoldPilePlanarUV(positionWS);
    triUV.weights = half3(0, 1, 0);
#endif

    // Far LOD: flat material only.
    if (lodBand >= 1.99h)
    {
        half4 albedoFar = SampleGoldPileTriplanarAlbedo(triUV) * _BaseColor;
        half4 maskFar = SampleGoldPileTriplanarTex(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), triUV);
        half metallicFar = maskFar.r * _Metallic;
        half smoothnessFar = maskFar.a * _Smoothness;
        ApplyGoldPileColourVariation(positionWS.xz, albedoFar.rgb, smoothnessFar);

        SurfaceData surfaceFar = (SurfaceData)0;
        surfaceFar.albedo = albedoFar.rgb;
        surfaceFar.metallic = metallicFar;
        surfaceFar.specular = half3(0, 0, 0);
        surfaceFar.smoothness = smoothnessFar;
        surfaceFar.normalTS = half3(0, 0, 1);
        surfaceFar.emission = 0;
        surfaceFar.occlusion = 1;
        surfaceFar.alpha = 1;

        InputData inputFar;
        GoldPileInitializeInputData(input, normalWS, inputFar);
        half4 colorFar = UniversalFragmentPBR(inputFar, surfaceFar);
#if defined(GOLDPILE_STYLIZED)
        Light mainFar = GetMainLight(inputFar.shadowCoord, inputFar.positionWS, inputFar.shadowMask);
        colorFar.rgb = ApplyGoldPileStylized(
            colorFar.rgb,
            albedoFar.rgb,
            normalWS,
            inputFar.viewDirectionWS,
            mainFar.direction,
            mainFar.color,
            mainFar.distanceAttenuation * mainFar.shadowAttenuation,
            0.5h,
            metallicFar,
            1.0h);
#endif
        Light pixelLightFar = GetMainLight(
            inputFar.shadowCoord,
            inputFar.positionWS,
            inputFar.shadowMask);
        colorFar.rgb += GoldPileDistantCoinPixels(
            positionWS,
            input.positionCS,
            input.deformUV,
            normalWS,
            inputFar.viewDirectionWS,
            pixelLightFar.direction,
            pixelLightFar.color *
                (pixelLightFar.distanceAttenuation * pixelLightFar.shadowAttenuation),
            camDist);
        colorFar.rgb = MixFog(colorFar.rgb, inputFar.fogCoord);
        return colorFar;
    }

    float2 sampleUV = triUV.uvY;
    half pomFade = 0;
    half pomSelfShadow = 1;
    float2 pomDelta = 0;

    // Height map toggle is driven by the material float (reliable) — not only the keyword.
    // Off = no parallax. On = Unity-style height parallax in world XZ UV space.
    if (_HeightMapEnabled > 0.5h)
    {
        half allowParallax = 1.0h;
        if (_DisableLod < 0.5h)
        {
            if (lodBand >= 1.99h)
                allowParallax = 0.0h;
            else if (lodBand >= 0.5h)
                allowParallax = 0.65h;
        }

        if (allowParallax > 0.001h)
        {
            pomFade = max(GoldPilePomFade(camDist), 0.35h) * allowParallax;

            float3 viewDirTS = GoldPileWorldPlanarViewDirTS(positionWS);
            float3 lightDirTS = GoldPileWorldPlanarLightDirTS(GetMainLight().direction);

#if defined(_POM_ON)
            GoldPilePomResult pom = GoldPileParallaxOcclusion(
                triUV.uvY,
                viewDirTS,
                lightDirTS,
                pomFade);
            sampleUV = pom.uv;
            pomDelta = pom.uvDelta;
            pomSelfShadow = pom.selfShadow;
#else
            float3 vd = viewDirTS;
            vd.z = max(vd.z, 0.15);
            vd = normalize(vd);
            half h = SampleGoldPilePomHeight(triUV.uvY);
            float amp = _Parallax * pomFade * max((float)_HeightMapIntensity, 0.0);
            float2 offset = (vd.xy / vd.z) * amp * (1.0h - h);
            sampleUV = triUV.uvY - offset;
            pomDelta = sampleUV - triUV.uvY;
#endif
        }
    }

    GoldPileTriplanarUV sampleTri = triUV;
    sampleTri.uvX += pomDelta;
    sampleTri.uvY = sampleUV;
    sampleTri.uvZ += pomDelta;

#if defined(_TRIPLANAR)
    if (_HeightMapEnabled > 0.5h && pomFade > 0.001)
    {
        half topBias = saturate(abs(normalWS.y));
        half3 w = sampleTri.weights;
        w.y = lerp(w.y, 1.0h, topBias * pomFade * 0.85h);
        w.x *= (1.0h - topBias * pomFade * 0.85h);
        w.z *= (1.0h - topBias * pomFade * 0.85h);
        w /= max(w.x + w.y + w.z, 1e-5h);
        sampleTri.weights = w;
    }
#endif

    half4 albedoSample = SampleGoldPileTriplanarAlbedo(sampleTri) * _BaseColor;
    half4 maskSample = SampleGoldPileTriplanarTex(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), sampleTri);
    half occlusion = lerp(1.0h, SampleGoldPileTriplanarTex(TEXTURE2D_ARGS(_OcclusionMap, sampler_OcclusionMap), sampleTri).g, _OcclusionStrength);
    half heightSample = SampleGoldPilePomHeight(sampleUV);
    occlusion *= pomSelfShadow;

    half metallic = maskSample.r * _Metallic;
    half smoothness = maskSample.a * _Smoothness;
    ApplyGoldPileColourVariation(positionWS.xz, albedoSample.rgb, smoothness);

    half3 coinNormalTS = SampleGoldPileCoinNormalTS(sampleTri, _BumpScale * _CoinNormalStrength);
    half3 pileNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_PileNormalMap, sampler_PileNormalMap, sampleUV), _PileNormalStrength);
    // Reoriented normal blend: mound + coin detail.
    half3 t = coinNormalTS + half3(0, 0, 1);
    half3 u = pileNormalTS * half3(-1, -1, 1);
    half3 blendedTS = normalize(t * dot(t, u) - u * t.z);

#if defined(_DETAIL_ON)
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, sampleUV).r;
    half edge = GoldPileEdgeMask(normalWS, heightSample);
    detailMask = saturate(detailMask + edge);

    half3 detailAlbedo = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, sampleUV * 2.0).rgb;
    albedoSample.rgb = lerp(albedoSample.rgb, albedoSample.rgb * detailAlbedo * _DetailAlbedoScale, detailMask);

    half3 detailN = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, sampleUV * 2.0), _DetailNormalScale);
    half3 t2 = blendedTS + half3(0, 0, 1);
    half3 u2 = detailN * half3(-1, -1, 1);
    half3 blendedDetail = normalize(t2 * dot(t2, u2) - u2 * t2.z);
    blendedTS = lerp(blendedTS, blendedDetail, detailMask);
#endif

    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
    half3 detailedNormalWS = NormalizeNormalPerPixel(TransformTangentToWorld(blendedTS, tangentToWorld));

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedoSample.rgb;
    surfaceData.metallic = metallic;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = smoothness;
    surfaceData.normalTS = blendedTS;
    surfaceData.emission = 0;
    surfaceData.occlusion = occlusion;
    surfaceData.alpha = 1;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    InputData inputData;
    GoldPileInitializeInputData(input, detailedNormalWS, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);

    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);

#if defined(GOLDPILE_STYLIZED)
    color.rgb = ApplyGoldPileStylized(
        color.rgb,
        albedoSample.rgb,
        detailedNormalWS,
        inputData.viewDirectionWS,
        mainLight.direction,
        mainLight.color,
        mainLight.distanceAttenuation * mainLight.shadowAttenuation,
        heightSample,
        metallic,
        occlusion);
#endif

    // Local sparkles disabled — global TreasureSparkleRendererFeature owns glints.
    // Sparkles: float toggle + allow mid LOD (only skip far).
    if (false && _SparkleEnabled > 0.5h && lodBand < 1.5h)
    {
        half3 sparkle = GoldPileSparkle(
            positionWS,
            detailedNormalWS,
            inputData.viewDirectionWS,
            mainLight.direction,
            metallic,
            camDist);
        // Mid LOD softens cost/visibility slightly.
        if (lodBand >= 0.5h)
            sparkle *= 0.5h;
#if defined(GOLDPILE_STYLIZED)
        // Stylized path: punchier, warmer glints.
        sparkle *= 1.25h;
#endif
        color.rgb += sparkle;
    }

    // Geometric normal keeps the static pixel mask independent of POM/normal-map view changes.
    color.rgb += GoldPileDistantCoinPixels(
        positionWS,
        input.positionCS,
        input.deformUV,
        normalWS,
        inputData.viewDirectionWS,
        mainLight.direction,
        mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation),
        camDist);

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    return color;
#endif
}

#endif
