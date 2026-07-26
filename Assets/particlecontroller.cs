using UnityEngine;
using UnityEngine.XR;

public class ExtinguisherTrigger : MonoBehaviour
{
    [Header("Lever")]
    public Transform lever;
    public float idleAngle = 0f;
    public float squeezedAngle = -30f;
    public float leverSpeed = 10f;

    [Header("Particles")]
    public ParticleSystem spray; // "Play On Awake" should be UNCHECKED

    [Header("Input")]
    [Range(0f, 1f)] public float gripThreshold = 0.5f;
    public KeyCode desktopTestKey = KeyCode.Space;

    bool isSqueezed;
    bool wasSqueezed;

    void Update()
    {
        isSqueezed = GetGripInput();

        float targetAngle = isSqueezed ? squeezedAngle : idleAngle;
        Quaternion targetRot = Quaternion.Euler(targetAngle, 0f, 0f);
        lever.localRotation = Quaternion.Lerp(lever.localRotation, targetRot, Time.deltaTime * leverSpeed);

        if (isSqueezed && !wasSqueezed)
            spray.Play();
        else if (!isSqueezed && wasSqueezed)
            spray.Stop();

        wasSqueezed = isSqueezed;
    }

    bool GetGripInput()
    {
        if (Input.GetKey(desktopTestKey))
            return true;

        return GetGripFromHand(XRNode.LeftHand) || GetGripFromHand(XRNode.RightHand);
    }

    bool GetGripFromHand(XRNode hand)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float gripValue))
            return gripValue >= gripThreshold;

        return false;
    }
}