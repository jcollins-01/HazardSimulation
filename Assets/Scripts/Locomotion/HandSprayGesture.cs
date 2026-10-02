using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Hand-tracking replacement for the controller trigger on sprayers (fire extinguisher / hose).
///
/// Hold the sprayer with one hand, then pinch thumb and index on the other, free hand to spray;
/// open the pinch to stop. This component is the activate input of the holding hand's
/// Near-Far interactor, so XRI raises the same activated / deactivated events the controller
/// trigger does and <see cref="Spray"/> (and its network sync) work unchanged.
///
/// While a sprayer is held, the free hand's interactors are blocked from hovering and selecting,
/// otherwise its pinch would also grab (and, with single select, steal) the sprayer.
///
/// One instance per hand, under that hand's object (built by the locomotion scene builder).
/// Joint poses from XR Hands are in tracking space; only distances are used, so no conversion is needed.
/// </summary>
[DefaultExecutionOrder(XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
public class HandSprayGesture : MonoBehaviour, IXRInputButtonReader
{
    [Tooltip("Hand that holds the sprayer. The opposite hand's pinch sprays.")]
    [SerializeField] private Handedness holdingHand = Handedness.Left;

    [Tooltip("The holding hand's Near-Far interactor. Its activate input is replaced by this gesture.")]
    [SerializeField] private XRBaseInputInteractor holdingInteractor;

    [Tooltip("The free hand's interactors (Near-Far, Poke), blocked while a sprayer is held.")]
    [SerializeField] private XRBaseInteractor[] freeHandInteractors;

    [Header("Free Hand Pinch (meters)")]
    [Tooltip("Thumb tip to index tip distance at or below which the pinch starts spraying.")]
    [SerializeField] private float pinchPressDistance = 0.015f;
    [Tooltip("Thumb tip to index tip distance above which spraying stops.")]
    [SerializeField] private float pinchReleaseDistance = 0.03f;

    private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    private bool _isPerformed;
    private bool _wasPerformedThisFrame;
    private bool _wasCompletedThisFrame;
    private bool _blockingFreeHand;

    private void Awake()
    {
        if (holdingInteractor == null)
            return;
        holdingInteractor.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ObjectReference;
        holdingInteractor.activateInput.SetObjectReference(this);
    }

    private void OnDisable()
    {
        SetPerformed(false);
        BlockFreeHand(false);
    }

    private void Update()
    {
        bool holdingSprayer = IsHoldingSprayer();

        // Re-assert every frame: HandTeleportGesture also toggles these flags when the free hand aims.
        if (holdingSprayer)
            BlockFreeHand(true);
        else if (_blockingFreeHand)
            BlockFreeHand(false);

        SetPerformed(holdingSprayer && IsFreeHandPinching());
    }

    private bool IsHoldingSprayer()
    {
        if (holdingInteractor == null || !holdingInteractor.isActiveAndEnabled || !holdingInteractor.hasSelection)
            return false;
        var held = holdingInteractor.firstInteractableSelected;
        return held != null && held.transform.GetComponentInParent<Spray>() != null;
    }

    private bool IsFreeHandPinching()
    {
        var subsystem = GetHandSubsystem();
        if (subsystem == null)
            return false;

        XRHand freeHand = holdingHand == Handedness.Left ? subsystem.rightHand : subsystem.leftHand;
        if (!freeHand.isTracked ||
            !freeHand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb) ||
            !freeHand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index))
            return false;

        float distance = Vector3.Distance(thumb.position, index.position);
        return _isPerformed ? distance < pinchReleaseDistance : distance <= pinchPressDistance;
    }

    private void SetPerformed(bool performed)
    {
        _wasPerformedThisFrame = !_isPerformed && performed;
        _wasCompletedThisFrame = _isPerformed && !performed;
        _isPerformed = performed;
    }

    private void BlockFreeHand(bool block)
    {
        _blockingFreeHand = block;
        if (freeHandInteractors == null)
            return;
        foreach (var interactor in freeHandInteractors)
        {
            if (interactor == null)
                continue;
            interactor.allowHover = !block;
            interactor.allowSelect = !block;
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

    public bool ReadIsPerformed() => _isPerformed;
    public bool ReadWasPerformedThisFrame() => _wasPerformedThisFrame;
    public bool ReadWasCompletedThisFrame() => _wasCompletedThisFrame;
    public float ReadValue() => _isPerformed ? 1f : 0f;

    public bool TryReadValue(out float value)
    {
        value = ReadValue();
        return _isPerformed;
    }
}
