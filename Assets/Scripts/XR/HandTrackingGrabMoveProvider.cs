using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

/// <summary>
/// Hand-tracking equivalent of the XR Interaction Toolkit Grab Move Provider.
/// A pinch anchors the selected hand in the world while the XR Origin moves
/// opposite the hand motion. When both hands pinch, their midpoint drives the
/// translation.
/// </summary>
[AddComponentMenu("XR/Locomotion/Hand Tracking Grab Move Provider", 11)]
public sealed class HandTrackingGrabMoveProvider : ConstrainedMoveProvider
{
    [SerializeField]
    [Tooltip("The ratio of actual movement distance to hand movement distance.")]
    [Min(0f)]
    float m_MoveFactor = 4.5f;

    [SerializeField]
    [Tooltip("Thumb-to-index distance required to begin a pinch, in meters.")]
    [Min(0.005f)]
    float m_PinchStartDistance = 0.035f;

    [SerializeField]
    [Tooltip("Thumb-to-index distance at which a pinch is released, in meters.")]
    [Min(0.005f)]
    float m_PinchReleaseDistance = 0.05f;

    [SerializeField]
    [Tooltip("When enabled, both pinching hands drive movement from their midpoint.")]
    bool m_UseBothHands = true;

    [SerializeField]
    [Tooltip("Allows this provider to be disabled without removing the hand-tracking setup.")]
    bool m_CanMove = true;

    [SerializeField]
    [Tooltip("Tracking movement smaller than this distance is ignored to prevent hand jitter from moving the player.")]
    [Min(0f)]
    float m_HandMovementDeadzone = 0.005f;

    [SerializeField]
    [Tooltip("Smooths the tracked hand position over this many seconds. Set to zero to disable smoothing.")]
    [Min(0f)]
    float m_HandPositionSmoothing = 0.02f;

    [SerializeField]
    [Tooltip("Maximum player movement that can be applied in one frame. Set to zero for no limit.")]
    [Min(0f)]
    float m_MaxMovementPerFrame = 0.4f;

    [SerializeField]
    [Tooltip("Ignore movement that would move the player backward relative to the headset. Disable if intentional backward locomotion is needed.")]
    bool m_PreventBackwardMovement = false;

    readonly List<XRHandSubsystem> m_HandSubsystems = new List<XRHandSubsystem>();
    readonly List<XRBaseInteractor> m_HandInteractors = new List<XRBaseInteractor>();

    XRHandSubsystem m_HandSubsystem;
    Transform m_InteractorSearchRoot;
    bool m_LeftPinching;
    bool m_RightPinching;
    bool m_HasPreviousPosition;
    int m_PreviousActiveHandMask;
    Vector3 m_PreviousHandPosition;

