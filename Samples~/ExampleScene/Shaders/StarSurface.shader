Shader "Custom/Stellar Star Unified"
{
    Properties
    {
        [KeywordEnum(Sphere NdotV, Billboard UV)] _GradientSource ("Gradient Source Mode", Float) = 0

        [Header(Core Colors and Intensity)]
        [HDR]_CoreColor ("Core Color (HDR)", Color) = (1, 0.9, 0.7, 1)
        [HDR]_SurfaceColor ("Surface Color (HDR)", Color) = (1, 0.6, 0.2, 1) // Will be used for inside view
        _Intensity ("Overall Intensity", Range(1, 300)) = 80

        [Header(Surface Dynamics)]
        [NoScaleOffset]_NoiseTexA ("Noise Texture A (Grayscale)", 2D) = "white" {}
        _NoiseScaleA ("Noise Scale A", Float) = 15
        _NoiseSpeedA ("Noise Speed A (XY Scroll)", Vector) = (0.02, 0.01, 0, 0)
        [NoScaleOffset]_NoiseTexB ("Noise Texture B (Grayscale)", 2D) = "white" {}
        _NoiseScaleB ("Noise Scale B", Float) = 25
        _NoiseSpeedB ("Noise Speed B (XY Scroll)", Vector) = (-0.01, 0.03, 0, 0)
        _DistortionStrength("Noise Distortion Strength", Range(0, 0.5)) = 0.1
        _NoiseBrightnessInfluence("Noise Brightness Influence", Range(0, 2)) = 0.5

        [Header(Core and Limb Effect)]
        _CoreBrightnessPower("Core Brightness Falloff Power", Range(0.1, 10)) = 4.0
        _LimbColorPower ("Limb Color Transition Power", Range(0.1, 10)) = 2.5

        [Header(Optional Corona)]
        _EnableCorona ("Enable Corona", Float) = 1.0
        [HDR]_CoronaColor ("Corona Color (HDR)", Color) = (1, 0.5, 0.1, 0.5)
        _CoronaBias("Corona Start Bias", Range(0.0, 1.0)) = 0.05
        _CoronaSize ("Corona Radial Size", Range(0.0, 3.0)) = 0.8
        _CoronaPower ("Corona Falloff Power", Range(0.1, 10)) = 4.0 // Unused currently

        [Header(Billboard Settings Used if Mode is Billboard)]
        _UVCenter ("UV Center", Vector) = (0.5, 0.5, 0, 0)
        _UVRadius ("UV Radius for Limb", Float) = 0.5
        _BillboardBrightnessCorrection("Billboard Power Correction", Range(0.5, 3.0)) = 1.0

        [Header(Rendering Options)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1 // One
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0 // Off
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }
        LOD 100

        Pass
        {
            Name "DynamicStarUniversalPass"
            Tags { "LightMode"="UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off // IMPORTANT: Must be Off to render backfaces

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _GRADIENTSOURCE_BILLBOARD_UV
            #pragma multi_compile_fog

            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 texcoord     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                #if defined(REQUIRES_VERTEX_SHADING_DATA)
                    float4 fogFactorAndVertexLight : TEXCOORD3;
                #else
                    half fogFactor : TEXCOORD3;
                #endif
                float edgeMask      : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_NoiseTexA); SAMPLER(sampler_NoiseTexA);
            TEXTURE2D(_NoiseTexB); SAMPLER(sampler_NoiseTexB);

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _SurfaceColor;
                half _Intensity;
                half _NoiseScaleA;
                half4 _NoiseSpeedA;
                half _NoiseScaleB;
                half4 _NoiseSpeedB;
                half _DistortionStrength;
                half _NoiseBrightnessInfluence;
                half _CoreBrightnessPower;
                half _LimbColorPower;
                half _EnableCorona;
                half4 _CoronaColor;
                half _CoronaBias;
                half _CoronaSize;
                half _CoronaPower;
                float2 _UVCenter;
                half _UVRadius;
                half _BillboardBrightnessCorrection;
            CBUFFER_END

            half SampleNoise(TEXTURE2D(tex), SAMPLER(samp), float2 uv, half scale, half4 speed)
            {
                float2 scrolledUV = uv * scale;
                scrolledUV += speed.xy * _Time.y;
                return SAMPLE_TEXTURE2D(tex, samp, scrolledUV).r;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionWS = posInputs.positionWS;
                output.positionCS = posInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.texcoord;

                float distFromCenter = length(input.texcoord - _UVCenter);
                half uvRadius = max(_UVRadius, 0.0001);
                output.edgeMask = 1.0 - smoothstep(uvRadius - 0.05h, uvRadius, distFromCenter);

                #if defined(REQUIRES_VERTEX_SHADING_DATA)
                    output.fogFactorAndVertexLight = half4(ComputeFogFactor(posInputs.positionCS.z), 0,0,0);
                #else
                    output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                #endif

                return output;
            }

            // --- REMOVED [[vk::location(0)]] ---
            half4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            // --- END REMOVAL ---
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 finalEmission = 0;

                if (isFrontFace) // --- FRONT FACE (Outside View) ---
                {
                    half gradientFactor = 0;
                    #if defined(_GRADIENTSOURCE_BILLBOARD_UV) // Billboard UV Mode
                        half uvRadius = max(_UVRadius, 0.0001);
                        float distFromCenter = length(input.uv - _UVCenter);
                        gradientFactor = saturate(1.0 - (distFromCenter / uvRadius));
                        gradientFactor *= input.edgeMask;
                    #else // Sphere NdotV Mode
                        float3 normalWS = normalize(input.normalWS);
                        float3 viewDirWS = normalize(GetWorldSpaceNormalizeViewDir(input.positionWS));
                        gradientFactor = saturate(dot(normalWS, viewDirWS));
                    #endif

                    half effectiveCorePower = _CoreBrightnessPower;
                    half effectiveLimbPower = _LimbColorPower;
                    #if defined(_GRADIENTSOURCE_BILLBOARD_UV)
                        effectiveCorePower *= _BillboardBrightnessCorrection;
                        effectiveLimbPower *= _BillboardBrightnessCorrection;
                    #endif

                    half noiseA = SampleNoise(_NoiseTexA, sampler_NoiseTexA, input.uv, _NoiseScaleA, _NoiseSpeedA);
                    float2 distortedUV_B = input.uv + (noiseA * 2.0 - 1.0) * _DistortionStrength;
                    half noiseB = SampleNoise(_NoiseTexB, sampler_NoiseTexB, distortedUV_B, _NoiseScaleB, _NoiseSpeedB);
                    half combinedNoise01 = saturate(noiseA * noiseB);

                    half colorLerpFactor = pow(gradientFactor, effectiveLimbPower);
                    half3 surfaceColor = lerp(_SurfaceColor.rgb, _CoreColor.rgb, colorLerpFactor);

                    half brightnessFactor = pow(gradientFactor, effectiveCorePower);
                    half noiseBrightnessMod = lerp(1.0 - _NoiseBrightnessInfluence, 1.0 + _NoiseBrightnessInfluence, combinedNoise01);
                    brightnessFactor *= noiseBrightnessMod;

                    half3 baseEmission = surfaceColor * brightnessFactor;
                    half3 coronaEmission = 0;

                    if (_EnableCorona > 0.5)
                    {
                        half limbFactor = 1.0 - gradientFactor;
                        half coronaStart = _CoronaBias;
                        half coronaEnd = saturate(_CoronaBias + _CoronaSize);
                        half coronaMask = smoothstep(coronaStart, coronaEnd, limbFactor);
                        coronaMask *= (1.0 - smoothstep(coronaEnd, saturate(coronaEnd + 0.1h), limbFactor));
                        coronaEmission = _CoronaColor.rgb * coronaMask * _CoronaColor.a;
                    }
                    finalEmission = baseEmission + coronaEmission;
                    finalEmission *= _Intensity;
                }
                else // --- BACK FACE (Inside View - Simplified) ---
                {
                    finalEmission = _SurfaceColor.rgb * _Intensity;
                }

                // --- Final Adjustments (applies to both paths) ---
                finalEmission = max(0, finalEmission);

                // Apply Fog
                #if defined(REQUIRES_VERTEX_SHADING_DATA)
                    finalEmission = MixFog(finalEmission, input.fogFactorAndVertexLight.x);
                #else
                    finalEmission = MixFog(finalEmission, input.fogFactor);
                #endif

                return half4(finalEmission, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}