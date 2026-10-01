using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

/// <summary>
/// Keeps a Normcore avatar hand target ("Left/Right Hand VR Target") following the
/// player's real hand in both input modes.
///
/// The targets used to be children of the controller objects. XRInputModalityManager
/// deactivates those when hand tracking takes over, which froze the targets in place
/// and left the networked avatar's hands hanging in the air. This component moves the
/// target up to the XR Origin's Camera Offset and drives it each frame from:
///  - the controller transform (with the original local offset) while controllers are in use, or
///  - the tracked wrist joint while hand tracking is active.
///
/// VRIK copies this target's rotation straight onto the avatar's hand bone, so in hand
/// tracking mode the target is placed on the real wrist and rotated by
/// <see cref="wristToAvatarHandEuler"/>, which maps the XR Hands wrist axes (+Z toward the
/// fingertips, +Y out of the back of the hand) onto the avatar's Biped hand bone axes.
/// The controller offset is deliberately not used here: it contains a wrist bend that only
/// makes sense while holding a controller.
///
/// Runs just before RealtimeAvatar (-90), which copies these targets into the avatar.
/// </summary>
[DefaultExecutionOrder(-95)]
public class HandTrackingAvatarTarget : MonoBehaviour
{
    public enum PoseSource { Controller, Wrist, HandGrip }

    [SerializeField] private Handedness handedness = Handedness.Left;

    [Tooltip("Controller object this target was originally parented to.")]
    [SerializeField] private Transform controller;

    [Tooltip("Parent the target is moved under (the XR Origin's Camera Offset). Tracked poses are in this space.")]
    [SerializeField] private Transform trackingSpace;

    [Header("Hand Tracking Alignment")]
    [Tooltip("Rotation from the tracked wrist joint to the avatar's hand bone, measured from the avatar skeleton.")]
    [SerializeField] private Vector3 wristToAvatarHandEuler;
    [Tooltip("Small positional nudge in wrist space (+Z toward fingers, +Y back of hand) if the avatar's wrist sits off the real one.")]
    [SerializeField] private Vector3 wristToAvatarHandPosition;

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    private Vector3 _controllerToTargetPosition;
    private Quaternion _controllerToTargetRotation = Quaternion.identity;

    private InputAction _gripPosition;
    private InputAction _gripRotation;
    private InputAction _gripTracked;

    /// <summary>Where this frame's pose came from.</summary>
    public PoseSource CurrentSource { get; private set; }

    /// <summary>True while this frame's pose came from hand tracking rather than the controller.</summary>
    public bool IsUsingHandTracking => CurrentSource != PoseSource.Controller;

    private void Awake()
    {
        if (controller == null)
            controller = transform.parent;

        if (controller != null)
        {
            _controllerToTargetPosition = controller.InverseTransformPoint(transform.position);
            _controllerToTargetRotation = Quaternion.Inverse(controller.rotation) * transform.rotation;
        }

        if (trackingSpace != null && transform.parent != trackingSpace)
            transform.SetParent(trackingSpace, true);

        // Fallback for runtimes without XR Hands joint data: the OpenXR Hand Interaction
        // profile's grip pose, treated like a controller grip.
        string device = "<HandInteraction>{" + (handedness == Handedness.Left ? "LeftHand" : "RightHand") + "}";
        _gripPosition = new InputAction("HandGripPosition", InputActionType.Value, device + "/devicePosition");
        _gripRotation = new InputAction("HandGripRotation", InputActionType.Value, device + "/deviceRotation");
        _gripTracked = new InputAction("HandGripTracked", InputActionType.Button, device + "/isTracked");
    }

    private void OnEnable()
    {
        _gripPosition.Enable();
        _gripRotation.Enable();
        _gripTracked.Enable();
    }

    private void OnDisable()
    {
        _gripPosition.Disable();
        _gripRotation.Disable();
        _gripTracked.Disable();
    }

    private void OnDestroy()
    {
        _gripPosition.Dispose();
        _gripRotation.Dispose();
        _gripTracked.Dispose();
    }

    private void Update() => UpdatePose();
    private void LateUpdate() => UpdatePose();

    private void UpdatePose()
    {
        // Prefer the controller whenever it is active so behaviour is unchanged for controller users.
        if (controller != null && controller.gameObject.activeInHierarchy)
        {
            CurrentSource = PoseSource.Controller;
            ApplyControllerStyle(controller.position, controller.rotation);
            return;
        }

        Transform space = trackingSpace != null ? trackingSpace : transform.parent;

        var subsystem = GetHandSubsystem();
        if (subsystem != null)
        {
            XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
            if (hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))
            {
                CurrentSource = PoseSource.Wrist;
                ToWorld(space, wrist.position, wrist.rotation, out Vector3 wristPos, out Quaternion wristRot);
                transform.SetPositionAndRotation(
                    wristPos + wristRot * wristToAvatarHandPosition,
                    wristRot * Quaternion.Euler(wristToAvatarHandEuler));
                return;
            }
        }

        if (_gripTracked.IsPressed() && _gripRotation.activeControl != null)
        {
            CurrentSource = PoseSource.HandGrip;
            ToWorld(space, _gripPosition.ReadValue<Vector3>(), _gripRotation.ReadValue<Quaternion>(), out Vector3 gripPos, out Quaternion gripRot);
            ApplyControllerStyle(gripPos, gripRot);
            return;
        }

        CurrentSource = PoseSource.Controller;
    }

    private void ApplyControllerStyle(Vector3 anchorPosition, Quaternion anchorRotation)
    {
        transform.SetPositionAndRotation(
            anchorPosition + anchorRotation * _controllerToTargetPosition,
            anchorRotation * _controllerToTargetRotation);
    }

    private static void ToWorld(Transform space, Vector3 localPosition, Quaternion localRotation, out Vector3 position, out Quaternion rotation)
    {
        position = space != null ? space.TransformPoint(localPosition) : localPosition;
        rotation = space != null ? space.rotation * localRotation : localRotation;
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
