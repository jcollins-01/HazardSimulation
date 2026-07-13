using UnityEngine;

/// <summary>
/// Manages XR Origin and player setup for proper head/body tracking.
/// This is the standard approach for OpenXR in modern Unity.
/// </summary>
public class XROriginSetup : MonoBehaviour
{
    [SerializeField] private Transform cameraOffset;
    [SerializeField] private CharacterController characterController;
    
    private Vector3 originPosition;

    private void Start()
    {
        // Get reference to camera
        Camera mainCamera = GetComponentInChildren<Camera>();
        if (mainCamera == null)
        {
            Debug.LogError("No camera found!");
            return;
        }

        cameraOffset = mainCamera.transform;
        characterController = GetComponent<CharacterController>();
        
        // In XR, the origin (this transform) represents the player's feet
        // The camera's local position represents head tracking offset
        // When the camera moves due to head tracking, the origin should NOT move
        
        originPosition = transform.position;
        Debug.Log("XR Origin initialized");
    }

    private void Update()
    {
        // Keep the origin position steady - only camera should move from head tracking
        // Body movement is controlled separately by PlayerController via CharacterController
        
        if (characterController != null && characterController.enabled)
        {
            // The origin position is handled by CharacterController.Move()
            // We just need to make sure head tracking doesn't move the origin
        }
    }

    public Transform GetCameraTransform()
    {
        return cameraOffset;
    }
}
