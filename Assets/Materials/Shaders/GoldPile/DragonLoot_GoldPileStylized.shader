Shader "DragonLoot/Gold Pile Stylized"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 0.78, 0.28, 1)

        _MetallicGlossMap("Metallic Smoothness", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0.72
        _Smoothness("Smoothness", Range(0, 1)) = 0.45

        [Normal] _BumpMap("Coin Normal", 2D) = "bump" {}
        _BumpScale("Coin Normal Scale", Range(0, 2)) = 1
        _CoinNormalStrength("Coin Normal Strength", Range(0, 2)) = 1

        [Normal] _PileNormalMap("Pile Normal", 2D) = "bump" {}
        _PileNormalStrength("Pile Normal Strength", Range(0, 2)) = 0.35

        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1

        [Header(PileDeform)]
        [Toggle] _DeformEnabled("Runtime Deform Map", Float) = 0
        _DeformMap("Deform Height Map", 2D) = "black" {}
        _DeformScale("Deform Height Scale", Range(0, 8)) = 1.5
        [HideInInspector] _DeformWorldSize("Deform World Size", Float) = 4
        [HideInInspector] _DeformResolution("Deform Resolution", Float) = 64

        _WorldTiling("World Tiling", Float) = 0.35

        _HueVariation("Hue Variation", Range(0, 0.2)) = 0.03
        _BrightnessVariation("Brightness Variation", Range(0, 0.5)) = 0.08
        _RoughnessVariation("Roughness Variation", Range(0, 0.5)) = 0.1

        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap("Detail Albedo", 2D) = "gray" {}
        _DetailAlbedoScale("Detail Albedo Scale", Range(0, 2)) = 1
        [Normal] _DetailNormalMap("Detail Normal", 2D) = "bump" {}
        _DetailNormalScale("Detail Normal Scale", Range(0, 2)) = 1
        _EdgeDirtStrength("Edge Dirt Strength", Range(0, 2)) = 0.75

        [Header(Distant Coin Pixels)]
        [Toggle] _DistantCoinEnabled("Distant Coin Pixels", Float) = 1
        [HDR] _DistantCoinColor0("Colour 1", Color) = (1.0, 0.78, 0.28, 1)
        [HDR] _DistantCoinColor1("Colour 2", Color) = (0.82, 0.86, 0.92, 1)
        [HDR] _DistantCoinColor2("Colour 3", Color) = (0.95, 0.45, 0.2, 1)
        [HDR] _DistantCoinColor3("Colour 4", Color) = (0.55, 0.75, 0.35, 1)
        [HDR] _DistantCoinColor4("Colour 5", Color) = (0.35, 0.55, 0.95, 1)
        [HDR] _DistantCoinColor5("Colour 6", Color) = (0.75, 0.35, 0.85, 1)
        [HDR] _DistantCoinColor6("Colour 7", Color) = (0.95, 0.75, 0.35, 1)
        [HDR] _DistantCoinColor7("Colour 8", Color) = (0.45, 0.3, 0.2, 1)
        _DistantCoinAmount0("Colour 1 Amount", Range(0, 1)) = 0.34
        _DistantCoinAmount1("Colour 2 Amount", Range(0, 1)) = 0.38
        _DistantCoinAmount2("Colour 3 Amount", Range(0, 1)) = 0.28
        _DistantCoinAmount3("Colour 4 Amount", Range(0, 1)) = 0
        _DistantCoinAmount4("Colour 5 Amount", Range(0, 1)) = 0
        _DistantCoinAmount5("Colour 6 Amount", Range(0, 1)) = 0
        _DistantCoinAmount6("Colour 7 Amount", Range(0, 1)) = 0
        _DistantCoinAmount7("Colour 8 Amount", Range(0, 1)) = 0
        _DistantCoinDensity("Density", Range(0.25, 32)) = 4
        _DistantCoinCoverage("Coverage", Range(0.01, 1)) = 0.35
        _DistantCoinIntensity("Intensity", Range(0, 8)) = 1.25
        [IntRange] _DistantCoinMaxPixels("Maximum Pixels", Range(1, 9)) = 4
        _DistantCoinGrowFar("Grow Far Distance", Float) = 80
        _DistantCoinGrowNear("Grow Near Distance", Float) = 28
        _DistantCoinFadeStart("Near Fade Start", Float) = 22
        _DistantCoinFadeEnd("Near Fade End", Float) = 10
        _DistantCoinTopMask("Top Surface Mask", Range(0, 1)) = 0.65

        [Header(Distant Coin Pixel Shine)]
        [Toggle] _DistantCoinShineEnabled("Shine", Float) = 0
        _DistantCoinShineIntensity("Shine Intensity", Range(0, 8)) = 1
        _DistantCoinShineMetallic("Metallic", Range(0, 1)) = 0.9
        _DistantCoinShineSmoothness("Smoothness", Range(0, 1)) = 0.75
        _DistantCoinShineNormalStrength("Deform Normal Strength", Range(0, 8)) = 1

        [Toggle(_DETAIL_ON)] _DetailEnabled("Detail Layer", Float) = 0

        [Header(StylizedLook)]
        _StylizedMetalSoftness("Metal Softness", Range(0, 1)) = 0.55
        _StylizedContrast("Contrast", Range(0.5, 2)) = 1.15
        _StylizedSaturation("Saturation", Range(0.5, 2)) = 1.25
        _StylizedWarmTint("Warm Tint", Color) = (1, 0.82, 0.4, 1)
        _StylizedWarmStrength("Warm Strength", Range(0, 1)) = 0.45
        _AlbedoMix("Painted Albedo Mix", Range(0, 1)) = 0.35

        [Header(Rim)]
        [HDR] _RimColor("Rim Color", Color) = (1.4, 1.05, 0.45, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity("Rim Intensity", Range(0, 4)) = 0.85

        [Header(SpecRamp)]
        [HDR] _SpecRampColor("Spec Ramp Color", Color) = (1.6, 1.35, 0.7, 1)
        _SpecRampThreshold("Spec Ramp Threshold", Range(0, 1)) = 0.68
        _SpecRampSoftness("Spec Ramp Softness", Range(0.01, 0.5)) = 0.1
        _SpecRampIntensity("Spec Ramp Intensity", Range(0, 4)) = 1.1

        [Header(Crevice)]
        _CreviceColor("Crevice Color", Color) = (0.42, 0.18, 0.06, 1)
        _CreviceStrength("Crevice Strength", Range(0, 2)) = 0.9

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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #define GOLDPILE_STYLIZED 1
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

                float3 positionOS = ApplyGoldPileRuntimeDeform(input.positionOS.xyz, input.texcoord);
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

                float3 positionOS = ApplyGoldPileRuntimeDeform(input.positionOS.xyz, input.texcoord);
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

                float3 positionOS = ApplyGoldPileRuntimeDeform(input.positionOS.xyz, input.texcoord);
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
