Shader "Mario64Remake/StylizedTriplanarPBR"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Detail Albedo (Triplanar)", 2D) = "white" {}
        [NoScaleOffset] _BumpMap ("Detail Normal (Triplanar)", 2D) = "bump" {}
        _TriplanarScale ("World Scale", Float) = 0.28
        _DetailStrength ("Detail Contrast", Range(0, 1)) = 0.75
        _BumpScale ("Normal Strength", Range(0, 2)) = 0.85
        _Smoothness ("Smoothness", Range(0, 1)) = 0.22
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 0)
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _EmissionColor;
                float _TriplanarScale;
                float _DetailStrength;
                float _BumpScale;
                float _Smoothness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 nWS = normalize(input.normalWS);
                float3 blend = pow(abs(nWS), 4.0);
                blend /= max(blend.x + blend.y + blend.z, 0.0001);

                float3 wp = input.positionWS * _TriplanarScale;
                half3 albedoX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, wp.zy).rgb;
                half3 albedoY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, wp.xz).rgb;
                half3 albedoZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, wp.xy).rgb;
                half3 detailAlbedo = albedoX * blend.x + albedoY * blend.y + albedoZ * blend.z;
                detailAlbedo = lerp(half3(1, 1, 1), detailAlbedo, _DetailStrength);

                half3 normX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, wp.zy), _BumpScale);
                half3 normY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, wp.xz), _BumpScale);
                half3 normZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, wp.xy), _BumpScale);

                float3 worldNorm = normalize(
                    float3(0, normX.y, normX.x) * blend.x +
                    float3(normY.x, 0, normY.y) * blend.y +
                    float3(normZ.x, normZ.y, 0) * blend.z +
                    nWS
                );

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = worldNorm;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(worldNorm);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = _BaseColor.rgb * detailAlbedo;
                surfaceData.alpha = _BaseColor.a;
                surfaceData.smoothness = _Smoothness;
                surfaceData.metallic = 0.0;
                surfaceData.specular = half3(0.25, 0.25, 0.25);
                surfaceData.occlusion = 1.0;
                surfaceData.emission = _EmissionColor.rgb;

                half4 col = UniversalFragmentBlinnPhong(inputData, surfaceData);
                col.rgb = MixFog(col.rgb, input.fogFactor);
                return col;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}

