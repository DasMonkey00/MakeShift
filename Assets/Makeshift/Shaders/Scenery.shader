// The forest's URP shader: plain sun and sky lighting worked out per vertex, so it costs little per pixel, with the
// pack's wind, and for grass a fade that has no detail swaps to see. Looks like URP Simple Lit without its highlights.
//
// Grass: meshes carry, in their third UV set, each blade's foot (xyz) and a mark (w): 1 for a blade that sinks away
// with distance, 0.5 for one that stays and widens to make up the cover. ForestGrass makes those meshes and sets the
// distances. Whole clumps then shrink into the ground, each at its own distance.
Shader "Makeshift/Scenery"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Cut out", Float) = 0
        _Cutoff ("Cut-off", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Wrap ("Light wrapping round to the unlit side", Range(0,1)) = 0
        [Toggle(_FLAT_COLOR)] _FlatColor ("One colour, texture only cuts the shape", Float) = 0
        _Lit ("Lit by sun and sky", Range(0,1)) = 1
        _Shadows ("Darkened by shadows", Range(0,1)) = 1

        [Toggle(_WIND)] _Wind ("Wind", Float) = 0
        _WindNoiseTexture ("Wind noise", 2D) = "white" {}
        _WindParams ("Wind: scale, scroll, jitter, jitter scale", Vector) = (0.1, 0.03, 0.15, 2)

        [Toggle(_GRASS)] _GrassFade ("Grass", Float) = 0
        _TopColor ("Grass colour towards the tips", Color) = (1,1,1,1)
        _GradientLevel ("Grass gradient height", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "StormWind.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_WindNoiseTexture); SAMPLER(sampler_WindNoiseTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _TopColor;
            float4 _WindParams;
            half _Cutoff;
            half _Wrap;
            half _GradientLevel;
            half _Lit;
            half _Shadows;
            float _Cull;
        CBUFFER_END

        // x: blades start sinking, y: all gone, z and w: nearest and furthest distance a whole clump fades at.
        float4 _MkGrassFade;
        float3 _MkGrassEye;

        // The same number for a clump here and in ForestGrass, which leaves out the ones already faded away.
        float MkRank(float2 at)
        {
            uint2 q = uint2(int2(floor(at * 16.0)));
            uint h = (q.x * 73856093u) ^ (q.y * 19349663u);
            return float((h >> 8) & 1023u) / 1023.0;
        }

        float3 MkShape(float3 positionOS, float4 blade, float4 colour)
        {
            #if defined(_GRASS)
            if (_MkGrassFade.w > 0.0)
            {
                float3 pivot = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float far = distance(pivot.xz, _MkGrassEye.xz);
                float thinned = smoothstep(_MkGrassFade.x, _MkGrassFade.y, far);
                if (blade.w > 0.75) positionOS = lerp(positionOS, blade.xyz, thinned);
                else if (blade.w > 0.25) positionOS.xz = blade.xz + (positionOS.xz - blade.xz) * (1.0 + 0.9 * thinned);
                float gone = lerp(_MkGrassFade.z, _MkGrassFade.w, MkRank(pivot.xz));
                positionOS *= 1.0 - smoothstep(gone * 0.75, gone, far);
            }
            #endif
            #if defined(_WIND)
            {
                float3 world = TransformObjectToWorld(positionOS);
                float2 at = world.xz * _WindParams.x;
                float time = MkWindTime();
                float3 a = abs(SAMPLE_TEXTURE2D_LOD(_WindNoiseTexture, sampler_WindNoiseTexture, at + time * _WindParams.y, 0).rgb);
                float3 b = SAMPLE_TEXTURE2D_LOD(_WindNoiseTexture, sampler_WindNoiseTexture,
                    at * _WindParams.w + time * _WindParams.z, 0).rgb;
                positionOS += MkWindShape(pow(a, 2.5) * b * colour.rgb);
            }
            #endif
            return positionOS;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_vertex _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _FLAT_COLOR
            #pragma shader_feature_local _GRASS
            #pragma shader_feature_local_vertex _WIND

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float4 blade : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 sky : TEXCOORD2;   // ambient and small lights
                half3 sun : TEXCOORD3;   // dimmed by shadow per pixel
                half fog : TEXCOORD4;
                half3 tint : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 shaped = MkShape(input.positionOS.xyz, input.blade, input.colour);
                float3 world = TransformObjectToWorld(shaped);
                o.positionWS = world;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                #if defined(_GRASS)
                // Blades take the ground's light, so a clump does not flicker bright and dark as it turns.
                half3 normal = half3(0.0, 1.0, 0.0);
                o.tint = lerp(_BaseColor.rgb, _TopColor.rgb, saturate(input.uv.y + (-1.0 + 2.0 * _GradientLevel)));
                #else
                half3 normal = TransformObjectToWorldNormal(input.normalOS);
                o.tint = _BaseColor.rgb;
                #endif

                Light main = GetMainLight();
                half facing = saturate((dot(normal, main.direction) + _Wrap) / (1.0 + _Wrap));
                o.sun = main.color * facing;
                o.sky = SampleSH(normal);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light small = GetAdditionalLight(i, world);
                    half toward = saturate((dot(normal, small.direction) + _Wrap) / (1.0 + _Wrap));
                    o.sky += small.color * (small.distanceAttenuation * toward);
                }
                #endif
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 sample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                #if defined(_ALPHATEST_ON)
                clip(sample.a - _Cutoff);
                #endif
                #if defined(_FLAT_COLOR)
                half3 albedo = input.tint;
                #else
                half3 albedo = sample.rgb * input.tint;
                #endif
                half shadow = 1.0;
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                Light lit = GetMainLight(TransformWorldToShadowCoord(input.positionWS), input.positionWS, half4(1, 1, 1, 1));
                shadow = lerp(1.0, lit.shadowAttenuation, _Shadows);
                #endif
                half3 colour = albedo * lerp(half3(1, 1, 1), input.sky + input.sun * shadow, _Lit);
                return half4(MixFog(colour, input.fog), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _GRASS
            #pragma shader_feature_local_vertex _WIND
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float4 blade : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 world = TransformObjectToWorld(MkShape(input.positionOS.xyz, input.blade, input.colour));
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction = normalize(_LightPosition - world);
                #else
                float3 direction = _LightDirection;
                #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(world, normal, direction));
                o.positionCS = ApplyShadowClamping(clip);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local _GRASS
            #pragma shader_feature_local_vertex _WIND

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 colour : COLOR;
                float2 uv : TEXCOORD0;
                float4 blade : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(MkShape(input.positionOS.xyz, input.blade, input.colour));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
