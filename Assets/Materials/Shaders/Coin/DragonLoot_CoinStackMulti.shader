Shader "DragonLoot/Coin Stack Multi"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)

        _BaseMapArray("Albedo Array", 2DArray) = "" {}
        _MetallicGlossMapArray("Metallic Smoothness Array", 2DArray) = "" {}
        [Normal] _BumpMapArray("Normal Array", 2DArray) = "" {}

        _Metallic("Metallic", Range(0, 1)) = 0.92
        _Smoothness("Smoothness", Range(0, 1)) = 0.75

        _BumpScale("Cap Normal Scale", Range(0, 2)) = 1
        _SideBumpScale("Side Normal Scale", Range(0, 2)) = 0.35

        [Header(Reflection)]
        _ReflectionFloor("Reflection Floor", Range(0, 1)) = 0.12

        [Header(Fresnel)]
        [HDR] _FresnelColor("Fresnel Color Fallback", Color) = (1, 0.82, 0.45, 1)
        _FresnelIntensity("Fresnel Intensity", Range(0, 4)) = 0.25
        _FresnelPower("Fresnel Power", Range(1, 8)) = 4
        _TypeCount("Type Count", Float) = 3

        [Header(Shine)]
        _ShineBoost("Shine Boost", Range(0, 2)) = 0.35
        _SpecularIntensity("Specular Intensity", Range(0, 4)) = 0.75
        _SpecularPower("Specular Power", Range(8, 256)) = 64

        [Header(Sparkles)]
        [Toggle] _SparkleEnabled("Sparkles", Float) = 0
        [HDR] _SparkleColor("Sparkle Color", Color) = (1, 0.92, 0.65, 1)
        _SparkleIntensity("Sparkle Intensity", Range(0, 8)) = 0
        _SparkleDensity("Sparkle Density", Range(1, 64)) = 14
        _SparkleSharpness("Sparkle Sharpness", Range(4, 128)) = 48
        _SparkleSpeed("Sparkle Speed", Range(0, 10)) = 2
        _SparkleCoverage("Sparkle Coverage", Range(0.05, 1)) = 0.3
        _SparkleFlicker("Sparkle Flicker", Range(0, 1)) = 0.45

        [Header(Instance Variation)]
        _TintVariation("Tint Variation", Range(0, 0.2)) = 0.04
        _ValueVariation("Value Variation", Range(0, 0.2)) = 0.04
        [HideInInspector] _VariationSeed("Variation Seed", Float) = 1

        [Header(Stack Bands)]
        _CoinCount("Coin Count", Float) = 1
        _CoinTypeMap("Coin Type Map", 2D) = "black" {}
        _BandContrast("Band Contrast", Range(0, 2)) = 1.1
        _GrooveDarkness("Groove Darkness", Range(0, 1)) = 0.35
        _GrooveWidth("Groove Width", Range(0.01, 0.25)) = 0.08
        _RidgeSoftness("Ridge Softness", Range(0, 0.15)) = 0.02
        _CapNormalThreshold("Cap Normal Threshold", Range(0.3, 0.95)) = 0.55
        _RidgeAlbedoBoost("Ridge Albedo Boost", Range(0, 0.2)) = 0.02
        _GrooveSmoothnessScale("Groove Smoothness Scale", Range(0, 1)) = 0.82
        _RidgeSmoothnessScale("Ridge Smoothness Scale", Range(0, 1.2)) = 1.0
        _GrooveMetallicScale("Groove Metallic Scale", Range(0, 1)) = 0.92
        _MeshBoundsMinY("Mesh Bounds Min Y", Float) = -0.05
        _MeshBoundsSizeY("Mesh Bounds Size Y", Float) = 0.1

        [Header(Side Normals)]
        _GrooveNormalStrength("Groove Normal Strength", Range(0, 2)) = 0.65
        _GrooveNormalBias("Groove Normal Bias", Range(0, 2)) = 1
        _CoinEdgeBevelWidth("Coin Edge Bevel Width", Range(0.01, 0.35)) = 0.12
        _CoinEdgeBevelStrength("Coin Edge Bevel Strength", Range(0, 1)) = 0.45
        _SideFacetStrength("Side Facet Strength", Range(0, 0.5)) = 0.04
        _SideFacetFrequency("Side Facet Frequency", Range(1, 32)) = 12

        [Header(Seam Parallax)]
        _SeamWorldOffsetMax("Seam World Offset Max", Range(0, 0.05)) = 0.012
        _SeamViewAlignStart("Seam View Align Start", Range(0, 1)) = 0.7
        _SeamViewAlignEnd("Seam View Align End", Range(0, 1)) = 0.92
        _SeamClipWidth("Seam Rim Width", Range(0.001, 0.45)) = 0.12
        [Toggle] _SeamClipGrooveOnly("Seam Grooves Only", Float) = 0
        _SeamSideClip("Seam Side Clip", Range(0, 1)) = 0
        _SeamNormalStrength("Seam Normal Strength", Range(0, 2)) = 0.55
        _SeamSoftAO("Seam Soft AO", Range(0, 1)) = 0.4

        // Dummy ST so TRANSFORM_TEX / UnityPerMaterial stay compatible with shared lighting.
        [HideInInspector] _BaseMap("Albedo Dummy", 2D) = "white" {}
        [HideInInspector] _Cutoff("Cutoff", Range(0, 1)) = 0.5
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
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull[_Cull]
            ZWrite On

            // Treasure sparkle mask bit (Ref 64 / bit 6). Avoids URP default stencil Ref 1.
            Stencil
            {
                Ref 64
                Comp Always
                Pass Replace
                ReadMask 64
                WriteMask 64
            }

            HLSLPROGRAM
            #pragma target 3.5
            #define _COIN_STACK_MULTI 1
            #pragma vertex CoinStackLitVert
            #pragma fragment CoinStackLitFrag

            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "CoinStackLighting.hlsl"
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
            #pragma target 3.5
            #pragma vertex CoinStackShadowVert
            #pragma fragment CoinStackShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "CoinStackMaterial.hlsl"
            #include "CoinStackSeamClip.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float stackY01    : TEXCOORD2;
                nointerpolation float instanceSeed : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings CoinStackShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.normalOS = input.normalOS;
                output.stackY01 = CoinStackComputeStackY01(input.positionOS.y);
                output.instanceSeed = CoinStackInstanceSeed();

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                float3 shadowedWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                output.positionCS = TransformWorldToHClip(shadowedWS);
#if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
                return output;
            }

            half4 CoinStackShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float coinCount = max((float)_CoinCount, 1.0);
                if (!CoinStackIsCap(input.normalOS))
                {
                    CoinStackBandData bands = CoinStackEvaluateBands(input.stackY01, coinCount);
                    CoinStackSeamView seam = CoinStackEvaluateSeamView(
                        input.positionWS,
                        input.normalOS,
                        input.stackY01,
                        coinCount,
                        input.instanceSeed,
                        bands.grooveMask,
                        bands.ridgeMask);
                    CoinStackClipSeamSide(seam);
                }
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
            #pragma target 3.5
            #pragma vertex CoinStackDepthVert
            #pragma fragment CoinStackDepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CoinStackMaterial.hlsl"
            #include "CoinStackSeamClip.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float stackY01    : TEXCOORD2;
                nointerpolation float instanceSeed : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings CoinStackDepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.normalOS = input.normalOS;
                output.stackY01 = CoinStackComputeStackY01(input.positionOS.y);
                output.instanceSeed = CoinStackInstanceSeed();
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

			half4 CoinStackDepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float coinCount = max((float)_CoinCount, 1.0);
                if (!CoinStackIsCap(input.normalOS))
                {
                    CoinStackBandData bands = CoinStackEvaluateBands(input.stackY01, coinCount);
                    CoinStackSeamView seam = CoinStackEvaluateSeamView(
                        input.positionWS,
                        input.normalOS,
                        input.stackY01,
                        coinCount,
                        input.instanceSeed,
                        bands.grooveMask,
                        bands.ridgeMask);
                    CoinStackClipSeamSide(seam);
                }
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
            #pragma target 3.5
            #pragma vertex CoinStackDepthNormalsVert
            #pragma fragment CoinStackDepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CoinStackMaterial.hlsl"
            #include "CoinStackSeamClip.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float stackY01    : TEXCOORD3;
                nointerpolation float instanceSeed : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings CoinStackDepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.normalOS = input.normalOS;
                output.stackY01 = CoinStackComputeStackY01(input.positionOS.y);
                output.instanceSeed = CoinStackInstanceSeed();
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 CoinStackDepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float coinCount = max((float)_CoinCount, 1.0);
                if (!CoinStackIsCap(input.normalOS))
                {
                    CoinStackBandData bands = CoinStackEvaluateBands(input.stackY01, coinCount);
                    CoinStackSeamView seam = CoinStackEvaluateSeamView(
                        input.positionWS,
                        input.normalOS,
                        input.stackY01,
                        coinCount,
                        input.instanceSeed,
                        bands.grooveMask,
                        bands.ridgeMask);
                    CoinStackClipSeamSide(seam);
                }
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "DragonLoot/Coin Stack"
}
