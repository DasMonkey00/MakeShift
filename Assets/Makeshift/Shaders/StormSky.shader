// Storm cloud on a dome around the player, faded in over the skybox. Two texture reads per sky pixel,
// and trees and ground hide it through the depth test.
Shader "Makeshift/StormSky"
{
    Properties
    {
        _Noise ("Cloud noise", 2D) = "gray" {}
        _Dark ("Cloud shadow", Color) = (0.07, 0.085, 0.11, 1)
        _Light ("Cloud light", Color) = (0.27, 0.31, 0.37, 1)
        _Horizon ("Horizon", Color) = (0.17, 0.2, 0.24, 1)
        _Storm ("Storm", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _FlashDir ("Flash direction", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" "RenderType" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                half4 _Dark;
                half4 _Light;
                half4 _Horizon;
                half _Storm;
                half _Flash;
                float4 _FlashDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.direction);
                float2 uv = dir.xz / (max(dir.y, 0.0) + 0.2) * 0.2;
                float t = _Time.y;
                half a = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv + t * float2(0.012, 0.005)).r;
                half b = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 2.6 + t * float2(0.027, -0.008) + a * 0.15).r;
                half cloud = saturate(a * 0.7 + b * 0.5 - 0.1);

                half3 color = lerp(_Dark.rgb, _Light.rgb, smoothstep(0.3, 0.78, cloud));
                half toward = saturate(dot(dir, normalize(_FlashDir.xyz)));
                half glow = toward * toward * toward;
                glow = glow * glow * 1.8 + 0.12;
                color += half3(0.72, 0.8, 1.0) * (_Flash * glow * (0.25 + smoothstep(0.25, 0.85, cloud)));

                color = lerp(_Horizon.rgb, color, smoothstep(0.0, 0.28, dir.y));
                return half4(color, _Storm);
            }
            ENDHLSL
        }
    }
}
