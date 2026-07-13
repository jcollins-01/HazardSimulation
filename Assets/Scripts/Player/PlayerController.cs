using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.XR;
#endif

/// <summary>
/// Controls individual player movement and input.
/// Supports both desktop (keyboard/mouse) and XR (hand controllers).
/// Syncs data with Normcore for other players to see.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationSpeed = 100f;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;
    
    private int playerId;
    private bool isLocalPlayer;
    private PlayerMovementTracker movementTracker;
    private Vector3 moveDirection;
    private float verticalVelocity;
    private bool isXRActive;

    private void OnEnable()
    {
        if (isLocalPlayer)
        {
            InputSystem.onActionChange += OnInputActionChange;
        }
    }

    private void OnDisable()
    {
        if (isLocalPlayer)
        {
            InputSystem.onActionChange -= OnInputActionChange;
        }
    }

    public void Initialize(int id, bool isLocal)
    {
        playerId = id;
        isLocalPlayer = isLocal;
        
        movementTracker = GetComponent<PlayerMovementTracker>();
        if (movementTracker == null)
        {
            movementTracker = gameObject.AddComponent<PlayerMovementTracker>();
        }
        
        movementTracker.Initialize(playerId);

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
            }
        }

        // Auto-find Head by name
        if (headTransform == null)
        {
            Transform foundHead = transform.Find("Head");
            if (foundHead != null)
            {
                headTransform = foundHead;
            }
            else
            {
                Debug.LogWarning("Head transform not found!");
            }
        }

        // Check if XR is running
        isXRActive = XRSettings.enabled;

        // Disable input for remote players
        var inputModule = GetComponent<InputModule>();
        if (inputModule != null)
        {
            inputModule.enabled = isLocal;
        }

        Debug.Log($"Player {playerId} initialized. IsLocal: {isLocal}, XR Active: {isXRActive}");
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (isXRActive)
        {
            UpdateXRMovement();
        }
        else
        {
            UpdateDesktopMovement();
        }

        UpdateTracking();
    }

    private void UpdateDesktopMovement()
    {
        // WASD movement
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        moveDirection = new Vector3(horizontal, 0, vertical).normalized;

        if (characterController.isGrounded)
        {
            verticalVelocity = 0;
        }
        else
        {
            verticalVelocity -= 9.81f * Time.deltaTime;
        }

        Vector3 moveVelocity = (transform.forward * moveDirection.z + transform.right * moveDirection.x) * moveSpeed;
        moveVelocity.y = verticalVelocity;

        characterController.Move(moveVelocity * Time.deltaTime);

        // Mouse look
        float mouseX = Input.GetAxis("Mouse X");
        transform.Rotate(0, mouseX * rotationSpeed * Time.deltaTime, 0);
    }

    private void UpdateXRMovement()
    {
        // Simple XR: Only move based on controller input, let OpenXR handle camera tracking
        moveDirection = Vector3.zero;

        // Get left controller thumbstick
        var inputDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller, inputDevices);
        
        if (inputDevices.Count > 0)
        {
            UnityEngine.XR.InputDevice leftController = inputDevices[0];
            if (leftController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 thumbstick))
            {
                moveDirection = new Vector3(thumbstick.x, 0, thumbstick.y).normalized;
            }
        }

        // Apply movement
        if (characterController.isGrounded)
        {
            verticalVelocity = 0;
        }
        else
        {
            verticalVelocity -= 9.81f * Time.deltaTime;
        }

        Vector3 moveVelocity = (transform.forward * moveDirection.z + transform.right * moveDirection.x) * moveSpeed;
        moveVelocity.y = verticalVelocity;

        characterController.Move(moveVelocity * Time.deltaTime);
        
        // That's it. OpenXR handles camera tracking automatically.
    }

    private void UpdateTracking()
    {
        if (movementTracker != null)
        {
            movementTracker.RecordPosition(transform.position);
            movementTracker.RecordRotation(transform.rotation);
        }
    }

    private void OnInputActionChange(object action, InputActionChange change)
    {
        // Placeholder for input system changes
    }

    /// <summary>
    /// Get the head tracking transform (for camera positioning).
    /// </summary>
    public Transform GetHeadTransform()
    {
        return headTransform;
    }

    /// <summary>
    /// Get the left hand transform.
    /// </summary>
    public Transform GetLeftHandTransform()
    {
        return leftHandTransform;
    }

    /// <summary>
    /// Get the right hand transform.
    /// </summary>
    public Transform GetRightHandTransform()
    {
        return rightHandTransform;
    }
}
