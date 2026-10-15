Shader "Hidden/TIC/WhiteHotComposite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "TI BASIC Composite"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_ThermalRawMask);
            float _YellowThresholdCelsius;
            float _OrangeThresholdCelsius;
            float _RedThresholdCelsius;
            float _ThresholdBlendCelsius;

            half3 TemperatureColour(float temperature)
            {
                const half3 yellow = half3(0.78, 0.64, 0.20);
                const half3 orange = half3(0.82, 0.32, 0.10);
                const half3 red = half3(0.68, 0.075, 0.045);

                half orangeAmount = smoothstep(
                    _YellowThresholdCelsius,
                    _OrangeThresholdCelsius,
                    temperature);
                half redAmount = smoothstep(
                    _OrangeThresholdCelsius,
                    _RedThresholdCelsius,
                    temperature);
                return lerp(lerp(yellow, orange, orangeAmount), red, redAmount);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 scene = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel).rgb;
                float temperature = SAMPLE_TEXTURE2D_X_LOD(
                    _ThermalRawMask, sampler_PointClamp, uv, 0).r;

                // Compress additive fire/VFX brightness before deriving structural
                // detail. Optical RGB can shape the image, but never determines heat.
                half peak = max(max(scene.r, scene.g), scene.b);
                scene /= 1.0h + peak;
                half luminance = dot(scene, half3(0.2126, 0.7152, 0.0722));
                half whiteHot = saturate((luminance - 0.16h) * 1.62h + 0.20h);
                whiteHot = pow(max(whiteHot, 0.001h), 0.82h);

                float2 centred = uv * 2.0 - 1.0;
                half vignette = saturate(1.0 - dot(centred, centred) * 0.10);
                half detectorNoise = frac(
                    sin(dot(floor(input.positionCS.xy), float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                whiteHot = saturate(whiteHot * vignette + detectorNoise * 0.004h);
                whiteHot = lerp(0.018h, 0.93h, whiteHot);
                half structuralDetail = whiteHot;

                half warmth = smoothstep(21.0, max(22.0, _YellowThresholdCelsius), temperature);
                half thermalLift = warmth * lerp(0.24h, 0.42h, structuralDetail);
                whiteHot = saturate(whiteHot + thermalLift * (1.0h - whiteHot));

                half3 grayscale = whiteHot.xxx;

                // Restore the restrained TIC palette: cool surfaces stay
                // white-hot grayscale while hotter measured surfaces transition
                // through yellow, orange, and red.
                float blendWidth = max(_ThresholdBlendCelsius, 1.0);
                half colourAmount = smoothstep(
                    _YellowThresholdCelsius - blendWidth,
                    _YellowThresholdCelsius + blendWidth,
                    temperature);
                half orangeAmount = smoothstep(
                    _OrangeThresholdCelsius - blendWidth,
                    _OrangeThresholdCelsius + blendWidth,
                    temperature);
                half redAmount = smoothstep(
                    _RedThresholdCelsius - blendWidth,
                    _RedThresholdCelsius + blendWidth,
                    temperature);
                half3 palette = TemperatureColour(temperature);
                half detail = lerp(0.62h, 0.96h, structuralDetail);
                half3 coloured = palette * detail;
                half colourStrength = colourAmount * lerp(0.24h, 0.46h, orangeAmount);
                colourStrength = lerp(colourStrength, 0.66h, redAmount * colourAmount);
                half3 thermalImage = lerp(grayscale, coloured, colourStrength);

                // The meter is rendered into the same camera target. Preserve
                // only its saturated fill pixels after the thermal pass so the
                // physical TIC screen keeps its color cue without allowing
                // transparent fire particles to bypass the grayscale image.
                half sourcePeak = max(max(scene.r, scene.g), scene.b);
                half sourceFloor = min(min(scene.r, scene.g), scene.b);
                half sourceChroma = sourcePeak - sourceFloor;
                half meterRegion = step(0.68h, uv.x) * step(uv.y, 0.065h);
                half meterPixel = meterRegion * step(0.10h, sourceChroma);
                return half4(lerp(thermalImage, scene, meterPixel), 1.0);
            }
            ENDHLSL
        }
    }
}
