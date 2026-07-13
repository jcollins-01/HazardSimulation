using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.XR;
#endif

/// <summary>
/// Simple VR controller movement - reads left thumbstick for locomotion.
/// </summary>
public class VRLocomotion : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private CharacterController characterController;
    
    private float verticalVelocity;

    private void Start()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        Debug.Log("VRLocomotion initialized");
    }

    private void Update()
    {
        HandleMovement();
    }

    private void HandleMovement()
    {
        if (characterController == null) return;

        // Get left controller input
        var inputDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller,
            inputDevices);

        Vector3 moveDir = Vector3.zero;

        if (inputDevices.Count > 0)
        {
            UnityEngine.XR.InputDevice leftController = inputDevices[0];
            
            if (leftController.isValid)
            {
                // Get thumbstick input
                if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 thumbstick))
                {
                    moveDir = new Vector3(thumbstick.x, 0, thumbstick.y).normalized;
                    
                    if (moveDir.magnitude > 0.1f)
                    {
                        Debug.Log($"Movement input: {moveDir}");
                    }
                }
            }
        }

        // Apply gravity
        if (characterController.isGrounded)
        {
            verticalVelocity = -0.5f; // Small negative to keep grounded
        }
        else
        {
            verticalVelocity -= 9.81f * Time.deltaTime;
        }

        // Move relative to player direction
        Vector3 moveVelocity = (transform.forward * moveDir.z + transform.right * moveDir.x) * moveSpeed;
        moveVelocity.y = verticalVelocity;

        characterController.Move(moveVelocity * Time.deltaTime);
    }
}
