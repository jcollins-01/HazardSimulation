using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.XR;
#endif

/// <summary>
/// Properly tracks XR headset position and rotation.
/// Decouples camera movement from player body locomotion.
/// </summary>
public class XRCameraTracker : MonoBehaviour
{
    private Camera cameraComponent;
    private InputDevice headDevice;
    private bool isXRActive;

    private void Start()
    {
        cameraComponent = GetComponent<Camera>();
        isXRActive = XRSettings.enabled;

        if (isXRActive)
        {
            // Find the headset input device
            var inputDevices = new System.Collections.Generic.List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, inputDevices);
            
            if (inputDevices.Count > 0)
            {
                headDevice = inputDevices[0];
                Debug.Log($"XR Camera Tracker initialized. Head device: {headDevice.name}");
            }
        }
    }

    private void Update()
    {
        if (!isXRActive) return;

        // Update camera position and rotation based on actual XR input
        if (headDevice.isValid)
        {
            if (headDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position))
            {
                transform.localPosition = position;
            }

            if (headDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation))
            {
                transform.localRotation = rotation;
            }
        }
    }
}
