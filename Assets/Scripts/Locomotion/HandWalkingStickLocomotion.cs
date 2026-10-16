using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Hand-tracking "walking stick" locomotion, driving the rig's standard XRI move and smooth turn providers.
///
///  Walk: make a fist with either hand, as if gripping a walking stick, and swing it forward and back
///  from the shoulder. The faster the swing, the faster the player walks (up to the move provider's
///  Move Speed), in the direction the head faces. One swinging fist is enough, so the player can walk
///  while the other hand holds the extinguisher. A hand that is holding something never walks.
///
///  Turn: from the fist, lift the thumb into a thumbs-up and tilt the hand so the thumb points left or
///  right. The further the tilt, the faster the turn (up to the turn provider's Turn Speed). Either hand
///  can turn, and a fist can walk and turn at the same time.
///
/// Swing speed is the fist's velocity relative to the head, so physically walking or leaning does not
/// count. Only forward/back and up/down motion counts; sideways motion is reaching across the body.
/// Only the thumb's sideways tilt counts, so the forward/back pitch of a swinging fist does not turn.
///
/// The providers' left-hand input is set to Manual Value and written every frame; their right-hand
/// (controller thumbstick) input is turned off, so controllers cannot move or turn in this mode.
///
/// Hierarchy (built by the locomotion scene builder):
///   XR Origin / Locomotion
///     Walking Stick                   (this component)
///
/// Joint poses from XR Hands are in tracking space, i.e. relative to the XR Origin's Camera Offset.
/// </summary>
[DefaultExecutionOrder(XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
public class HandWalkingStickLocomotion : MonoBehaviour
{
    [Tooltip("Move provider driven by the swinging fists. Its Move Speed is the top walking speed.")]
    [SerializeField] private ContinuousMoveProvider moveProvider;

    [Tooltip("Smooth turn provider driven by the thumb tilt. Its Turn Speed is the top turning speed.")]
    [SerializeField] private ContinuousTurnProvider turnProvider;

    [Tooltip("The XR Origin's camera. Swings are measured relative to it, and it sets the walking direction.")]
    [SerializeField] private Transform head;

    [Tooltip("Space the XR Hands joint poses are in (the XR Origin's Camera Offset).")]
    [SerializeField] private Transform trackingSpace;

    [Tooltip("Each hand's Near-Far interactor. A hand that is holding something does not walk.")]
    [SerializeField] private XRBaseInteractor leftHandInteractor;
    [SerializeField] private XRBaseInteractor rightHandInteractor;

    [Header("Fist")]
    [Tooltip("Index, middle and ring straightness (knuckle-to-tip distance / finger length, 1 = straight) must be at or below this to make a fist.")]
    [Range(0f, 1f)] [SerializeField] private float curledEnter = 0.65f;
    [Tooltip("The fist ends once any of those fingers' straightness rises above this.")]
    [Range(0f, 1f)] [SerializeField] private float curledExit = 0.75f;

    [Header("Walking (m/s of fist speed)")]
    [Tooltip("Fist swing speed below which the player does not walk. Filters out tracking jitter and small movements.")]
    [SerializeField] private float minSwingSpeed = 0.3f;
    [Tooltip("Fist swing speed at which the player walks at full speed.")]
    [SerializeField] private float fullSwingSpeed = 1.2f;
    [Tooltip("Seconds to smooth the fist velocity over. Higher is steadier but slower to react.")]
    [SerializeField] private float swingSmoothingTime = 0.08f;

    [Header("Walking Response (seconds)")]
    [Tooltip("How quickly walking speeds up to match a faster swing.")]
    [SerializeField] private float speedUpTime = 0.15f;
    [Tooltip("How quickly walking slows when the swing slows. Long enough to carry through the pause at each end of a swing.")]
    [SerializeField] private float slowDownTime = 0.5f;
    [Tooltip("How quickly walking stops once no hand is in a fist.")]
    [SerializeField] private float stopTime = 0.1f;

    [Header("Thumbs-Up (meters)")]
    [Tooltip("Thumb tip at least this far from the curled index/middle fingers counts as a thumbs-up.")]
    [SerializeField] private float thumbUpEnter = 0.05f;
    [Tooltip("Thumb tip closer than this to the curled fingers ends the thumbs-up.")]
    [SerializeField] private float thumbUpExit = 0.04f;

    [Header("Turning (degrees of thumb tilt from straight up)")]
    [Tooltip("Tilt below this does not turn, so a roughly upright thumb holds still.")]
    [SerializeField] private float deadZoneAngle = 20f;
    [Tooltip("Tilt at which the player turns at full speed.")]
    [SerializeField] private float fullTurnAngle = 60f;
    [Tooltip("Seconds to smooth the turn input over. Smooths joint jitter.")]
    [SerializeField] private float turnSmoothingTime = 0.1f;

    private struct HandState
    {
        public bool isFist;
        public bool isThumbUp;
        public bool hasPreviousOffset;
        public Vector3 previousOffset;
        public Vector3 swingVelocity;
    }

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    // Below this the input is sent as zero so the providers end locomotion (and the comfort vignette fades).
    private const float k_InputEpsilon = 0.01f;

    private HandState _left;
    private HandState _right;
    private float _move;
    private float _turn;
    private Vector3 _headForward = Vector3.forward;

    /// <summary>Walking input, 0 to 1 of the move provider's Move Speed.</summary>
    public float MoveInput => _move;

    /// <summary>Turning input, -1 (left) to 1 (right) of the turn provider's Turn Speed.</summary>
    public float TurnInput => _turn;

    private void Awake()
    {
        if (moveProvider != null)
        {
            moveProvider.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
            moveProvider.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        }

        if (turnProvider != null)
        {
            turnProvider.leftHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
            turnProvider.rightHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        }
    }

    private void OnDisable()
    {
        _left = default;
        _right = default;
        _move = 0f;
        _turn = 0f;
        WriteInputs();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        float swingSpeed = 0f;
        float turn = 0f;
        bool anyFist = false;

        var subsystem = GetHandSubsystem();
        if (subsystem != null && head != null)
        {
            GetHeadFrame(out Vector3 headPosition, out Vector3 forward, out Vector3 right);
            UpdateHand(subsystem.leftHand, leftHandInteractor, ref _left, headPosition, forward, right, deltaTime, ref swingSpeed, ref turn, ref anyFist);
            UpdateHand(subsystem.rightHand, rightHandInteractor, ref _right, headPosition, forward, right, deltaTime, ref swingSpeed, ref turn, ref anyFist);
        }
        else
        {
            _left = default;
            _right = default;
        }

        float target = anyFist ? Mathf.InverseLerp(minSwingSpeed, fullSwingSpeed, swingSpeed) : 0f;
        float responseTime = !anyFist ? stopTime : target > _move ? speedUpTime : slowDownTime;
        _move = Mathf.Lerp(_move, target, Smoothing(responseTime, deltaTime));
        _turn = Mathf.Lerp(_turn, Mathf.Clamp(turn, -1f, 1f), Smoothing(turnSmoothingTime, deltaTime));

        WriteInputs();
    }

    private void UpdateHand(XRHand hand, XRBaseInteractor interactor, ref HandState state,
        Vector3 headPosition, Vector3 forward, Vector3 right, float deltaTime,
        ref float swingSpeed, ref float turn, ref bool anyFist)
    {
        if (!hand.isTracked || (interactor != null && interactor.hasSelection))
        {
            state = default;
            return;
        }

        state.isFist = IsFist(hand, state.isFist);
        if (!state.isFist)
        {
            state = default;
            return;
        }

        anyFist = true;

        if (TryGetPosition(hand, XRHandJointID.Palm, out Vector3 palm))
        {
            Vector3 offset = palm - headPosition;
            if (state.hasPreviousOffset)
            {
                Vector3 velocity = (offset - state.previousOffset) / deltaTime;
                Vector3 swing = Vector3.Dot(velocity, forward) * forward + velocity.y * Vector3.up;
                state.swingVelocity = Vector3.Lerp(state.swingVelocity, swing, Smoothing(swingSmoothingTime, deltaTime));
                swingSpeed = Mathf.Max(swingSpeed, state.swingVelocity.magnitude);
            }

            state.previousOffset = offset;
            state.hasPreviousOffset = true;
        }

        turn += ThumbTilt(hand, ref state, right);
    }

    /// <summary>
    /// -1 to 1 turn input from a thumbs-up tilted left or right, or 0 when the thumb is wrapped around
    /// the fist, upright within the dead zone, or pointing mostly forward or back.
    /// </summary>
    private float ThumbTilt(XRHand hand, ref HandState state, Vector3 right)
    {
        if (!TryGetPosition(hand, XRHandJointID.ThumbProximal, out Vector3 thumbBase) ||
            !TryGetPosition(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip) ||
            !TryGetPosition(hand, XRHandJointID.IndexIntermediate, out Vector3 indexMiddle) ||
            !TryGetPosition(hand, XRHandJointID.MiddleIntermediate, out Vector3 middleMiddle))
        {
            state.isThumbUp = false;
            return 0f;
        }

        // In an ordinary fist the thumb tip rests on the curled index/middle fingers; in a thumbs-up it is clear of them.
        float clearance = Mathf.Min(Vector3.Distance(thumbTip, indexMiddle), Vector3.Distance(thumbTip, middleMiddle));
        state.isThumbUp = state.isThumbUp ? clearance > thumbUpExit : clearance >= thumbUpEnter;
        if (!state.isThumbUp)
            return 0f;

        Vector3 thumb = thumbTip - thumbBase;
        float sideways = Vector3.Dot(thumb, right);
        float upward = thumb.y;
        if (new Vector2(sideways, upward).magnitude < 0.5f * thumb.magnitude)
            return 0f;

        float angle = Mathf.Atan2(sideways, upward) * Mathf.Rad2Deg;
        return Mathf.Sign(angle) * Mathf.InverseLerp(deadZoneAngle, fullTurnAngle, Mathf.Abs(angle));
    }

    private bool IsFist(XRHand hand, bool wasFist)
    {
        // Hysteresis: easier to stay in the fist than to make it, so walking does not flicker on and off.
        float threshold = wasFist ? curledExit : curledEnter;
        return IsCurled(hand, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip, threshold) &&
               IsCurled(hand, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip, threshold) &&
               IsCurled(hand, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip, threshold);
    }

    /// <summary>
    /// Head position, and its horizontal forward and right directions, in tracking space.
    /// Looking straight up or down keeps the last forward direction.
    /// </summary>
    private void GetHeadFrame(out Vector3 position, out Vector3 forward, out Vector3 right)
    {
        Transform space = trackingSpace != null ? trackingSpace : head.parent;
        position = space != null ? space.InverseTransformPoint(head.position) : head.position;
        Vector3 headForward = space != null ? space.InverseTransformDirection(head.forward) : head.forward;
        headForward.y = 0f;
        if (headForward.sqrMagnitude > 0.01f)
            _headForward = headForward.normalized;

        forward = _headForward;
        right = Vector3.Cross(Vector3.up, forward);
    }

    private void WriteInputs()
    {
        if (moveProvider != null)
            moveProvider.leftHandMoveInput.manualValue = new Vector2(0f, _move > k_InputEpsilon ? _move : 0f);
        if (turnProvider != null)
            turnProvider.leftHandTurnInput.manualValue = new Vector2(Mathf.Abs(_turn) > k_InputEpsilon ? _turn : 0f, 0f);
    }

    /// <summary>Frame-rate independent lerp factor that closes most of the gap in about <paramref name="time"/> seconds.</summary>
    private static float Smoothing(float time, float deltaTime) => time > 0f ? 1f - Mathf.Exp(-deltaTime / time) : 1f;

    /// <summary>Knuckle-to-tip distance divided by the summed bone lengths: about 1 when straight, about 0.5 in a fist.</summary>
    private static bool IsCurled(XRHand hand, XRHandJointID proximal, XRHandJointID intermediate, XRHandJointID distal, XRHandJointID tip, float threshold)
    {
        if (!TryGetPosition(hand, proximal, out Vector3 p) ||
            !TryGetPosition(hand, intermediate, out Vector3 i) ||
            !TryGetPosition(hand, distal, out Vector3 d) ||
            !TryGetPosition(hand, tip, out Vector3 t))
            return false;

        float length = Vector3.Distance(p, i) + Vector3.Distance(i, d) + Vector3.Distance(d, t);
        return length > 0f && Vector3.Distance(p, t) / length <= threshold;
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
}
