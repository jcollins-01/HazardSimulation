using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Connects Normcore avatar sync to IK-based full-body avatar animation.
///
/// Attach this to the root VR Player prefab (alongside RealtimeAvatar).
/// Normcore already syncs the "Head", "Left Hand", "Right Hand" child transforms —
/// this script drives the body mesh and IK targets from those every frame,
/// working automatically for both local and remote players.
///
/// EDITOR SETUP (in Unity, open VR Player prefab):
///   1. Drag Assets/firefighter/Firefighter.fbx into the prefab as a child.
///   2. Create 3 empty child GameObjects: "HeadIKTarget", "LeftHandIKTarget", "RightHandIKTarget".
///   3. Add Animator to the Firefighter child; assign VR Rig Animator controller.
///   4. Add AvatarIKDriver to the Firefighter child; connect the 3 IK target GOs.
///   5. Here (NetworkedAvatarIK): assign bodyRoot, headIKTarget, leftHandIKTarget, rightHandIKTarget.
///   6. On LocalAvatarHider: enable _autoHideBodyRenderers so the local player
///      doesn't see their own body from the inside.
/// </summary>
[RequireComponent(typeof(RealtimeAvatar))]
public class NetworkedAvatarIK : MonoBehaviour
{
    [Header("Body Mesh Root")]
    [Tooltip("Root transform of the Firefighter mesh child (drag the Firefighter child here)")]
    public Transform bodyRoot;

    [Header("Body Positioning")]
    [Tooltip("Offset from head position to body root – tune in Play mode")]
    public Vector3 headBodyPositionOffset = new Vector3(0f, -0.6f, 0f);

    [Range(0f, 1f)]
    [Tooltip("How quickly the body yaw follows head yaw. 1 = instant, 0.05 = very lazy")]
    public float turnSmoothness = 0.1f;

    [Header("IK Targets (empty GameObjects inside the prefab)")]
    [Tooltip("Drag 'HeadIKTarget' empty GO here")]
    public Transform headIKTarget;
    [Tooltip("Drag 'LeftHandIKTarget' empty GO here")]
    public Transform leftHandIKTarget;
    [Tooltip("Drag 'RightHandIKTarget' empty GO here")]
    public Transform rightHandIKTarget;

    // Normcore automatically syncs these child GameObjects every frame.
    // On the local avatar they track the XR rig; on remote they are network-replicated.
    private Transform _headSource;
    private Transform _leftHandSource;
    private Transform _rightHandSource;

    private void Start()
    {
        // Names must match the child GameObject names in VR Player.prefab
        _headSource      = transform.Find("Head");
        _leftHandSource  = transform.Find("Left Hand");
        _rightHandSource = transform.Find("Right Hand");

        if (_headSource == null)
            Debug.LogWarning("[NetworkedAvatarIK] 'Head' child not found. " +
                             "Check VR Player prefab has a child named exactly 'Head'.");
    }

    private void LateUpdate()
    {
        if (_headSource == null) return;

        // ── Position the body under the head ─────────────────────────────────
        if (bodyRoot != null)
        {
            bodyRoot.position = _headSource.position + headBodyPositionOffset;

            float targetYaw = _headSource.eulerAngles.y;
            bodyRoot.rotation = Quaternion.Lerp(
                bodyRoot.rotation,
                Quaternion.Euler(0f, targetYaw, 0f),
                turnSmoothness);
        }

        // ── Move IK targets to the Normcore-synced positions ─────────────────
        if (headIKTarget != null)
        {
            headIKTarget.position = _headSource.position;
            headIKTarget.rotation = _headSource.rotation;
        }

        if (leftHandIKTarget != null && _leftHandSource != null)
        {
            leftHandIKTarget.position = _leftHandSource.position;
            leftHandIKTarget.rotation = _leftHandSource.rotation;
        }

        if (rightHandIKTarget != null && _rightHandSource != null)
        {
            rightHandIKTarget.position = _rightHandSource.position;
            rightHandIKTarget.rotation = _rightHandSource.rotation;
        }
    }
}
