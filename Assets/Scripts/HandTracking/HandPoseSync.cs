using System;
using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// Replicates per-finger curl from XR hand tracking and poses the avatar's finger bones.
///
/// Data flow:
///  - On the client that owns this avatar, finger curl is measured each frame from the
///    XRHandSubsystem joint poses, applied straight to the local avatar (no network lag),
///    and written to the model packed as two ints (see HandPoseModel).
///  - On every other client the packed values are unpacked and smoothed onto the same
///    finger bones.
///  - When a hand is not tracked (player is using controllers) the fingers relax back
///    to the avatar's rest pose, which is exactly how the avatar looked before.
///
/// Fingers are posed in LateUpdate after VRIK, which only drives the arms and body and
/// never touches the finger bones.
/// </summary>
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(RealtimeView))]
public class HandPoseSync : RealtimeComponent<HandPoseModel>
{
    private const int FingerCount = 5;
    private const int CurlBits = 6;
    private const int CurlMax = (1 << CurlBits) - 1;
    private const int TrackedBit = 1 << (FingerCount * CurlBits);

    [Serializable]
    public class FingerBones
    {
        [Tooltip("Base, middle and tip bone of this finger, in order.")]
        public Transform[] segments = new Transform[3];

        [Tooltip("Bend angle per segment (degrees) at full curl.")]
        public Vector3 maxBendAngles = new Vector3(70f, 95f, 70f);

        [Tooltip("Local axis each segment rotates around when curling. Flip its sign if a finger bends backwards.")]
        public Vector3 bendAxis = Vector3.forward;

        [NonSerialized] public Quaternion[] restRotations;
    }

    [Serializable]
    public class HandBones
    {
        [Tooltip("Thumb, index, middle, ring, little.")]
        public FingerBones[] fingers = new FingerBones[FingerCount];
    }

    [Header("Avatar Finger Bones")]
    [SerializeField] private HandBones leftHand = new HandBones();
    [SerializeField] private HandBones rightHand = new HandBones();

    [Header("Curl Measurement (local player)")]
    [Tooltip("Summed joint bend (degrees) of a relaxed, open finger. Anything below reads as fully open.")]
    [SerializeField] private float fingerOpenAngle = 20f;
    [Tooltip("Summed joint bend (degrees) of a finger in a closed fist.")]
    [SerializeField] private float fingerFistAngle = 230f;
    [SerializeField] private float thumbOpenAngle = 15f;
    [SerializeField] private float thumbFistAngle = 95f;

    [Header("Remote Smoothing")]
    [Tooltip("How quickly remote fingers follow received values. Higher is snappier.")]
    [SerializeField] private float remoteSmoothing = 20f;

