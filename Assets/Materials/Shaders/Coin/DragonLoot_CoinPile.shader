Shader "DragonLoot/Coin Pile"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 0.84, 0.3, 1)

        _MetallicGlossMap("Metallic Smoothness", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0.92
        _Smoothness("Smoothness", Range(0, 1)) = 0.75

        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0, 2)) = 1

        [Header(Reflection)]
        _ReflectionFloor("Reflection Floor", Range(0, 0.5)) = 0.12

        [Header(Fresnel)]
        [HDR] _FresnelColor("Fresnel Color", Color) = (1, 0.82, 0.45, 1)
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 0.25
        _FresnelPower("Fresnel Power", Range(1, 8)) = 4

        [Header(Instance Variation)]
        _TintVariation("Tint Variation", Range(0, 0.2)) = 0.04
        _ValueVariation("Value Variation", Range(0, 0.2)) = 0.04

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

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CoinPileLitVert
            #pragma fragment CoinPileLitFrag

            // Forward+ (URP 17) requires cluster keywords or lights evaluate to black.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "CoinPileLighting.hlsl"
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
            #pragma vertex CoinPileDepthVert
            #pragma fragment CoinPileDepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half _BumpScale;
                half _ReflectionFloor;
                half4 _FresnelColor;
                half _FresnelIntensity;
                half _FresnelPower;
                half _TintVariation;
                half _ValueVariation;
                half _Cutoff;
                half _Cull;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings CoinPileDepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 CoinPileDepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
