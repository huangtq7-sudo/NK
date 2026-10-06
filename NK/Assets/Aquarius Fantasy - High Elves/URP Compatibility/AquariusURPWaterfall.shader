Shader "Aquarius Fantasy/URP/Waterfall"
{
    Properties
    {
        Texture2D_45B5617D ("Waterfall Texture", 2D) = "white" {}
        Texture2D_5BF4DB6D ("Flow Texture", 2D) = "white" {}
        Texture2D_C45B3B0E ("Foam Texture", 2D) = "white" {}
        _Color_1 ("Dark Color", Color) = (0.05,0.15,0.2,0.5)
        _Color_2 ("Light Color", Color) = (0.65,0.9,1,0.8)
        _Alpha ("Alpha", Range(0,1)) = 0.65
        _Float ("Brightness", Range(0,3)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardWaterfall"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(Texture2D_45B5617D); SAMPLER(sampler_Texture2D_45B5617D);
            TEXTURE2D(Texture2D_5BF4DB6D); SAMPLER(sampler_Texture2D_5BF4DB6D);
            TEXTURE2D(Texture2D_C45B3B0E); SAMPLER(sampler_Texture2D_C45B3B0E);

            CBUFFER_START(UnityPerMaterial)
                float4 Texture2D_45B5617D_ST;
                half4 _Color_1;
                half4 _Color_2;
                half _Alpha;
                half _Float;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, Texture2D_45B5617D);
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv1 = input.uv + float2(0.0, -_Time.y * 0.42);
                float2 uv2 = input.uv * float2(1.7, 1.15) + float2(_Time.y * 0.05, -_Time.y * 0.73);
                half flowA = SAMPLE_TEXTURE2D(Texture2D_45B5617D, sampler_Texture2D_45B5617D, uv1).r;
                half flowB = SAMPLE_TEXTURE2D(Texture2D_5BF4DB6D, sampler_Texture2D_5BF4DB6D, uv2).r;
                half foam = SAMPLE_TEXTURE2D(Texture2D_C45B3B0E, sampler_Texture2D_C45B3B0E, uv2 * 1.6).r;
                half mask = saturate(flowA * 0.65h + flowB * 0.35h);
                half3 color = lerp(_Color_1.rgb, _Color_2.rgb, saturate(mask + foam * 0.35h)) * max(_Float, 0.05h);
                color = MixFog(color, input.fogFactor);
                half alpha = saturate(_Alpha * smoothstep(0.05h, 0.8h, mask + foam * 0.25h));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
