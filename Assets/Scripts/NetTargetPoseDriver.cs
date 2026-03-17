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
        netObj = GetComponent<NetworkObject>();
    }

    void Start()
    {
        TryAutoFindSources();
    }

    public override void OnNetworkSpawn()
    {
        TryAutoFindSources();
    }

    void TryAutoFindSources()
    {
        if (!IsOwner && netObj != null && netObj.IsSpawned) return;

        if (headSource == null)
        {
            var t = GameObject.Find("Head VR Target");
            if (t) headSource = t.transform;
        }

        if (leftHandSource == null)
        {
            var t = GameObject.Find("Left Hand VR Target");
            if (t) leftHandSource = t.transform;
        }

        if (rightHandSource == null)
        {
            var t = GameObject.Find("Right Hand VR Target");
            if (t) rightHandSource = t.transform;
        }
    }

    bool ShouldDrive()
    {
        if (netObj == null) return true;
        if (!netObj.IsSpawned) return driveWhenNotSpawned;
        return netObj.IsOwner;
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