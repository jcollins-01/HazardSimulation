using UnityEngine;

/// <summary>
/// Drives Unity's built-in Animator IK system from the IK target transforms
/// supplied by NetworkedAvatarIK.
///
/// Attach this to the same GameObject as the Animator (the Firefighter child).
/// Connect headTarget, leftHandTarget, rightHandTarget in the Inspector to the
/// same IK target GameObjects that NetworkedAvatarIK moves each frame.
///
/// IMPORTANT: In the Animator Controller, open each layer's settings (gear icon)
/// and enable "IK Pass" — otherwise OnAnimatorIK will not be called.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AvatarIKDriver : MonoBehaviour
{
    [Tooltip("IK look-at target for the head (same GO as NetworkedAvatarIK.headIKTarget)")]
    public Transform headTarget;
    [Tooltip("IK goal for the left hand (same GO as NetworkedAvatarIK.leftHandIKTarget)")]
    public Transform leftHandTarget;
    [Tooltip("IK goal for the right hand (same GO as NetworkedAvatarIK.rightHandIKTarget)")]
    public Transform rightHandTarget;

    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null) return;

        // Head: Unity uses SetLookAtPosition (AvatarIKGoal has no Head value)
        if (headTarget != null)
        {
            _animator.SetLookAtWeight(1f, 0.3f, 1f, 1f, 0.5f);
            _animator.SetLookAtPosition(headTarget.position);
        }

        // Left hand
        if (leftHandTarget != null)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget.position);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandTarget.rotation);
        }

        // Right hand
        if (rightHandTarget != null)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget.position);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandTarget.rotation);
        }
    }
}
