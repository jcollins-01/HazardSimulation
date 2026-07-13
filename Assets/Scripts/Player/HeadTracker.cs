using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.XR;
#endif

/// <summary>
/// Applies XR head tracking data to this transform.
/// Attach to the "Head" GameObject to make it follow headset position.
/// </summary>
public class HeadTracker : MonoBehaviour
{
    private UnityEngine.XR.InputDevice headDevice;
    private bool isXRActive;

    private void Start()
    {
        isXRActive = XRSettings.enabled;

        if (isXRActive)
        {
            // Find the head-mounted XR device
            var inputDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.HeadMounted, inputDevices);
            
            if (inputDevices.Count > 0)
            {
                headDevice = inputDevices[0];
                Debug.Log($"Head tracker found device: {headDevice.name}");
            }
            else
            {
                Debug.LogWarning("No head-mounted XR device found!");
            }
        }
    }

    private void Update()
    {
        if (!isXRActive || !headDevice.isValid) return;

        // Apply head position to this transform
        if (headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 position))
        {
            transform.localPosition = position;
        }

        // Apply head rotation to this transform
        if (headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion rotation))
        {
            transform.localRotation = rotation;
        }
    }
}
