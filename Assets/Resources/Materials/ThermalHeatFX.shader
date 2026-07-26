Shader "HazardSimulation/ThermalHeatFX"
{
    Properties
    {
        _CoolColor ("Cool Color", Color) = (0.003, 0, 0.24, 1)
        _WarmColor ("Warm Color", Color) = (0.10, 0.55, 0, 1)
        _HotColor ("Hot Color", Color) = (1, 0, 0, 1)
        _Temperature ("Temperature", Range(0, 1)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Softness ("Depth Softness", Range(0, 1)) = 0.2
        _EffectMode ("Effect Mode", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale", Float) = 5
        _NoiseSpeed ("Noise Speed", Float) = 0.7
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "ThermalFX"
            Tags { "LightMode" = "ThermalFX" }

            // Thermal colors represent measured temperature and should replace
            // the colder color beneath them. Additive blending combines red
            // heat with the purple background and incorrectly produces pink.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 color : COLOR;
                float4 screenPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoolColor;
                float4 _WarmColor;
                float4 _HotColor;
                float _Temperature;
                float _Opacity;
                float _Softness;
                float _EffectMode;
                float _NoiseScale;
                float _NoiseSpeed;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.screenPosition = ComputeScreenPos(positionInputs.positionCS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centeredUv = input.uv * 2.0 - 1.0;
                float radial = saturate(1.0 - dot(centeredUv, centeredUv));

                // Ground heat is an elliptical pool. Air heat is one coherent,
                // tapered field whose edge drifts gently instead of a collection
                // of circular particles that can be mistaken for smoke.
                float vertical = saturate(input.uv.y);
                float wave = sin(vertical * 13.0 + _Time.y * _NoiseSpeed * 1.3);
                wave += sin(vertical * 27.0 - _Time.y * _NoiseSpeed * 0.7) * 0.45;
                float plumeCenter = wave * 0.035 * vertical;
                float plumeHalfWidth = lerp(1.02, 0.28, smoothstep(0.05, 1.0, vertical));
                float plumeField = saturate(1.0 - abs(centeredUv.x - plumeCenter) / plumeHalfWidth);
                float plumeVerticalMask = smoothstep(0.0, 0.08, vertical) *
                    (1.0 - smoothstep(0.86, 1.0, vertical));

                float groundMask = smoothstep(0.0, 0.16, radial);
                float plumeMask = smoothstep(0.0, 0.16, plumeField) * plumeVerticalMask;
                float field = lerp(radial, plumeField * lerp(1.0, 0.58, vertical), _EffectMode);
                float shapeMask = lerp(groundMask, plumeMask, _EffectMode);

                float noisePhase = dot(input.uv, float2(7.3, 11.7)) * _NoiseScale;
                float animatedNoise = sin(noisePhase + _Time.y * _NoiseSpeed);
                animatedNoise *= sin(noisePhase * 0.63 - _Time.y * _NoiseSpeed * 1.37);
                animatedNoise = lerp(0.82, 1.0, animatedNoise * 0.5 + 0.5);

                float softFade = 1.0;
                if (_Softness > 0.0001)
                {
                    float2 screenUv = input.screenPosition.xy / input.screenPosition.w;
                    float sceneDepth = SampleSceneDepth(screenUv);
                    float sceneEyeDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                    float fragmentEyeDepth = -TransformWorldToView(input.positionWS).z;
                    softFade = saturate((sceneEyeDepth - fragmentEyeDepth) / _Softness);
                }

                float heat = saturate(_Temperature);
                float alpha = shapeMask * animatedNoise * input.color.a * _Opacity * softFade;
                clip(alpha - 0.003);

                // Keep the visible edge warm, then rise through yellow/orange to
                // red at the heat source. The aura fades out before reaching the
                // cold purple portion of the TIC palette.
                float edgeHeat = max(0.43, heat * 0.58);
                float spatialHeat = lerp(edgeHeat, heat, smoothstep(0.0, 1.0, field));
                float3 coolToWarm = lerp(_CoolColor.rgb, _WarmColor.rgb, saturate(spatialHeat / 0.4));
                float3 warmToHot = lerp(_WarmColor.rgb, _HotColor.rgb, saturate((spatialHeat - 0.4) / 0.6));
                float3 thermalColor = spatialHeat < 0.4 ? coolToWarm : warmToHot;
                return half4(thermalColor * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
