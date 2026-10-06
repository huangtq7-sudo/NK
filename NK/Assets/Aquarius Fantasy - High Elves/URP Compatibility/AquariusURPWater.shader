Shader "Aquarius Fantasy/URP/Water"
{
    Properties
    {
        _NormalsMain ("Main Normal", 2D) = "bump" {}
        _NormalsSecondary ("Secondary Normal", 2D) = "bump" {}
        _FoamTexture ("Foam", 2D) = "white" {}
        _FoamTexture_1 ("Foam Detail", 2D) = "white" {}
        _ShallowColor ("Shallow Color", Color) = (0.2,0.45,0.55,0.45)
        _DeepColor ("Deep Color", Color) = (0.03,0.08,0.12,0.75)
        _Surface_Color ("Surface Color", Color) = (0.3,0.6,0.8,0.5)
        _FoamColor ("Foam Color", Color) = (0.8,0.85,0.9,0.8)
        _Depth ("Depth", Range(0.01,5)) = 1
        _FoamDistance ("Foam Distance", Range(0.01,5)) = 0.5
        _FoamStrength ("Foam Strength", Range(0,2)) = 1
        _FoamTiling ("Foam Tiling", Float) = 20
        _MainTiling ("Main Tiling", Float) = 7
        _SecondaryTiling ("Secondary Tiling", Float) = 13
        _NormalsSpeedA ("Normal Speed A", Float) = 12
        _NormalsSpeedB ("Normal Speed B", Float) = -26
        _NormalsStrength ("Normal Strength", Range(0,2)) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
        _Metallic ("Metallic", Range(0,1)) = 0
        _Alpha ("Alpha", Range(0,1)) = 0.55
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
            Name "ForwardWater"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalsMain); SAMPLER(sampler_NormalsMain);
            TEXTURE2D(_NormalsSecondary); SAMPLER(sampler_NormalsSecondary);
            TEXTURE2D(_FoamTexture); SAMPLER(sampler_FoamTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _Surface_Color;
                half4 _FoamColor;
                half _Depth;
                half _FoamDistance;
                half _FoamStrength;
                half _FoamTiling;
                half _MainTiling;
                half _SecondaryTiling;
                half _NormalsSpeedA;
                half _NormalsSpeedB;
                half _NormalsStrength;
                half _Smoothness;
                half _Metallic;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half4 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(p.positionCS);
                output.normalWS = n.normalWS;
                output.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float timeA = _Time.y * _NormalsSpeedA * 0.0025;
                float timeB = _Time.y * _NormalsSpeedB * 0.0025;
                float2 uvA = input.uv * max(_MainTiling, 0.01) + float2(timeA, timeA * 0.61);
                float2 uvB = input.uv * max(_SecondaryTiling, 0.01) + float2(timeB * 0.47, timeB);
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalsMain, sampler_NormalsMain, uvA));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalsSecondary, sampler_NormalsSecondary, uvB));
                half3 normalTS = normalize(half3((nA.xy + nB.xy) * _NormalsStrength, nA.z * nB.z));
                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float depthDifference = max(0.0, sceneEyeDepth - surfaceEyeDepth);
                half depthLerp = saturate(depthDifference / max(_Depth, 0.01));

                half3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthLerp);
                baseColor = lerp(baseColor, _Surface_Color.rgb, 0.35h);
                half foamMask = 1.0h - saturate(depthDifference / max(_FoamDistance, 0.01));
                half foamNoise = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, input.uv * _FoamTiling + _Time.yy * 0.03).r;
                foamMask *= smoothstep(0.35h, 0.75h, foamNoise) * _FoamStrength;

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDir = SafeNormalize(viewDir + mainLight.direction);
                half specular = pow(saturate(dot(normalWS, halfDir)), lerp(16.0h, 128.0h, _Smoothness));
                half3 color = baseColor * (SampleSH(normalWS) + mainLight.color * (0.25h + ndl * 0.75h));
                color += mainLight.color * specular * lerp(0.15h, 1.0h, _Smoothness);
                color = lerp(color, _FoamColor.rgb, saturate(foamMask));
                color = MixFog(color, input.fogFactor);
                half alpha = saturate(max(_Alpha, lerp(_ShallowColor.a, _DeepColor.a, depthLerp)) + foamMask * 0.25h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
