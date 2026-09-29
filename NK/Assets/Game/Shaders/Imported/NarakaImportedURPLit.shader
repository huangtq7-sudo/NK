Shader "Naraka/URP/Imported Character"
{
    Properties
    {
        _BaseMap ("Base Color", 2D) = "white" {}
        _BaseColor ("Base Tint", Color) = (1,1,1,1)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0,2)) = 1
        _MRAVMap ("MRAV (R=Metal, G=Roughness, B=AO)", 2D) = "white" {}
        [HideInInspector] _MRAVMode ("MRAV Mode (0=Packed, 1=Roughness Only)", Float) = 0
        _MetallicScale ("Metallic", Range(0,1)) = 1
        _RoughnessScale ("Roughness", Range(0,1)) = 1
        _OcclusionStrength ("Occlusion", Range(0,1)) = 1
        _EmissionMap ("Emission", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _DetailMap ("Detail Color", 2D) = "white" {}
        _DetailMRAVMap ("Detail MRAV", 2D) = "white" {}
        _SkinMap ("Skin Detail", 2D) = "gray" {}
        _DetailStrength ("Detail Strength", Range(0,1)) = 1
        _MaskMap ("Opacity Mask", 2D) = "white" {}
        [HideInInspector] _UseBaseRGB ("Use Base Texture RGB", Float) = 1
        [HideInInspector] _MaskChannel ("Mask Channel (0=R, 1=A)", Float) = 0
        [HideInInspector] _NormalSwapRG ("Swap Normal R/G", Float) = 0
        [HideInInspector] _NormalInvertR ("Invert Normal R", Float) = 0
        [Toggle] _UseNormal ("Use Normal", Float) = 0
        [Toggle] _UseMRAV ("Use MRAV", Float) = 0
        [Toggle] _UseEmission ("Use Emission", Float) = 0
        [Toggle] _UseDetail ("Use Detail", Float) = 0
        [Toggle] _UseDetailMRAV ("Use Detail MRAV", Float) = 0
        [Toggle] _UseSkin ("Use Skin Detail", Float) = 0
        [Toggle] _UseMask ("Use Opacity Mask", Float) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _Blend ("Blend", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_MRAVMap); SAMPLER(sampler_MRAVMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
            TEXTURE2D(_DetailMRAVMap); SAMPLER(sampler_DetailMRAVMap);
            TEXTURE2D(_SkinMap); SAMPLER(sampler_SkinMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _NormalScale;
                half _MetallicScale;
                half _RoughnessScale;
                half _OcclusionStrength;
                half _DetailStrength;
                half _UseNormal;
                half _UseMRAV;
                half _MRAVMode;
                half _UseEmission;
                half _UseDetail;
                half _UseDetailMRAV;
                half _UseSkin;
                half _UseMask;
                half _UseBaseRGB;
                half _MaskChannel;
                half _NormalSwapRG;
                half _NormalInvertR;
                half _AlphaClip;
                half _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexLighting = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 rawBase = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 baseSample = half4(lerp(half3(1, 1, 1), rawBase.rgb, _UseBaseRGB), rawBase.a) * _BaseColor;
                half4 detailSample = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, input.uv);
                half detailBlend = detailSample.a * _DetailStrength * _UseDetail;
                baseSample.rgb = lerp(baseSample.rgb, detailSample.rgb, saturate(detailBlend));
                half4 skinSample = SAMPLE_TEXTURE2D(_SkinMap, sampler_SkinMap, input.uv);
                half skinColorScale = lerp(0.65h, 1.35h, skinSample.g);
                baseSample.rgb *= lerp(1.0h, skinColorScale, _UseSkin * 0.35h);

                half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                half selectedMask = lerp(maskSample.r, maskSample.a, _MaskChannel);
                half mask = lerp(1.0h, selectedMask, _UseMask);
                half alpha = baseSample.a * mask;
                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif

                half3 normalTS = half3(0, 0, 1);
                if (_UseNormal > 0.5h)
                {
                    normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalScale);
                    normalTS.xy = lerp(normalTS.xy, normalTS.yx, _NormalSwapRG);
                    normalTS.x *= lerp(1.0h, -1.0h, _NormalInvertR);
                }
                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));

                half4 mrav = SAMPLE_TEXTURE2D(_MRAVMap, sampler_MRAVMap, input.uv);
                half4 detailMrav = SAMPLE_TEXTURE2D(_DetailMRAVMap, sampler_DetailMRAVMap, input.uv);
                mrav = lerp(mrav, detailMrav, detailMrav.a * _UseDetailMRAV);
                half sampledMetallic = lerp(mrav.r, 0.0h, _MRAVMode);
                half sampledRoughness = lerp(mrav.g, mrav.r, _MRAVMode);
                half sampledOcclusion = lerp(mrav.b, 1.0h, _MRAVMode);
                half metallic = lerp(_MetallicScale, sampledMetallic * _MetallicScale, _UseMRAV);
                half roughness = lerp(_RoughnessScale, sampledRoughness * _RoughnessScale, _UseMRAV);
                roughness = lerp(roughness, skinSample.r, _UseSkin * 0.6h);
                half occlusion = lerp(1.0h, lerp(1.0h, sampledOcclusion, _OcclusionStrength), _UseMRAV);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb * _UseEmission;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseSample.rgb;
                surfaceData.metallic = saturate(metallic);
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = saturate(1.0h - roughness);
                surfaceData.normalTS = normalTS;
                surfaceData.emission = emission;
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = alpha;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
