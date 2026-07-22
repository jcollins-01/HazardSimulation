using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.InputSystem;

[RequireComponent(typeof(XRSimpleInteractable))]
public class LeverSqueeze : MonoBehaviour
{
    [Header("Lever")]
    public Transform lever;
    public float idleAngle = 0f;
    public float squeezedAngle = -30f;
    public float leverSpeed = 15f;

    [Header("Particles")]
    public ParticleSystem spray;

    [Header("Safety")]
    public SafetyPin safetyPin;

    [Range(0f, 1f)] public float triggerThreshold = 0.5f;

    XRSimpleInteractable interactable;
    bool handNear;
    bool isSqueezed;
    bool wasSqueezed;

    void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        interactable.hoverEntered.AddListener(_ => handNear = true);
        interactable.hoverExited.AddListener(_ => handNear = false);
    }

    void Update()
    {
        isSqueezed = safetyPin.IsRemoved && handNear && GetTriggerInput();

        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
            isSqueezed = safetyPin.IsRemoved; // desktop test override, still respects the pin

        float targetAngle = isSqueezed ? squeezedAngle : idleAngle;
        lever.localRotation = Quaternion.Lerp(lever.localRotation, Quaternion.Euler(targetAngle, 0f, 0f), Time.deltaTime * leverSpeed);

        if (isSqueezed && !wasSqueezed) spray.Play();
        else if (!isSqueezed && wasSqueezed) spray.Stop();

        wasSqueezed = isSqueezed;
    }

    bool GetTriggerInput() => GetTriggerFromHand(XRNode.LeftHand) || GetTriggerFromHand(XRNode.RightHand);

    bool GetTriggerFromHand(XRNode hand)
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        return device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float v) && v >= triggerThreshold;
    }
}