    /// <summary>
    /// The ratio of actual movement distance to hand movement distance.
    /// </summary>
    public float moveFactor
    {
        get => m_MoveFactor;
        set => m_MoveFactor = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Whether this provider is allowed to move the XR Origin.
    /// </summary>
    public bool canMove
    {
        get => m_CanMove;
        set => m_CanMove = value;
    }

    void OnValidate()
    {
        m_MoveFactor = Mathf.Max(0f, m_MoveFactor);
        m_PinchStartDistance = Mathf.Max(0.005f, m_PinchStartDistance);
        m_PinchReleaseDistance = Mathf.Max(m_PinchStartDistance, m_PinchReleaseDistance);
        m_HandMovementDeadzone = Mathf.Max(0f, m_HandMovementDeadzone);
        m_HandPositionSmoothing = Mathf.Max(0f, m_HandPositionSmoothing);
        m_MaxMovementPerFrame = Mathf.Max(0f, m_MaxMovementPerFrame);
    }

    protected override Vector3 ComputeDesiredMove(out bool attemptingMove)
    {
        attemptingMove = false;

        var xrOriginComponent = mediator != null ? mediator.xrOrigin : null;
        var xrOrigin = xrOriginComponent?.Origin;
        if (!m_CanMove || xrOrigin == null || !TryGetRunningHandSubsystem(out var subsystem))
        {
            ResetMovementState();
            return Vector3.zero;
        }

        RefreshHandInteractors(xrOrigin.transform);

        var leftTracked = TryGetAvailablePinchingHandPose(
            subsystem.leftHand,
            InteractorHandedness.Left,
            ref m_LeftPinching,
            out var leftPosition);
        var rightTracked = TryGetAvailablePinchingHandPose(
            subsystem.rightHand,
            InteractorHandedness.Right,
            ref m_RightPinching,
            out var rightPosition);

        var activeHandMask = 0;
        Vector3 currentHandPosition;

        if (m_UseBothHands && leftTracked && rightTracked)
        {
            activeHandMask = 3;
            currentHandPosition = (leftPosition + rightPosition) * 0.5f;
        }
        else if (leftTracked)
        {
            activeHandMask = 1;
            currentHandPosition = leftPosition;
        }
        else if (rightTracked)
        {
            activeHandMask = 2;
            currentHandPosition = rightPosition;
        }
        else
        {
            ResetMovementState();
            return Vector3.zero;
        }

        // Re-anchor when a pinch begins or when the active hand combination changes.
        // This prevents a jump if one hand is lost while both hands are pinching.
        if (!m_HasPreviousPosition || activeHandMask != m_PreviousActiveHandMask)
        {
            m_HasPreviousPosition = true;
            m_PreviousActiveHandMask = activeHandMask;
            m_PreviousHandPosition = currentHandPosition;
            return Vector3.zero;
        }

        // Filter the hand pose before taking a delta. This keeps small tracking noise from
        // becoming locomotion while still allowing a deliberate pull to accumulate smoothly.
        var previousHandPosition = m_PreviousHandPosition;
        if (m_HandPositionSmoothing > 0f)
        {
            var smoothing = 1f - Mathf.Exp(-Time.deltaTime / m_HandPositionSmoothing);
            currentHandPosition = Vector3.Lerp(previousHandPosition, currentHandPosition, smoothing);
        }

        var handDelta = currentHandPosition - previousHandPosition;
        m_PreviousHandPosition = currentHandPosition;

        var handDistance = handDelta.magnitude;
        if (handDistance <= m_HandMovementDeadzone)
            return Vector3.zero;

        var usableHandDelta = handDelta * ((handDistance - m_HandMovementDeadzone) / handDistance);
        var move = xrOrigin.transform.TransformVector(-usableHandDelta) * m_MoveFactor;

        if (m_PreventBackwardMovement)
        {
            var headsetForward = xrOriginComponent.Camera != null ? xrOriginComponent.Camera.transform.forward : xrOrigin.transform.forward;
            headsetForward = Vector3.ProjectOnPlane(headsetForward, Vector3.up);
            if (headsetForward.sqrMagnitude > Mathf.Epsilon)
            {
                headsetForward.Normalize();
                var horizontalMove = Vector3.ProjectOnPlane(move, Vector3.up);
                var forwardAmount = Vector3.Dot(horizontalMove, headsetForward);
                if (forwardAmount < 0f)
                    move -= headsetForward * forwardAmount;
            }
        }

        if (m_MaxMovementPerFrame > 0f)
            move = Vector3.ClampMagnitude(move, m_MaxMovementPerFrame);

        attemptingMove = move.sqrMagnitude > Mathf.Epsilon;
        return move;
    }

    bool TryGetRunningHandSubsystem(out XRHandSubsystem subsystem)
    {
        if (m_HandSubsystem != null && m_HandSubsystem.running)
        {
            subsystem = m_HandSubsystem;
            return true;
        }

        m_HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(m_HandSubsystems);
        for (var index = 0; index < m_HandSubsystems.Count; ++index)
        {
            if (!m_HandSubsystems[index].running)
                continue;

            m_HandSubsystem = m_HandSubsystems[index];
            subsystem = m_HandSubsystem;
            return true;
        }

        m_HandSubsystem = null;
        subsystem = null;
        return false;
    }

    void RefreshHandInteractors(Transform xrOrigin)
    {
        if (xrOrigin == m_InteractorSearchRoot && m_HandInteractors.Count > 0)
            return;

        m_InteractorSearchRoot = xrOrigin;
        m_HandInteractors.Clear();
        m_HandInteractors.AddRange(xrOrigin.GetComponentsInChildren<XRBaseInteractor>(true));
    }

    bool TryGetAvailablePinchingHandPose(
        XRHand hand,
        InteractorHandedness handedness,
        ref bool wasPinching,
        out Vector3 handPosition)
    {
        if (HandHasSelection(handedness))
        {
            // A pinch used to hold an object must not also act as a locomotion grab.
            // Clear the latch so releasing the object requires a fresh pinch edge.
            wasPinching = false;
            handPosition = default;
            return false;
        }

        return TryGetPinchingHandPose(hand, ref wasPinching, out handPosition);
    }

    bool HandHasSelection(InteractorHandedness handedness)
    {
        for (var index = 0; index < m_HandInteractors.Count; ++index)
        {
            var interactor = m_HandInteractors[index];
            if (interactor.handedness == handedness && interactor.hasSelection)
                return true;
        }

        return false;
    }

    bool TryGetPinchingHandPose(XRHand hand, ref bool wasPinching, out Vector3 handPosition)
    {
        handPosition = default;
        if (!hand.isTracked)
        {
            wasPinching = false;
            return false;
        }

        var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
        var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        if (!thumbTip.TryGetPose(out var thumbPose) || !indexTip.TryGetPose(out var indexPose))
        {
            wasPinching = false;
            return false;
        }

        var pinchDistance = Vector3.Distance(thumbPose.position, indexPose.position);
        var threshold = wasPinching ? m_PinchReleaseDistance : m_PinchStartDistance;
        wasPinching = pinchDistance <= threshold;
        if (!wasPinching)
            return false;

        // Palm movement is steadier than the two fingertips after a pinch begins.
        if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmPose))
        {
            handPosition = palmPose.position;
            return true;
        }

        if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wristPose))
        {
            handPosition = wristPose.position;
            return true;
        }

        handPosition = (thumbPose.position + indexPose.position) * 0.5f;
        return true;
    }

    void ResetMovementState()
    {
        m_LeftPinching = false;
        m_RightPinching = false;
        m_HasPreviousPosition = false;
        m_PreviousActiveHandMask = 0;
        m_PreviousHandPosition = default;
    }
}
