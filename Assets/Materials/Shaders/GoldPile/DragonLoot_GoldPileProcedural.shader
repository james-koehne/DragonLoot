Shader "DragonLoot/Gold Pile Procedural"
{
    Properties
    {
        [MainTexture] _BaseMap("Coin Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Coin Tint", Color) = (1, 0.9, 0.45, 1)
        _GapColor("Gap Color (No Coin)", Color) = (0.55, 0.32, 0.08, 1)

        _MetallicGlossMap("Metallic Smoothness", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0.92
        _Smoothness("Smoothness", Range(0, 1)) = 0.75
        _GapMetallic("Gap Metallic", Range(0, 1)) = 0.85
        _GapSmoothness("Gap Smoothness", Range(0, 1)) = 0.35
        _MetallicVariation("Metallic Variation", Range(0, 0.25)) = 0

        [Normal] _BumpMap("Coin Normal", 2D) = "bump" {}
        _BumpScale("Coin Normal Scale", Range(0, 2)) = 1
        _CoinNormalStrength("Coin Normal Strength", Range(0, 2)) = 1

        [Header(Reflection)]
        _ReflectionFloor("Reflection Floor", Range(0, 0.5)) = 0.12

        [Header(Fresnel)]
        [HDR] _FresnelColor("Fresnel Color", Color) = (1, 0.82, 0.45, 1)
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 0.25
        _FresnelPower("Fresnel Power", Range(1, 8)) = 4

        [Header(Dirt)]
        _DirtMap("Dirt Texture", 2D) = "gray" {}
        _DirtStrength("Dirt Strength", Range(0, 2)) = 0
        _AOStrength("AO Strength", Range(0, 2)) = 0

        [Header(PileDeform)]
        [Toggle] _DeformEnabled("Runtime Deform Map", Float) = 0
        _DeformMap("Deform Height Map", 2D) = "black" {}
        _DeformScale("Deform Height Scale", Range(0, 8)) = 1.5
        _DeformNormalSoften("Deform Normal Soften", Range(0, 1)) = 0
        _DeformSampleBlur("Deform Sample Blur", Range(0, 4)) = 0
        [HideInInspector] _DeformWorldSize("Deform World Size", Float) = 4
        [HideInInspector] _DeformResolution("Deform Resolution", Float) = 64

        [Header(ProceduralCoins)]
        _CoinDiameter("Coin Diameter", Range(0.01, 0.2)) = 0.05
        _CoinDensity("Coin Density", Range(50, 2000)) = 400
        _CellJitter("Cell Jitter", Range(0, 1)) = 0.65
        _RotationRandomness("Rotation Randomness", Range(0, 1)) = 1
        _CoinTilt("Coin Tilt", Range(0, 0.5)) = 0.2
        _RimBevelStrength("Rim Bevel Strength", Range(0, 1.5)) = 0.55
        _RimBevelWidth("Rim Bevel Width", Range(0.02, 0.4)) = 0.14
        _RimAoStrength("Rim AO Strength", Range(0, 1)) = 0.35
        _BurialAmount("Burial Amount", Range(0, 1)) = 0.08
        _TintVariation("Tint Variation", Range(0, 0.2)) = 0.04
        _SmoothnessVariation("Smoothness Variation", Range(0, 0.3)) = 0
        _SpecularVariation("Specular Variation", Range(0, 0.3)) = 0
        _HueVariation("Hue Variation", Range(0, 0.15)) = 0
        _ValueVariation("Value Variation", Range(0, 0.2)) = 0.04
        _EdgeHighlightStrength("Edge Highlight Strength", Range(0, 2)) = 0
        _EdgeWidth("Edge Width", Range(0.02, 0.5)) = 0.12
        _CoinUVCenter("Coin UV Center", Vector) = (0.5, 0.58, 0, 0)
        _CoinUVScale("Coin UV Scale", Range(0.25, 4)) = 1.35

        [Header(MetalMix)]
        _CopperAmount("Copper Amount", Range(0, 1)) = 0.2
        _SilverAmount("Silver Amount", Range(0, 1)) = 0.1
        _CopperBaseMap("Copper Albedo", 2D) = "white" {}
        _CopperColor("Copper Tint", Color) = (1, 0.62, 0.38, 1)
        _SilverBaseMap("Silver Albedo", 2D) = "white" {}
        _SilverColor("Silver Tint", Color) = (0.92, 0.94, 0.98, 1)

        [Header(Distant Coin Pixels)]
        [Toggle] _DistantCoinEnabled("Distant Coin Pixels", Float) = 1
        [HDR] _DistantCoinColor0("Colour 1", Color) = (1.0, 0.78, 0.28, 1)
        [HDR] _DistantCoinColor1("Colour 2", Color) = (0.82, 0.86, 0.92, 1)
        [HDR] _DistantCoinColor2("Colour 3", Color) = (0.95, 0.45, 0.2, 1)
        [HDR] _DistantCoinColor3("Colour 4", Color) = (0.55, 0.32, 0.08, 1)
        _DistantCoinAmount0("Colour 1 Amount", Range(0, 1)) = 0.4
        _DistantCoinAmount1("Colour 2 Amount", Range(0, 1)) = 0.25
        _DistantCoinAmount2("Colour 3 Amount", Range(0, 1)) = 0.2
        _DistantCoinAmount3("Colour 4 Amount", Range(0, 1)) = 0.15
        _DistantCoinDensity("Density", Range(0.25, 32)) = 4
        _DistantCoinCoverage("Coverage", Range(0.01, 1)) = 0.35
        _DistantCoinIntensity("Intensity", Range(0, 8)) = 1.25
        [IntRange] _DistantCoinMaxPixels("Maximum Pixels", Range(1, 9)) = 4
        _DistantCoinGrowFar("Grow Far Distance", Float) = 80
        _DistantCoinGrowNear("Grow Near Distance", Float) = 28
        _DistantCoinFadeStart("Near Fade Start", Float) = 22
        _DistantCoinFadeEnd("Near Fade End", Float) = 10
        _DistantCoinTopMask("Top Surface Mask", Range(0, 1)) = 0.65

        [Header(Distant Coin Metal Focus)]
        [Toggle] _DistantCoinMetalFocusEnabled("Focus On Metallic Shine", Float) = 0
        _DistantCoinMetalFocus("Metal Focus Strength", Range(0, 1)) = 0.75
        _DistantCoinMetalFocusPower("Metal Focus Power", Range(1, 64)) = 16

        [Header(LOD)]
        [Toggle] _DisableLod("Disable Distance LOD", Float) = 0
        _LodNear("Near LOD Distance", Float) = 8
        _LodMid("Medium LOD Distance", Float) = 18
        _LodFar("Far LOD Distance", Float) = 40
        _LodNoiseFade("Distance Noise Fade", Range(0, 1)) = 1

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
            #pragma vertex GoldPileProcLitVert
            #pragma fragment GoldPileProcLitFrag

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

            #include "GoldPileProceduralLighting.hlsl"
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
            #pragma vertex GoldPileProcShadowVert
            #pragma fragment GoldPileProcShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "GoldPileProceduralCommon.hlsl"

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

            ShadowVaryings GoldPileProcShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyProcRuntimeDeform(input.positionOS.xyz, input.texcoord);
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

            half4 GoldPileProcShadowFrag(ShadowVaryings input) : SV_Target
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
            #pragma vertex GoldPileProcDepthVert
            #pragma fragment GoldPileProcDepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GoldPileProceduralCommon.hlsl"

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

            DepthVaryings GoldPileProcDepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyProcRuntimeDeform(input.positionOS.xyz, input.texcoord);
                output.positionCS = TransformObjectToHClip(positionOS);
                return output;
            }

            half4 GoldPileProcDepthFrag(DepthVaryings input) : SV_Target
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
            #pragma vertex GoldPileProcDepthNormalsVert
            #pragma fragment GoldPileProcDepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GoldPileProceduralCommon.hlsl"

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

            DepthNormalsVaryings GoldPileProcDepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = ApplyProcRuntimeDeform(input.positionOS.xyz, input.texcoord);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 GoldPileProcDepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
