using UnityEngine;
using Unity.Netcode;

public class NetTargetsPoseDriver : NetworkBehaviour
{
    [Header("Sources (XR Rig)")]
    public Transform headSource;
    public Transform leftHandSource;
    public Transform rightHandSource;

    [Header("Destinations (NetTargets)")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    [Tooltip("For offline testing in-scene (not spawned).")]
    public bool driveWhenNotSpawned = true;

    NetworkObject netObj;

    void Awake()
    {
        netObj = GetComponentInParent<NetworkObject>();
    }

    bool ShouldDrive()
    {
        if (netObj == null) return true;                 // not networked yet
        if (!netObj.IsSpawned) return driveWhenNotSpawned;
        return netObj.IsOwner;                           // owner only
    }

    void LateUpdate()
    {
        if (!ShouldDrive()) return;

        CopyPose(headSource, headTarget);
        CopyPose(leftHandSource, leftHandTarget);
        CopyPose(rightHandSource, rightHandTarget);
    }

    static void CopyPose(Transform src, Transform dst)
    {
        if (src == null || dst == null) return;
        dst.SetPositionAndRotation(src.position, src.rotation);
    }
}