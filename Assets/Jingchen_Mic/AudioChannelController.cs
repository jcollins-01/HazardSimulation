using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;
using UnityEngine.XR;

namespace Jingchen.Mic
{
    // RealtimeAvatarVoice sends its samples at -1. Gate every sample frame first.
    [DefaultExecutionOrder(-2)]
    [DisallowMultipleComponent]
    public sealed class AudioChannelController : MonoBehaviour
    {
        private readonly List<RealtimeAvatarManager> managers = new List<RealtimeAvatarManager>();
        private readonly Dictionary<RealtimeAvatar, RealtimeAvatarVoice[]> voices =
            new Dictionary<RealtimeAvatar, RealtimeAvatarVoice[]>();
        private readonly HashSet<RealtimeAvatarVoice> guardedVoices = new HashSet<RealtimeAvatarVoice>();
        private Realtime realtime;
        private AudioChannelNetwork network;
        private AudioChannelHud hud;
        private readonly AudioChannelInputGate input = new AudioChannelInputGate();
        private bool focused;
        private bool paused;
        private bool localTalkHeld;

        private void Awake()
        {
            realtime = GetComponent<Realtime>();
            focused = Application.isFocused;
        }

        internal void Register(RealtimeAvatarManager manager)
        {
            // Inactive scene objects can be discovered before this component's
            // Awake. Also recover if the separate network object was unloaded.
            if (realtime == null) realtime = GetComponent<Realtime>();
            if (network == null && realtime != null)
                network = AudioChannelNetwork.Create(realtime);
            if (manager == null || managers.Contains(manager)) return;
            managers.Add(manager);
            manager.avatarCreated += AvatarCreated;
            manager.avatarDestroyed += AvatarDestroyed;
            if (manager.avatars != null)
                foreach (var avatar in manager.avatars.Values) BindVoice(avatar);
            BindVoice(manager.localAvatar);
        }

        private void AvatarCreated(RealtimeAvatarManager manager, RealtimeAvatar avatar, bool local)
        {
            BindVoice(avatar);
        }

        private void BindVoice(RealtimeAvatar avatar)
        {
            if (avatar == null || voices.ContainsKey(avatar)) return;
            var components = avatar.GetComponentsInChildren<RealtimeAvatarVoice>(true);
            voices.Add(avatar, components);
            foreach (var voice in components)
            {
                voice.mute = true;
                if (avatar.isLocalAvatar)
                {
                    if (guardedVoices.Add(voice)) voice.voiceData += GuardOutgoingSamples;
                }
                else
                {
                    // Radio reception is room-wide, independent of avatar distance.
                    // Normcore reuses this source when it creates the audio stream.
                    var source = voice.GetComponent<AudioSource>();
                    if (source == null) source = voice.gameObject.AddComponent<AudioSource>();
                    source.spatialize = false;
                    source.spatialBlend = 0f;
                    source.dopplerLevel = 0f;
                }
            }
        }

        private void AvatarDestroyed(RealtimeAvatarManager manager, RealtimeAvatar avatar, bool local)
        {
            if (avatar != null && voices.TryGetValue(avatar, out var components))
            {
                foreach (var voice in components)
                {
                    if (voice == null) continue;
                    voice.mute = true;
                    voice.voiceData -= GuardOutgoingSamples;
                    guardedVoices.Remove(voice);
                }
                voices.Remove(avatar);
            }
            if (local) StopTalking();
        }

        private void Update()
        {
            bool localAvatarAvailable = false;
            Transform head = null;
            foreach (var manager in managers)
            {
                if (manager == null || !manager.isActiveAndEnabled || manager.localAvatar == null)
                    continue;
                localAvatarAvailable = true;
                head = manager.localAvatar.head;
                break;
            }

            bool rightValid = ReadPrimary(XRNode.RightHand, out bool talkPressed);
            bool leftValid = ReadPrimary(XRNode.LeftHand, out bool channelPressed);
            bool usable = focused && !paused && localAvatarAvailable &&
                realtime != null && realtime.connected && network != null && network.Ready;
            input.Sample(usable, rightValid, talkPressed, leftValid, channelPressed,
                out bool held, out bool cycle);
            localTalkHeld = held;

            if (network != null)
            {
                // Changing channel cancels the current request and requires A release.
                if (cycle) network.CycleChannel();
                network.SetTalkIntent(held);
            }

            int activeSpeaker = usable ? network.ActiveSpeakerClientId : -1;
            foreach (var manager in managers)
            {
                if (manager == null || manager.avatars == null) continue;
                foreach (var pair in manager.avatars)
                {
                    if (pair.Value == null) continue;
                    BindVoice(pair.Value);
                    bool allowed = pair.Value.isLocalAvatar
                        ? usable && held && network.HasFloor
                        : usable && activeSpeaker >= 0 && pair.Key == activeSpeaker;
                    foreach (var voice in voices[pair.Value])
                        if (voice != null) voice.mute = !allowed;
                }
            }

            if (head != null && hud == null)
                hud = AudioChannelHud.Create(head);
            if (hud != null)
                hud.Show(network, held, focused && !paused);
        }

        private static bool ReadPrimary(XRNode node, out bool pressed)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            pressed = false;
            return device.isValid &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!value) StopTalking();
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (value) StopTalking();
        }

        private void StopTalking()
        {
            localTalkHeld = false;
            input.Reset();
            if (network != null) network.SetTalkIntent(false);
            foreach (var components in voices.Values)
                foreach (var voice in components) if (voice != null) voice.mute = true;
        }

        private void OnDisable() { StopTalking(); }

        private void GuardOutgoingSamples(float[] samples)
        {
            // Normcore invokes voiceData immediately before sending this buffer.
            // Recheck the monotonic lease even if the main thread stalled between
            // our Update and RealtimeAvatarVoice.Update. Invalid samples are silence.
            if (samples != null && (!isActiveAndEnabled || !focused || paused ||
                !localTalkHeld || network == null || !network.HasFloor))
                System.Array.Clear(samples, 0, samples.Length);
        }

        private void OnDestroy()
        {
            StopTalking();
            foreach (var voice in guardedVoices)
                if (voice != null) voice.voiceData -= GuardOutgoingSamples;
            guardedVoices.Clear();
            foreach (var manager in managers)
            {
                if (manager == null) continue;
                manager.avatarCreated -= AvatarCreated;
                manager.avatarDestroyed -= AvatarDestroyed;
            }
            if (hud != null) Destroy(hud.gameObject);
        }
    }

    // No network or device dependencies: loss of focus/tracking must never re-open
    // a microphone until the user has physically released and pressed A again.
    public sealed class AudioChannelInputGate
    {
        private bool talkArmed;
        private bool channelArmed;

        public void Reset() { talkArmed = false; channelArmed = false; }

        public void Sample(bool usable, bool rightValid, bool talkPressed,
            bool leftValid, bool channelPressed, out bool held, out bool cycle)
        {
            held = false;
            cycle = false;
            if (!usable) { Reset(); return; }
            if (!rightValid) talkArmed = false;
            else if (!talkPressed) talkArmed = true;
            if (!leftValid) channelArmed = false;
            else if (!channelPressed) channelArmed = true;
            else if (channelArmed)
            {
                cycle = true;
                channelArmed = false;
                talkArmed = false;
            }
            held = rightValid && talkArmed && talkPressed && !cycle;
        }
    }
}