    private static readonly XRHandJointID[][] FingerJoints =
    {
        new[] { XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
        new[] { XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
        new[] { XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
        new[] { XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
        new[] { XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
    };

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    private readonly float[] _leftCurls = new float[FingerCount];
    private readonly float[] _rightCurls = new float[FingerCount];
    private readonly float[] _leftTarget = new float[FingerCount];
    private readonly float[] _rightTarget = new float[FingerCount];
    private readonly Vector3[] _jointPositions = new Vector3[5];

    /// <summary>Editor/debug override: when >= 0, every finger on both hands is posed at this curl.</summary>
    [NonSerialized] public float debugCurlOverride = -1f;

    private bool IsLocalPlayer => realtimeView != null && realtimeView.isOwnedLocallyInHierarchy;

    private void Awake()
    {
        CacheRestPose(leftHand);
        CacheRestPose(rightHand);
    }

    private void LateUpdate()
    {
        if (debugCurlOverride >= 0f)
        {
            for (int i = 0; i < FingerCount; i++)
                _leftCurls[i] = _rightCurls[i] = debugCurlOverride;
        }
        else if (IsLocalPlayer)
        {
            var subsystem = GetHandSubsystem();
            bool leftTracked = MeasureCurls(subsystem != null ? subsystem.leftHand : default, _leftCurls);
            bool rightTracked = MeasureCurls(subsystem != null ? subsystem.rightHand : default, _rightCurls);
            WriteModel(leftTracked, rightTracked);
        }
        else
        {
            float t = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            for (int i = 0; i < FingerCount; i++)
            {
                _leftCurls[i] = Mathf.Lerp(_leftCurls[i], _leftTarget[i], t);
                _rightCurls[i] = Mathf.Lerp(_rightCurls[i], _rightTarget[i], t);
            }
        }

        ApplyCurls(leftHand, _leftCurls);
        ApplyCurls(rightHand, _rightCurls);
    }

    protected override void OnRealtimeModelReplaced(HandPoseModel previousModel, HandPoseModel currentModel)
    {
        if (previousModel != null)
        {
            previousModel.leftHandDidChange -= OnLeftHandDidChange;
            previousModel.rightHandDidChange -= OnRightHandDidChange;
        }

        if (currentModel != null)
        {
            if (currentModel.isFreshModel)
            {
                currentModel.leftHand = 0;
                currentModel.rightHand = 0;
            }

            Unpack(currentModel.leftHand, _leftTarget);
            Unpack(currentModel.rightHand, _rightTarget);

            currentModel.leftHandDidChange += OnLeftHandDidChange;
            currentModel.rightHandDidChange += OnRightHandDidChange;
        }
    }

    private void OnLeftHandDidChange(HandPoseModel changedModel, int value) => Unpack(value, _leftTarget);
    private void OnRightHandDidChange(HandPoseModel changedModel, int value) => Unpack(value, _rightTarget);

    private void WriteModel(bool leftTracked, bool rightTracked)
    {
        if (model == null)
            return;

        // Only assign when the quantized value changes so idle hands send nothing.
        int left = Pack(_leftCurls, leftTracked);
        int right = Pack(_rightCurls, rightTracked);
        if (model.leftHand != left) model.leftHand = left;
        if (model.rightHand != right) model.rightHand = right;
    }

    private static int Pack(float[] curls, bool tracked)
    {
        int packed = tracked ? TrackedBit : 0;
        for (int i = 0; i < FingerCount; i++)
            packed |= Mathf.RoundToInt(Mathf.Clamp01(curls[i]) * CurlMax) << (i * CurlBits);
        return packed;
    }

    private static void Unpack(int packed, float[] curls)
    {
        // An untracked hand always relaxes to the rest pose.
        bool tracked = (packed & TrackedBit) != 0;
        for (int i = 0; i < FingerCount; i++)
            curls[i] = tracked ? ((packed >> (i * CurlBits)) & CurlMax) / (float)CurlMax : 0f;
    }

    private bool MeasureCurls(XRHand hand, float[] curls)
    {
        if (!hand.isTracked)
        {
            Array.Clear(curls, 0, FingerCount);
            return false;
        }

        for (int f = 0; f < FingerCount; f++)
        {
            var joints = FingerJoints[f];
            bool valid = true;
            for (int j = 0; j < joints.Length && valid; j++)
            {
                valid = hand.GetJoint(joints[j]).TryGetPose(out Pose pose);
                _jointPositions[j] = pose.position;
            }

            if (!valid)
                continue; // keep last value for a briefly occluded finger

            // Sum the bend between consecutive bone directions along the finger.
            float bend = 0f;
            for (int j = 1; j < joints.Length - 1; j++)
            {
                Vector3 a = _jointPositions[j] - _jointPositions[j - 1];
                Vector3 b = _jointPositions[j + 1] - _jointPositions[j];
                bend += Vector3.Angle(a, b);
            }

            bool thumb = f == 0;
            float open = thumb ? thumbOpenAngle : fingerOpenAngle;
            float fist = thumb ? thumbFistAngle : fingerFistAngle;
            curls[f] = Mathf.Clamp01((bend - open) / Mathf.Max(1f, fist - open));
        }

        return true;
    }

    private static void ApplyCurls(HandBones hand, float[] curls)
    {
        for (int f = 0; f < FingerCount && f < hand.fingers.Length; f++)
        {
            var finger = hand.fingers[f];
            if (finger?.restRotations == null)
                continue;

            for (int s = 0; s < finger.segments.Length; s++)
            {
                var bone = finger.segments[s];
                if (bone == null)
                    continue;

                float angle = curls[f] * finger.maxBendAngles[Mathf.Min(s, 2)];
                bone.localRotation = finger.restRotations[s] * Quaternion.AngleAxis(angle, finger.bendAxis);
            }
        }
    }

    private static void CacheRestPose(HandBones hand)
    {
        foreach (var finger in hand.fingers)
        {
            if (finger?.segments == null)
                continue;

            finger.restRotations = new Quaternion[finger.segments.Length];
            for (int s = 0; s < finger.segments.Length; s++)
                finger.restRotations[s] = finger.segments[s] != null ? finger.segments[s].localRotation : Quaternion.identity;
        }
    }

    private static XRHandSubsystem GetHandSubsystem()
    {
        SubsystemManager.GetSubsystems(s_Subsystems);
        foreach (var subsystem in s_Subsystems)
        {
            if (subsystem.running)
                return subsystem;
        }

        return null;
    }
}
