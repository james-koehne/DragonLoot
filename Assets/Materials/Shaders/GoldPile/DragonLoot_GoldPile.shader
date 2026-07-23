Shader "DragonLoot/Gold Pile"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 0.84, 0.3, 1)

        _MetallicGlossMap("Metallic Smoothness", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.85

        [Normal] _BumpMap("Coin Normal", 2D) = "bump" {}
        _BumpScale("Coin Normal Scale", Range(0, 2)) = 1
        _CoinNormalStrength("Coin Normal Strength", Range(0, 2)) = 1

        [Normal] _PileNormalMap("Pile Normal", 2D) = "bump" {}
        _PileNormalStrength("Pile Normal Strength", Range(0, 2)) = 0.35

        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1

        [Header(HeightMap)]
        [Toggle(_HEIGHTMAP_ON)] _HeightMapEnabled("Use Height Map", Float) = 1
        _HeightMap("Height Map", 2D) = "gray" {}
        _HeightMapIntensity("Height Map Intensity", Range(0, 2)) = 1
        _Parallax("Height Map Scale", Range(0.0, 0.35)) = 0.12
        _HeightMapPower("Height Map Power", Range(0.25, 4)) = 1
        _PomHeightBias("Height Map Bias", Range(-0.5, 0.5)) = 0
        _PomHeightContrast("Height Map Contrast", Range(0.25, 4)) = 1.5
        _HeightMapTiling("Height Map Tiling", Float) = 1
        [Toggle] _HeightMapInvert("Invert Height Map", Float) = 0
        _PomNormalHeight("Blend Coin Normal Height", Range(0, 1)) = 0

        [Header(ParallaxOcclusion)]
        [Toggle(_POM_ON)] _PomEnabled("High Quality POM", Float) = 1
        _POMSteps("POM Steps", Range(4, 32)) = 16
        _PomFadeStart("POM Fade Start", Float) = 8
        _PomFadeEnd("POM Fade End", Float) = 18
        [Toggle] _PomInvertHeight("Invert Combined Height", Float) = 0
        _PomSelfShadow("POM Self Shadow", Range(0, 1)) = 0.45

        [Header(VertexDisplacement)]
        _HeightScale("Vertex Displace Scale", Range(0, 2)) = 0.25
        _HeightAmount("Vertex Displace Amount", Range(0, 1)) = 1

        [Header(PileDeform)]
        [Toggle] _DeformEnabled("Runtime Deform Map", Float) = 0
        _DeformMap("Deform Height Map", 2D) = "black" {}
        _DeformScale("Deform Height Scale", Range(0, 8)) = 1.5
        [HideInInspector] _DeformWorldSize("Deform World Size", Float) = 4
        [HideInInspector] _DeformResolution("Deform Resolution", Float) = 64

        _WorldTiling("World Tiling", Float) = 0.35
        _TriplanarSharpness("Triplanar Sharpness", Range(0.1, 8)) = 4

        _HueVariation("Hue Variation", Range(0, 0.2)) = 0.03
        _BrightnessVariation("Brightness Variation", Range(0, 0.5)) = 0.08
        _RoughnessVariation("Roughness Variation", Range(0, 0.5)) = 0.1

        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap("Detail Albedo", 2D) = "gray" {}
        _DetailAlbedoScale("Detail Albedo Scale", Range(0, 2)) = 1
        [Normal] _DetailNormalMap("Detail Normal", 2D) = "bump" {}
        _DetailNormalScale("Detail Normal Scale", Range(0, 2)) = 1
        _EdgeDirtStrength("Edge Dirt Strength", Range(0, 2)) = 0.75

        [Header(Sparkles)]
        [Toggle(_SPARKLE_ON)] _SparkleEnabled("Sparkles", Float) = 1
        [HDR] _SparkleColor("Sparkle Color", Color) = (1, 0.92, 0.65, 1)
        _SparkleIntensity("Sparkle Intensity", Range(0, 16)) = 2
        _SparkleDensity("Sparkle Density", Range(1, 128)) = 18
        _SparkleSpeed("Sparkle Speed", Range(0, 20)) = 3
        _SparkleSize("Sparkle Size", Range(4, 256)) = 48
        _SparkleCoverage("Sparkle Coverage", Range(0.05, 1)) = 0.45
        _SparkleFresnel("Sparkle Fresnel", Range(0, 2)) = 0.75
        _SparkleFlicker("Sparkle Flicker", Range(0, 1)) = 0.65
        _SparkleNearFadeStart("Sparkle Near Fade Start", Float) = 2
        _SparkleNearFadeEnd("Sparkle Near Fade End", Float) = 12
        _SparkleNearIntensity("Sparkle Near Intensity", Range(0, 1)) = 0.15
        _SparkleNearCoverage("Sparkle Near Coverage", Range(0, 1)) = 0.2
        _SparkleFacingCoverage("Sparkle Facing Coverage", Range(0, 4)) = 1.5
        _SparkleFacingIntensity("Sparkle Facing Intensity", Range(0, 4)) = 1.25
        _SparkleFacingPower("Sparkle Facing Power", Range(0.25, 8)) = 2
        _SparkleGlobalIntensity("Sparkle Global Intensity", Range(0, 8)) = 0.35
        _SparkleGlobalDensity("Sparkle Global Density", Range(1, 256)) = 48
        _SparkleGlobalSize("Sparkle Global Size", Range(4, 256)) = 24
        _SparkleGlobalSpeed("Sparkle Global Speed", Range(0, 20)) = 1.5
        _SparkleSoftness("Sparkle Softness", Range(0, 1)) = 0.65
        _SparkleMinPixelWidth("Sparkle Min Pixel Width", Range(0.5, 6)) = 1.5
        _SparkleStability("Sparkle Stability", Range(0, 1)) = 0.7

        // Kept in CBUFFER for SRP batching parity with the Stylized A/B shader.
        [HideInInspector] _DistantCoinEnabled("Distant Coin Pixels", Float) = 0
        [HideInInspector] _DistantCoinColor0("Distant Coin Colour 1", Color) = (1.0, 0.78, 0.28, 1)
        [HideInInspector] _DistantCoinColor1("Distant Coin Colour 2", Color) = (0.82, 0.86, 0.92, 1)
        [HideInInspector] _DistantCoinColor2("Distant Coin Colour 3", Color) = (0.95, 0.45, 0.2, 1)
        [HideInInspector] _DistantCoinColor3("Distant Coin Colour 4", Color) = (0.55, 0.75, 0.35, 1)
        [HideInInspector] _DistantCoinColor4("Distant Coin Colour 5", Color) = (0.35, 0.55, 0.95, 1)
        [HideInInspector] _DistantCoinColor5("Distant Coin Colour 6", Color) = (0.75, 0.35, 0.85, 1)
        [HideInInspector] _DistantCoinColor6("Distant Coin Colour 7", Color) = (0.95, 0.75, 0.35, 1)
        [HideInInspector] _DistantCoinColor7("Distant Coin Colour 8", Color) = (0.45, 0.3, 0.2, 1)
        [HideInInspector] _DistantCoinAmount0("Distant Coin Colour 1 Amount", Range(0, 1)) = 0.34
        [HideInInspector] _DistantCoinAmount1("Distant Coin Colour 2 Amount", Range(0, 1)) = 0.38
        [HideInInspector] _DistantCoinAmount2("Distant Coin Colour 3 Amount", Range(0, 1)) = 0.28
        [HideInInspector] _DistantCoinAmount3("Distant Coin Colour 4 Amount", Range(0, 1)) = 0
        [HideInInspector] _DistantCoinAmount4("Distant Coin Colour 5 Amount", Range(0, 1)) = 0
        [HideInInspector] _DistantCoinAmount5("Distant Coin Colour 6 Amount", Range(0, 1)) = 0
        [HideInInspector] _DistantCoinAmount6("Distant Coin Colour 7 Amount", Range(0, 1)) = 0
        [HideInInspector] _DistantCoinAmount7("Distant Coin Colour 8 Amount", Range(0, 1)) = 0
        [HideInInspector] _DistantCoinDensity("Distant Coin Density", Range(0.25, 32)) = 4
        [HideInInspector] _DistantCoinCoverage("Distant Coin Coverage", Range(0.01, 1)) = 0.35
        [HideInInspector] _DistantCoinIntensity("Distant Coin Intensity", Range(0, 8)) = 1.25
        [HideInInspector] _DistantCoinMaxPixels("Distant Coin Maximum Pixels", Range(1, 9)) = 4
        [HideInInspector] _DistantCoinGrowFar("Distant Coin Grow Far", Float) = 80
        [HideInInspector] _DistantCoinGrowNear("Distant Coin Grow Near", Float) = 28
        [HideInInspector] _DistantCoinFadeStart("Distant Coin Fade Start", Float) = 22
        [HideInInspector] _DistantCoinFadeEnd("Distant Coin Fade End", Float) = 10
        [HideInInspector] _DistantCoinTopMask("Distant Coin Top Mask", Range(0, 1)) = 0.65
        [HideInInspector] _DistantCoinShineEnabled("Distant Coin Shine", Float) = 0
        [HideInInspector] _DistantCoinShineIntensity("Distant Coin Shine Intensity", Range(0, 8)) = 1
        [HideInInspector] _DistantCoinShineMetallic("Distant Coin Shine Metallic", Range(0, 1)) = 0.9
        [HideInInspector] _DistantCoinShineSmoothness("Distant Coin Shine Smoothness", Range(0, 1)) = 0.75
        [HideInInspector] _DistantCoinShineNormalStrength("Distant Coin Shine Normal Strength", Range(0, 8)) = 1

        [Header(LOD)]
        [Toggle] _DisableLod("Disable Distance LOD", Float) = 0
        _LodNear("LOD Near Distance", Float) = 12
        _LodFar("LOD Far Distance", Float) = 35

        [Toggle(_TRIPLANAR)] _TriplanarEnabled("Triplanar", Float) = 1
        [Toggle(_DETAIL_ON)] _DetailEnabled("Detail Layer", Float) = 0

        // Kept in CBUFFER for SRP batching parity with the Stylized A/B shader.
        [HideInInspector] _StylizedMetalSoftness("Stylized Metal Softness", Range(0, 1)) = 0
        [HideInInspector] _StylizedContrast("Stylized Contrast", Range(0.5, 2)) = 1
        [HideInInspector] _StylizedSaturation("Stylized Saturation", Range(0.5, 2)) = 1
        [HideInInspector] _StylizedWarmTint("Stylized Warm Tint", Color) = (1, 0.85, 0.45, 1)
        [HideInInspector] _StylizedWarmStrength("Stylized Warm Strength", Range(0, 1)) = 0
        [HideInInspector] _RimColor("Rim Color", Color) = (1, 0.9, 0.55, 1)
        [HideInInspector] _RimPower("Rim Power", Range(0.5, 8)) = 3
        [HideInInspector] _RimIntensity("Rim Intensity", Range(0, 4)) = 0
        [HideInInspector] _SpecRampColor("Spec Ramp Color", Color) = (1, 0.95, 0.7, 1)
        [HideInInspector] _SpecRampThreshold("Spec Ramp Threshold", Range(0, 1)) = 0.72
        [HideInInspector] _SpecRampSoftness("Spec Ramp Softness", Range(0.01, 0.5)) = 0.08
        [HideInInspector] _SpecRampIntensity("Spec Ramp Intensity", Range(0, 4)) = 0
        [HideInInspector] _CreviceColor("Crevice Color", Color) = (0.45, 0.22, 0.08, 1)
        [HideInInspector] _CreviceStrength("Crevice Strength", Range(0, 2)) = 0
        [HideInInspector] _AlbedoMix("Albedo Mix", Range(0, 1)) = 0

        [HideInInspector] _Cutoff("Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull[_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GoldPileLitVert
            #pragma fragment GoldPileLitFrag

            #pragma shader_feature_local _POM_ON
            #pragma shader_feature_local _HEIGHTMAP_ON
            #pragma shader_feature_local _TRIPLANAR
            #pragma shader_feature_local _SPARKLE_ON
            #pragma shader_feature_local _DETAIL_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "GoldPileLighting.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GoldPileShadowVert
            #pragma fragment GoldPileShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "GoldPileInput.hlsl"
            #include "GoldPileCommon.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings GoldPileShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyGoldPileVertexDisplacement(input.positionOS.xyz, input.texcoord);
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
                return output;
            }

            half4 GoldPileShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GoldPileDepthVert
            #pragma fragment GoldPileDepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GoldPileInput.hlsl"
            #include "GoldPileCommon.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings GoldPileDepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyGoldPileVertexDisplacement(input.positionOS.xyz, input.texcoord);
                output.positionCS = TransformObjectToHClip(positionOS);
                return output;
            }

            half4 GoldPileDepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GoldPileDepthNormalsVert
            #pragma fragment GoldPileDepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GoldPileInput.hlsl"
            #include "GoldPileCommon.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings GoldPileDepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyGoldPileVertexDisplacement(input.positionOS.xyz, input.texcoord);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 GoldPileDepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
