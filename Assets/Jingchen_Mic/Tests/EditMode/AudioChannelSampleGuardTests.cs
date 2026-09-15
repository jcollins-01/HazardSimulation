using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Jingchen.Mic.Tests
{
    public sealed class AudioChannelSampleGuardTests
    {
        [Test]
        public void SamplesWithoutConfirmedPermissionAreSilencedAtSendTime()
        {
            var root = new GameObject("Audio sample guard test");
            try
            {
                var controller = root.AddComponent<AudioChannelController>();
                var method = typeof(AudioChannelController).GetMethod("GuardOutgoingSamples",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                float[] samples = { 0.7f, -0.5f, 1f, -1f };
                method.Invoke(controller, new object[] { samples });
                Assert.That(samples, Is.All.EqualTo(0f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
