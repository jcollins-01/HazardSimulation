using UnityEngine;

/// <summary>
/// Sets up XR camera and hand tracking for VR play.
/// Automatically configures camera hierarchy for OpenXR.
/// </summary>
public class XRRigSetup : MonoBehaviour
{
    private PlayerController playerController;
    
    private Camera mainCamera;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        SetupXRRig();
    }

    private void SetupXRRig()
    {
        // Get or create camera
        mainCamera = GetComponentInChildren<Camera>();
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("Camera");
            cameraObj.transform.SetParent(transform);
            cameraObj.transform.localPosition = Vector3.zero;
            mainCamera = cameraObj.AddComponent<Camera>();
        }

        // Set this as the main camera for XR
        mainCamera.tag = "MainCamera";

        Debug.Log("XR Rig configured for player");
    }
}
