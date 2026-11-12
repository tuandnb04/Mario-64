Shader "Mario64Remake/ToonSkybox"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.290, 0.565, 0.886, 1)
        _HorizonColor ("Horizon Color", Color) = (0.745, 0.890, 0.973, 1)
        _GroundColor ("Ground Color", Color) = (0.231, 0.620, 0.322, 1)
        _SunColor ("Sun Disc Color", Color) = (1.0, 0.949, 0.863, 1)
        _Exponent ("Horizon Exponent", Range(0.2, 4.0)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir    : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float4 _SunColor;
                float _Exponent;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDir = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDir);
                float y = dir.y;

                float skyBlend = pow(saturate(y), _Exponent);
                float groundBlend = pow(saturate(-y * 3.0), 1.2);

                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, skyBlend);
                col = lerp(col, _GroundColor.rgb, groundBlend);

                // Stylized quantized sun halo & disc
                Light mainLight = GetMainLight();
                float sunDot = dot(dir, mainLight.direction);
                float sunDisc = smoothstep(0.994, 0.996, sunDot);
                float sunHalo = smoothstep(0.94, 0.99, sunDot) * 0.35;
                col += (sunDisc * 1.6 + sunHalo) * _SunColor.rgb;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}

