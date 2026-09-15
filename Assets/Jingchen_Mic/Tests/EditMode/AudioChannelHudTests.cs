using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Jingchen.Mic.Tests
{
    public sealed class AudioChannelHudTests
    {
        private GameObject head;
        private Component hud;
        private Text label;

        [SetUp]
        public void SetUp()
        {
            head = new GameObject("Audio Channel HUD Test Head");
            Type hudType = typeof(AudioChannelNetwork).Assembly.GetType("Jingchen.Mic.AudioChannelHud", true);
            MethodInfo create = hudType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null);
            hud = (Component)create.Invoke(null, new object[] { head.transform });
            label = hud.GetComponentInChildren<Text>();
        }

        [TearDown]
        public void TearDown()
        {
            if (head != null) UnityEngine.Object.DestroyImmediate(head);
        }

        [Test]
        public void WorldSpaceHudDoesNotInterceptControllerInteraction()
        {
            Assert.That(hud.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(hud.transform.parent, Is.EqualTo(head.transform));
            Assert.That(hud.transform.localPosition.z, Is.GreaterThan(0f));
            Assert.That(hud.GetComponent<Image>(), Is.Not.Null);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.font, Is.Not.Null);
            Assert.That(hud.GetComponentsInChildren<GraphicRaycaster>(true), Is.Empty);
            foreach (Graphic graphic in hud.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, graphic.name + " intercepts XR pointer input.");
        }

        [TestCase(true, "CONNECTING - MIC OFF")]
        [TestCase(false, "MIC OFF")]
        public void MissingConnectionShowsAnExplicitMutedState(bool available, string expected)
        {
            ShowDisconnected(available);
            Assert.That(label.text, Does.Contain("CHANNEL - | " + expected));
            Assert.That(label.text, Does.Contain("HOLD RIGHT A: TALK"));
            Assert.That(label.text, Does.Contain("TAP LEFT X: CHANNEL"));
            AssertLabelFits();
        }

        // Check the longest production messages without creating a network room.
        [TestCase("TALKING - RELEASE A TO FINISH")]
        [TestCase("LISTENING TO C - WAITING")]
        [TestCase("REQUESTING - WAIT TO SPEAK")]
        public void TalkAndWaitInstructionsFitTheWorldSpacePanel(string state)
        {
            label.text = "CHANNEL A | " + state +
                "\nHOLD RIGHT A: TALK    TAP LEFT X: CHANNEL";
            AssertLabelFits();
        }

        [Test]
        public void CameraRenderCapturesVisiblePanelAndText()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("HUD structure and text layout are tested separately; capture needs a graphics device.");

            ShowDisconnected(true);
            var cameraObject = new GameObject("Audio Channel HUD Test Camera", typeof(Camera));
            cameraObject.transform.SetParent(head.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.2f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 2f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            camera.cullingMask = 1 << 31;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            foreach (Transform child in hud.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 31;
            hud.GetComponent<Canvas>().worldCamera = camera;

            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture previousTarget = RenderTexture.active;
            RenderPipelineAsset previousDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            RenderPipelineAsset previousQualityPipeline = QualitySettings.renderPipeline;
            try
            {
                // Camera.Render is the built-in manual-render API. Temporarily
                // select it for this isolated capture and restore both settings.
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
                target.Create();
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();

                string path = Environment.GetEnvironmentVariable("AUDIO_CHANNEL_HUD_CAPTURE_PATH");
                if (string.IsNullOrEmpty(path))
                {
                    var project = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                    path = Path.Combine(project.Parent.FullName,
                        project.Name + "-audio-channel-validation", "hud.png");
                }
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                TestContext.WriteLine("HUD capture: " + path);

                int textPixels = 0;
                int panelPixels = 0;
                Color backgroundPixel = pixels.GetPixel(0, 0);
                float backgroundBrightness = backgroundPixel.r + backgroundPixel.g + backgroundPixel.b;
                foreach (Color pixel in pixels.GetPixels())
                {
                    if (pixel.r > 0.7f && pixel.g > 0.45f && pixel.b < 0.7f) textPixels++;
                    // Compare with the rendered clear color so both Gamma and
                    // Linear project color spaces use the same visibility test.
                    if (pixel.r + pixel.g + pixel.b < backgroundBrightness * 0.75f) panelPixels++;
                }
                Assert.That(textPixels, Is.GreaterThan(50), "Camera did not render the yellow connection text.");
                Assert.That(panelPixels, Is.GreaterThan(1000), "Camera did not render the dark HUD background.");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                QualitySettings.renderPipeline = previousQualityPipeline;
                GraphicsSettings.defaultRenderPipeline = previousDefaultPipeline;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        private void ShowDisconnected(bool available)
        {
            MethodInfo show = hud.GetType().GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(show, Is.Not.Null);
            show.Invoke(hud, new object[] { null, false, available });
        }

        private void AssertLabelFits()
        {
            Canvas.ForceUpdateCanvases();
            Rect bounds = label.rectTransform.rect;
            Assert.That(bounds.width, Is.GreaterThan(0f));
            Assert.That(bounds.height, Is.GreaterThan(0f));
            Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(bounds.width + 1f),
                "Instructions exceed the available width at the configured font size.");
            Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(bounds.height + 1f),
                "Instructions exceed the available panel height.");
        }
    }
}
