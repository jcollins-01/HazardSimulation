using Normal.Realtime;
using UnityEngine;
using UnityEngine.XR;

namespace Jingchen.Mic
{
    [DisallowMultipleComponent]
    internal sealed class LapelMicController : MonoBehaviour
    {
        private const float InteractionRadius = 0.20f;

        private RealtimeAvatar avatar;
        private RealtimeAvatarVoice voice;
        private LapelMicVisual visual;
        private bool initialized;
        private bool applicationFocused = true;

        internal void Initialize(
            RealtimeAvatar realtimeAvatar,
            RealtimeAvatarVoice avatarVoice,
            LapelMicVisual lapelMicVisual)
        {
            if (realtimeAvatar == null || !realtimeAvatar.isLocalAvatar)
                return;

            avatar = realtimeAvatar;
            voice = avatarVoice;
            visual = lapelMicVisual;
            initialized = voice != null;
            applicationFocused = Application.isFocused;

            MuteImmediately();
        }

        private void Update()
        {
            if (!initialized || avatar == null || voice == null)
            {
                MuteImmediately();
                return;
            }

            Vector3 interactionPosition = visual != null
                ? visual.InteractionPosition
                : GetFallbackInteractionPosition();

            bool leftHandNear = IsHandNear(avatar.leftHand, interactionPosition);
            bool rightHandNear = IsHandNear(avatar.rightHand, interactionPosition);
            bool handNear = leftHandNear || rightHandNear;

            bool leftPushToTalkHeld = leftHandNear && IsPrimaryButtonPressed(XRNode.LeftHand);
            bool rightPushToTalkHeld = rightHandNear && IsPrimaryButtonPressed(XRNode.RightHand);
            bool transmitting = applicationFocused && (leftPushToTalkHeld || rightPushToTalkHeld);

            SetMuted(!transmitting);

            if (visual != null)
                visual.SetLocalState(handNear, transmitting);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            applicationFocused = hasFocus;

            if (!hasFocus)
                MuteImmediately();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                MuteImmediately();
        }

        private void OnDisable()
        {
            MuteImmediately();
        }

        private void OnDestroy()
        {
            MuteImmediately();
        }

        private bool IsHandNear(Transform hand, Vector3 interactionPosition)
        {
            return hand != null &&
                   hand.gameObject.activeInHierarchy &&
                   Vector3.SqrMagnitude(hand.position - interactionPosition) <=
                   InteractionRadius * InteractionRadius;
        }

        private static bool IsPrimaryButtonPressed(XRNode handNode)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(handNode);
            return device.isValid &&
                   device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed) &&
                   pressed;
        }

        private Vector3 GetFallbackInteractionPosition()
        {
            Transform head = avatar != null ? avatar.head : null;
            Transform avatarTransform = avatar != null ? avatar.transform : transform;
            Vector3 origin = head != null
                ? head.position - avatarTransform.up * 0.30f
                : avatarTransform.position + avatarTransform.up * 1.35f;

            return origin - avatarTransform.right * 0.10f + avatarTransform.forward * 0.08f;
        }

        private void MuteImmediately()
        {
            SetMuted(true);

            if (visual != null)
                visual.SetLocalState(false, false);
        }

        private void SetMuted(bool muted)
        {
            if (voice != null && voice.mute != muted)
                voice.mute = muted;
        }
    }
}
