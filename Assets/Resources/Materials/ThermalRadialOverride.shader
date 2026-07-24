Shader "Thermal/Thermal Radial Override"
{
    Properties
    {
        _Temperature ("Temperature", Range(0, 1)) = 0
        _HeatOrigin ("Heat Origin", Vector) = (0, 0, 0, 0)
        _HeatRadius ("Heat Radius", Float) = 100000
        _UseLocalizedHeat ("Use Localized Heat", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float _Temperature;
                float4 _HeatOrigin;
                float _HeatRadius;
                float _UseLocalizedHeat;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half3 SampleThermalGradient(float heat)
            {
                const float midpoint = 0.406103611;
                const half3 cold = half3(0.002671842, 0.0, 0.241509318);
                const half3 warm = half3(0.103045650, 0.547169805, 0.0);
                const half3 hot = half3(1.0, 0.0, 0.001680851);

                if (heat <= midpoint)
                    return lerp(cold, warm, saturate(heat / midpoint));

                return lerp(warm, hot, saturate((heat - midpoint) / (1.0 - midpoint)));
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float heat = saturate(_Temperature);

                if (_UseLocalizedHeat > 0.5)
                {
                    float distanceFromOrigin = distance(input.positionWS, _HeatOrigin.xyz);
                    float radialHeat = saturate(1.0 - distanceFromOrigin / max(_HeatRadius, 0.001));
                    heat *= radialHeat;
                }

                return half4(SampleThermalGradient(heat), 1.0);
            }
            ENDHLSL
        }
    }
}
