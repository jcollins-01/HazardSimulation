using UnityEngine;
using Unity.Netcode;

public class DummyPoseInput_NGO : NetworkBehaviour
{
    [Header("Targets (optional, auto-find by name if empty)")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Root Move")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 90f;

    [Header("Head Look")]
    public float headYawSpeed = 2.5f;

    [Header("Hand Move")]
    public float handMoveSpeed = 0.8f;
    public float maxHandOffset = 0.25f;
    public bool allowHandVertical = false;
    public KeyCode resetKey = KeyCode.R;

    private Vector3 leftHome, rightHome;
    private bool homeCaptured;

    void Awake()
    {
        if (!head) head = transform.Find("Head");
        if (!leftHand) leftHand = transform.Find("LeftHand");
        if (!rightHand) rightHand = transform.Find("RightHand");
    }

    public override void OnNetworkSpawn()
    {
        CaptureHome();
    }

    void CaptureHome()
    {
        if (leftHand) leftHome = leftHand.localPosition;
        if (rightHand) rightHome = rightHand.localPosition;
        homeCaptured = true;
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;
        if (!homeCaptured) CaptureHome();

        float h = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float v = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);

        Vector3 move = (transform.forward * v + transform.right * h).normalized;
        transform.position += move * moveSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.Q)) transform.Rotate(Vector3.up, -turnSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.E)) transform.Rotate(Vector3.up, turnSpeed * Time.deltaTime);

        // Head: mouse X yaw
        if (head)
        {
            float mx = Input.GetAxis("Mouse X");
            head.localRotation *= Quaternion.Euler(0f, mx * headYawSpeed, 0f);
        }

        // Reset
        if (Input.GetKeyDown(resetKey)) ResetHands();

        // Left hand: IJKL
        if (leftHand)
        {
            Vector3 d = Vector3.zero;
            if (Input.GetKey(KeyCode.I)) d += Vector3.forward;
            if (Input.GetKey(KeyCode.K)) d += Vector3.back;
            if (Input.GetKey(KeyCode.J)) d += Vector3.left;
            if (Input.GetKey(KeyCode.L)) d += Vector3.right;
            ApplyHandTarget(leftHand, leftHome, d);
        }

        // Right hand: Arrow keys
        if (rightHand)
        {
            Vector3 d = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow)) d += Vector3.forward;
            if (Input.GetKey(KeyCode.DownArrow)) d += Vector3.back;
            if (Input.GetKey(KeyCode.LeftArrow)) d += Vector3.left;
            if (Input.GetKey(KeyCode.RightArrow)) d += Vector3.right;
            ApplyHandTarget(rightHand, rightHome, d);
        }
    }

    void ResetHands()
    {
        if (leftHand) leftHand.localPosition = leftHome;
        if (rightHand) rightHand.localPosition = rightHome;
    }

    void ApplyHandTarget(Transform hand, Vector3 home, Vector3 dir)
    {
        Vector3 targetOffset = Vector3.zero;
        if (dir.sqrMagnitude > 0.0001f)
        {
            targetOffset = dir.normalized * maxHandOffset;
            if (!allowHandVertical) targetOffset.y = 0f;
        }

        Vector3 currentOffset = hand.localPosition - home;
        if (!allowHandVertical) currentOffset.y = 0f;

        currentOffset = Vector3.MoveTowards(currentOffset, targetOffset, handMoveSpeed * Time.deltaTime);
        hand.localPosition = home + currentOffset;
    }
}