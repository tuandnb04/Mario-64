Shader "Mario64Remake/PaintingPortalWave"
{
    Properties
    {
        [MainColor] _BaseColor ("Primary Painting Color", Color) = (0.25, 0.78, 0.35, 1)
        _AccentColor ("Accent Motif Color", Color) = (1.0, 0.86, 0.25, 1)
        _FrameTrimColor ("Portal Shimmer Trim", Color) = (1.0, 0.94, 0.62, 1)
        [MainTexture] _BaseMap ("Painting Texture (Optional)", 2D) = "white" {}
        _RippleAmplitude ("Ripple Amplitude", Range(0.0, 0.15)) = 0.014
        _RippleFrequency ("Ripple Frequency", Float) = 24.0
        _RippleSpeed ("Ripple Speed", Float) = 4.2
        _RippleCenter ("Ripple Center (UV)", Vector) = (0.5, 0.5, 0.0, 0.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _AccentColor;
            float4 _FrameTrimColor;
            float4 _BaseMap_ST;
            float4 _RippleCenter;
            float _RippleAmplitude;
            float _RippleFrequency;
            float _RippleSpeed;
        CBUFFER_END
        ENDHLSL

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                float2 uvCentered = input.uv - _RippleCenter.xy;
                float dist = length(uvCentered);
                float waveZ = sin(dist * _RippleFrequency - _Time.y * _RippleSpeed) * _RippleAmplitude * 0.45;
                float3 displacedOS = input.positionOS.xyz + input.normalOS * waveZ;

                VertexPositionInputs posInputs = GetVertexPositionInputs(displacedOS);
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
                float2 center = _RippleCenter.xy;
                float2 delta = input.uv - center;
                float dist = length(delta);
                float2 radialDir = dist > 0.0001 ? (delta / dist) : float2(0.0, 1.0);

                float phase = dist * _RippleFrequency - _Time.y * _RippleSpeed;
                float ripple = sin(phase) * exp(-dist * 1.45);
                float2 warpedUV = input.uv + radialDir * (ripple * _RippleAmplitude);

                half4 texCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, warpedUV);

                // Procedural SM64 painting landscape/swirl motif blended with Base & Accent colors
                float horizon = smoothstep(0.32, 0.72, warpedUV.y + sin(warpedUV.x * 6.28318) * 0.06);
                float swirl = 0.5 + 0.5 * sin(warpedUV.x * 12.0 + warpedUV.y * 8.0 + _Time.y * 0.8);
                half3 motifCol = lerp(_BaseColor.rgb, _AccentColor.rgb, saturate(horizon * 0.65 + swirl * 0.35));
                half3 albedo = motifCol * texCol.rgb;

                // Water-surface normal perturbation for glossy magic portal highlights
                float3 nWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 tangentApprox = normalize(cross(nWS, abs(nWS.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 bitangentApprox = cross(nWS, tangentApprox);
                float rippleSlope = cos(phase) * _RippleAmplitude * 16.0;
                float3 perturbedN = normalize(nWS + (tangentApprox * radialDir.x + bitangentApprox * radialDir.y) * rippleSlope);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float NdotL = saturate(dot(perturbedN, mainLight.direction) * 0.55 + 0.45);
                float3 lit = albedo * (mainLight.color * NdotL * lerp(0.5, 1.0, mainLight.shadowAttenuation) + SampleSH(perturbedN) * 0.45);

                // Specular magic sheen + border rim glow
                float3 halfVec = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(perturbedN, halfVec)), 28.0) * 0.45;

                float edgeMask = max(
                    smoothstep(0.42, 0.50, abs(input.uv.x - 0.5)),
                    smoothstep(0.42, 0.50, abs(input.uv.y - 0.5)));
                float fresnel = pow(1.0 - saturate(dot(viewDirWS, perturbedN)), 2.5);
                float shimmer = saturate(edgeMask * 0.65 + fresnel * 0.45 + max(0.0, ripple) * 0.85);

                float3 finalCol = lit + spec * _FrameTrimColor.rgb + shimmer * _FrameTrimColor.rgb * (0.28 + _RippleAmplitude * 3.5);
                finalCol = MixFog(finalCol, input.fogFactor);
                return half4(finalCol, 1.0);
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
