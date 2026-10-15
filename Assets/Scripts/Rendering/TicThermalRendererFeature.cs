using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds a TI BASIC-style image from depth-tested surface temperatures.
/// Temperatures stay scalar until the final composite; rendered RGB is never
/// accumulated as heat and no screen-space or proxy heat volumes are generated.
/// </summary>
public sealed class TicThermalRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public sealed class PresentationSettings
    {
        [Tooltip("Project default, not an NFPA-mandated threshold.")]
        [Min(22f)] public float yellowThresholdCelsius = 150f;

        [Tooltip("Project default, not an NFPA-mandated threshold.")]
        [Min(23f)] public float orangeThresholdCelsius = 300f;

        [Tooltip("Project default, not an NFPA-mandated threshold.")]
        [Min(24f)] public float redThresholdCelsius = 450f;

        [Range(1f, 40f)] public float thresholdBlendCelsius = 15f;
    }

    [SerializeField] private PresentationSettings presentation = new();

    private sealed class TicThermalPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId[] ShaderTags =
        {
            new("UniversalForwardOnly"),
            new("UniversalForward"),
            new("SRPDefaultUnlit"),
            new("LightweightForward")
        };

        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int ThermalRawMaskId = Shader.PropertyToID("_ThermalRawMask");
        private static readonly int YellowThresholdId = Shader.PropertyToID("_YellowThresholdCelsius");
        private static readonly int OrangeThresholdId = Shader.PropertyToID("_OrangeThresholdCelsius");
        private static readonly int RedThresholdId = Shader.PropertyToID("_RedThresholdCelsius");
        private static readonly int ThresholdBlendId = Shader.PropertyToID("_ThresholdBlendCelsius");
        private static readonly MaterialPropertyBlock CompositeProperties = new();

        private readonly Material maskMaterial;
        private readonly Material compositeMaterial;
        private readonly PresentationSettings settings;
        private readonly List<ShaderTagId> shaderTags = new(ShaderTags);

        private sealed class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle rawMask;
            public Material material;
            public float yellowThreshold;
            public float orangeThreshold;
            public float redThreshold;
            public float thresholdBlend;
        }

        public TicThermalPass(
            Material mask,
            Material composite,
            PresentationSettings presentationSettings)
        {
            maskMaterial = mask;
            compositeMaterial = composite;
            settings = presentationSettings;
            // Composite after transparent scene elements so fire particles are
            // part of the grayscale thermal image. The composite preserves
            // the small colored TIC meter band from the source image.
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid())
                return;

            TextureHandle sourceColor = resources.activeColorTexture;
            TextureDesc maskDescriptor = renderGraph.GetTextureDesc(sourceColor);
            maskDescriptor.name = "TIC Temperature Mask (Celsius)";
            // One scalar channel: the visible solid surface temperature in Celsius.
            maskDescriptor.colorFormat = GraphicsFormat.R16_SFloat;
            maskDescriptor.depthBufferBits = DepthBits.None;
            maskDescriptor.msaaSamples = MSAASamples.None;
            maskDescriptor.clearBuffer = true;
            maskDescriptor.clearColor = Color.black;
            TextureHandle rawMask = renderGraph.CreateTexture(maskDescriptor);

            AddTemperatureMaskPass(renderGraph, frameData, rawMask, resources.activeDepthTexture);

            TextureDesc outputDescriptor = renderGraph.GetTextureDesc(sourceColor);
            outputDescriptor.name = "TIC Thermal Camera Color";
            outputDescriptor.clearBuffer = false;
            TextureHandle output = renderGraph.CreateTexture(outputDescriptor);
            AddCompositePass(renderGraph, sourceColor, rawMask, output);

            resources.cameraColor = output;
        }

        private void AddTemperatureMaskPass(RenderGraph renderGraph, ContextContainer frameData, TextureHandle mask, TextureHandle depth)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                shaderTags, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
            drawingSettings.overrideMaterial = maskMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;

            FilteringSettings filteringSettings = new(RenderQueueRange.opaque, ~0);
            RendererListParams rendererListParams = new(renderingData.cullResults, drawingSettings, filteringSettings);
            RendererListHandle rendererList = renderGraph.CreateRendererList(rendererListParams);

            using var builder = renderGraph.AddRasterRenderPass<MaskPassData>("TIC Surface Temperature Mask", out MaskPassData passData);
            passData.rendererList = rendererList;
            builder.UseRendererList(passData.rendererList);
            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            if (depth.IsValid())
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
            builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
            {
                context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1f, 0);
                context.cmd.DrawRendererList(data.rendererList);
            });
        }

        private void AddCompositePass(RenderGraph renderGraph, TextureHandle source, TextureHandle rawMask, TextureHandle destination)
        {
            using var builder = renderGraph.AddRasterRenderPass<CompositePassData>("TIC TI BASIC Composite", out CompositePassData passData);
            passData.source = source;
            passData.rawMask = rawMask;
            passData.material = compositeMaterial;
            passData.yellowThreshold = settings.yellowThresholdCelsius;
            passData.orangeThreshold = settings.orangeThresholdCelsius;
            passData.redThreshold = settings.redThresholdCelsius;
            passData.thresholdBlend = settings.thresholdBlendCelsius;

            builder.UseTexture(passData.source, AccessFlags.Read);
            builder.UseTexture(passData.rawMask, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
            {
                CompositeProperties.Clear();
                CompositeProperties.SetTexture(BlitTextureId, data.source);
                CompositeProperties.SetTexture(ThermalRawMaskId, data.rawMask);
                CompositeProperties.SetFloat(YellowThresholdId, data.yellowThreshold);
                CompositeProperties.SetFloat(OrangeThresholdId, data.orangeThreshold);
                CompositeProperties.SetFloat(RedThresholdId, data.redThreshold);
                CompositeProperties.SetFloat(ThresholdBlendId, data.thresholdBlend);
                CompositeProperties.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, CompositeProperties);
            });
        }

    }

    private Material maskMaterial;
    private Material compositeMaterial;
    private TicThermalPass pass;

    public override void Create()
    {
        DisposeResources();
        NormalizeSettings();
        maskMaterial = CreateMaterial("Hidden/TIC/TemperatureMask");
        compositeMaterial = CreateMaterial("Hidden/TIC/WhiteHotComposite");

        if (maskMaterial != null && compositeMaterial != null)
            pass = new TicThermalPass(maskMaterial, compositeMaterial, presentation);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null || renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            return;

        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        DisposeResources();
        pass = null;
    }

    private void OnValidate()
    {
        NormalizeSettings();
    }

    private void NormalizeSettings()
    {
        presentation ??= new PresentationSettings();
        presentation.yellowThresholdCelsius = Mathf.Max(22f, presentation.yellowThresholdCelsius);
        presentation.orangeThresholdCelsius = Mathf.Max(
            presentation.yellowThresholdCelsius + 1f,
            presentation.orangeThresholdCelsius);
        presentation.redThresholdCelsius = Mathf.Max(
            presentation.orangeThresholdCelsius + 1f,
            presentation.redThresholdCelsius);
    }

    private static Material CreateMaterial(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"TIC renderer could not find shader '{shaderName}'.");
            return null;
        }

        return CoreUtils.CreateEngineMaterial(shader);
    }

    private void DisposeResources()
    {
        CoreUtils.Destroy(maskMaterial);
        CoreUtils.Destroy(compositeMaterial);
        maskMaterial = null;
        compositeMaterial = null;
    }
}
