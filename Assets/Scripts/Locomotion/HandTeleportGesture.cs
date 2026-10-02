using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hand-tracking teleport using a "finger gun" gesture, driving a standard XRI teleport ray.
///
///  1. Aim: index finger extended, middle and ring fingers curled. The teleport ray appears
///     along the index finger.
///  2. Select: press the thumb down onto the side of the hand (thumb tip near the index /
///     middle knuckles). This is the ray's select input.
///  3. Teleport: lift the thumb again. Teleport areas fire on select exit, so this is the
///     moment the player moves.
///  Relaxing the hand out of the aim pose while the thumb is still down hides the ray,
///  which XRI treats as a cancelled select, so no teleport happens.
///
/// The gesture is deliberately different from a pinch so it does not fight the hand's
/// Near-Far interactor (pinch to grab / press UI). While aiming, the hand's other
/// interactors are blocked from hovering and selecting.
///
/// Hierarchy (built by the locomotion scene builder):
///   Left Hand / Right Hand            (deactivated by XRInputModalityManager when controllers are in use)
///     Hand Teleport                   (this component)
///       Teleport Interactor           (starter-assets XRRayInteractor, select input = this)
///
/// Joint poses from XR Hands are in tracking space, i.e. relative to the XR Origin's Camera Offset.
/// </summary>
[DefaultExecutionOrder(XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
public class HandTeleportGesture : MonoBehaviour, IXRInputButtonReader
{
    [SerializeField] private Handedness handedness = Handedness.Left;

    [Tooltip("Teleport ray driven by this gesture. Its GameObject is shown only while aiming.")]
    [SerializeField] private XRRayInteractor teleportInteractor;

    [Tooltip("Space the XR Hands joint poses are in (the XR Origin's Camera Offset).")]
    [SerializeField] private Transform trackingSpace;

    [Tooltip("This hand's other interactors (Near-Far, Poke). They are blocked while aiming so a teleport cannot also grab or press UI.")]
    [SerializeField] private XRBaseInteractor[] suppressWhileAiming;

    [Header("Finger Gun Thresholds")]
    [Tooltip("Straightness (knuckle-to-tip distance / finger length, 1 = straight) the index must reach to start aiming.")]
    [Range(0f, 1f)] [SerializeField] private float indexExtendedEnter = 0.9f;
    [Tooltip("Aiming stops once index straightness drops below this.")]
    [Range(0f, 1f)] [SerializeField] private float indexExtendedExit = 0.8f;
    [Tooltip("Middle and ring straightness must be at or below this to start aiming.")]
    [Range(0f, 1f)] [SerializeField] private float otherCurledEnter = 0.7f;
    [Tooltip("Aiming stops once middle or ring straightness rises above this.")]
    [Range(0f, 1f)] [SerializeField] private float otherCurledExit = 0.8f;

    [Header("Thumb Trigger (meters)")]
    [Tooltip("Thumb tip within this distance of the index/middle knuckles counts as pressed.")]
    [SerializeField] private float thumbPressDistance = 0.03f;
    [Tooltip("Thumb tip beyond this distance counts as released.")]
    [SerializeField] private float thumbReleaseDistance = 0.045f;

    [Header("Ray")]
    [Tooltip("Higher is snappier, lower is steadier. Smooths joint jitter on the ray direction.")]
    [SerializeField] private float raySmoothing = 15f;

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    private bool _isAiming;
    private bool _isPerformed;
    private bool _wasPerformedThisFrame;
    private bool _wasCompletedThisFrame;
    private Vector3 _smoothedDirection;

    /// <summary>True while the finger gun aim pose is held and the ray is showing.</summary>
    public bool IsAiming => _isAiming;

    private void Awake()
    {
        if (teleportInteractor == null)
            teleportInteractor = GetComponentInChildren<XRRayInteractor>(true);

        if (teleportInteractor != null)
        {
            // Only select is driven by the gesture; every controller-style input is turned off
            // so the starter prefab's input action references cannot move or rotate the ray.
            teleportInteractor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ObjectReference;
            teleportInteractor.selectInput.SetObjectReference(this);
            teleportInteractor.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            teleportInteractor.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            teleportInteractor.uiScrollInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.translateManipulationInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.rotateManipulationInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.directionalManipulationInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.scaleToggleInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            teleportInteractor.scaleOverTimeInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.scaleDistanceDeltaInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            teleportInteractor.gameObject.SetActive(false);
        }
    }

    private void OnDisable() => SetState(false, false);

    private void Update()
    {
        bool aiming = false;
        bool thumbDown = false;

        var subsystem = GetHandSubsystem();
        if (subsystem != null)
        {
            XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (hand.isTracked)
                EvaluateGesture(hand, out aiming, out thumbDown);
        }

        SetState(aiming, aiming && thumbDown);
    }

    private void EvaluateGesture(XRHand hand, out bool aiming, out bool thumbDown)
    {
        aiming = false;
        thumbDown = false;

        if (!TryGetPosition(hand, XRHandJointID.IndexProximal, out Vector3 indexProximal) ||
            !TryGetPosition(hand, XRHandJointID.IndexTip, out Vector3 indexTip) ||
            !TryGetPosition(hand, XRHandJointID.MiddleProximal, out Vector3 middleProximal) ||
            !TryGetPosition(hand, XRHandJointID.MiddleIntermediate, out Vector3 middleIntermediate) ||
            !TryGetPosition(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip) ||
            !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))
            return;

        float index = Straightness(hand, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip);
        float middle = Straightness(hand, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip);
        float ring = Straightness(hand, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip);

        // Hysteresis: easier to stay in the pose than to enter it, so the ray does not flicker.
        aiming = _isAiming
            ? index >= indexExtendedExit && middle <= otherCurledExit && ring <= otherCurledExit
            : index >= indexExtendedEnter && middle <= otherCurledEnter && ring <= otherCurledEnter;
        if (!aiming)
            return;

        float thumbDistance = Mathf.Min(
            Vector3.Distance(thumbTip, indexProximal),
            Mathf.Min(Vector3.Distance(thumbTip, middleProximal), Vector3.Distance(thumbTip, middleIntermediate)));
        thumbDown = _isPerformed ? thumbDistance < thumbReleaseDistance : thumbDistance <= thumbPressDistance;

        UpdateRayPose(indexProximal, indexTip, wrist.rotation);
    }

    private void UpdateRayPose(Vector3 indexProximal, Vector3 indexTip, Quaternion wristRotation)
    {
        Transform space = trackingSpace != null ? trackingSpace : transform.parent;
        Vector3 origin = space != null ? space.TransformPoint(indexProximal) : indexProximal;
        Vector3 direction = (indexTip - indexProximal).normalized;
        Quaternion spaceRotation = space != null ? space.rotation : Quaternion.identity;
        direction = spaceRotation * direction;
        Vector3 up = spaceRotation * wristRotation * Vector3.up;

        _smoothedDirection = !_isAiming || _smoothedDirection == Vector3.zero
            ? direction
            : Vector3.Slerp(_smoothedDirection, direction, 1f - Mathf.Exp(-raySmoothing * Time.deltaTime)).normalized;

        teleportInteractor.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(_smoothedDirection, up));
    }

    private void SetState(bool aiming, bool performed)
    {
        bool wasPerformed = _isPerformed;
        _wasPerformedThisFrame = !wasPerformed && performed;
        _wasCompletedThisFrame = wasPerformed && !performed;
        _isPerformed = performed;

        if (aiming == _isAiming)
            return;
        _isAiming = aiming;

        // Leaving the aim pose deactivates the ray before select ends this frame,
        // so XRI reports the select as cancelled and the teleport does not fire.
        if (teleportInteractor != null)
            teleportInteractor.gameObject.SetActive(aiming);

        if (suppressWhileAiming == null)
            return;
        foreach (var interactor in suppressWhileAiming)
        {
            if (interactor == null)
                continue;
            interactor.allowHover = !aiming;
            interactor.allowSelect = !aiming;
        }
    }

    /// <summary>Knuckle-to-tip distance divided by the summed bone lengths: about 1 when straight, about 0.5 in a fist.</summary>
    private static float Straightness(XRHand hand, XRHandJointID proximal, XRHandJointID intermediate, XRHandJointID distal, XRHandJointID tip)
    {
        if (!TryGetPosition(hand, proximal, out Vector3 p) ||
            !TryGetPosition(hand, intermediate, out Vector3 i) ||
            !TryGetPosition(hand, distal, out Vector3 d) ||
            !TryGetPosition(hand, tip, out Vector3 t))
            return 0f;

        float length = Vector3.Distance(p, i) + Vector3.Distance(i, d) + Vector3.Distance(d, t);
        return length > 0f ? Vector3.Distance(p, t) / length : 0f;
    }

    private static bool TryGetPosition(XRHand hand, XRHandJointID id, out Vector3 position)
    {
        bool ok = hand.GetJoint(id).TryGetPose(out Pose pose);
        position = pose.position;
        return ok;
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

    public bool ReadIsPerformed() => _isPerformed;
    public bool ReadWasPerformedThisFrame() => _wasPerformedThisFrame;
    public bool ReadWasCompletedThisFrame() => _wasCompletedThisFrame;
    public float ReadValue() => _isPerformed ? 1f : 0f;

    public bool TryReadValue(out float value)
    {
        value = ReadValue();
        return _isAiming;
    }
}
