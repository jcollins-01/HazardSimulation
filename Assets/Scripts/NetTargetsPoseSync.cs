using UnityEngine;
using Unity.Netcode;

public class NetTargetsPoseSync : NetworkBehaviour
{
    [Header("Targets (NetTargets)")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    private NetworkVariable<Vector3> headPos = new(writePerm: NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> headRot = new(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> leftPos = new(writePerm: NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> leftRot = new(writePerm: NetworkVariableWritePermission.Owner);

    private NetworkVariable<Vector3> rightPos = new(writePerm: NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> rightRot = new(writePerm: NetworkVariableWritePermission.Owner);

    void LateUpdate()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            if (headTarget) { headPos.Value = headTarget.position; headRot.Value = headTarget.rotation; }
            if (leftHandTarget) { leftPos.Value = leftHandTarget.position; leftRot.Value = leftHandTarget.rotation; }
            if (rightHandTarget) { rightPos.Value = rightHandTarget.position; rightRot.Value = rightHandTarget.rotation; }
        }
        else
        {
            if (headTarget) headTarget.SetPositionAndRotation(headPos.Value, headRot.Value);
            if (leftHandTarget) leftHandTarget.SetPositionAndRotation(leftPos.Value, leftRot.Value);
            if (rightHandTarget) rightHandTarget.SetPositionAndRotation(rightPos.Value, rightRot.Value);
        }
    }
}