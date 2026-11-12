Shader "Mario64Remake/ToonLit"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.918, 0.859, 0.769, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _ShadowTint ("Shadow Tint (Palette Slate)", Color) = (0.180, 0.220, 0.322, 1)
        _RimColor ("Rim Color", Color) = (1.0, 0.949, 0.863, 1)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.5
        _RimStrength ("Rim Strength", Range(0.0, 1.0)) = 0.25
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)
        _Bands ("Toon Bands", Range(2, 4)) = 3
        _BandSoftness ("Band Edge Softness", Range(0.01, 0.1)) = 0.035
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _ShadowTint;
                float4 _RimColor;
                float4 _EmissionColor;
                float _RimPower;
                float _RimStrength;
                float _Bands;
                float _BandSoftness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float3 baseAlbedo = texColor.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                float atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                float litVal = NdotL * lerp(0.35, 1.0, atten);

                // Quantized 2-3 band ramp with crisp smoothstep edges
                float band1 = smoothstep(0.38 - _BandSoftness, 0.38 + _BandSoftness, litVal);
                float band2 = smoothstep(0.72 - _BandSoftness, 0.72 + _BandSoftness, litVal);
                float toonRamp = band1 * 0.65 + band2 * 0.35;

                // Shadow band is palette-tinted, never black
                float3 shadowColor = lerp(baseAlbedo * 0.52, _ShadowTint.rgb, 0.45);
                float3 litColor = baseAlbedo * mainLight.color;
                float3 color = lerp(shadowColor, litColor, toonRamp);

                // Subtle stylized rim highlight on sunlit edges
                float rimDot = 1.0 - saturate(dot(viewDirWS, normalWS));
                float rim = smoothstep(0.65, 0.72, pow(rimDot, _RimPower) * saturate(NdotL));
                color += rim * _RimStrength * _RimColor.rgb;

                // Ambient sky/ground hemisphere bounce
                float upFactor = normalWS.y * 0.5 + 0.5;
                float3 ambient = SampleSH(normalWS) * baseAlbedo * 0.28;
                color += ambient;

                // HDR Emission for stars, coins, speed pads
                color += _EmissionColor.rgb;

                color = MixFog(color, input.fogFactor);
                return half4(color, _BaseColor.a * texColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 position : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.position.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}

