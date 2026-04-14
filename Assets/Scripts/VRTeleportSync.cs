using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Hooks into the XR Interaction Toolkit teleport event flow and broadcasts the
/// owning rig's final world transform after a teleport completes.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class VRTeleportSync : MonoBehaviourPun
{
    private readonly List<BaseTeleportationInteractable> teleportInteractables = new List<BaseTeleportationInteractable>();
    private bool teleportBroadcastPending;

    private void Awake()
    {
        GetComponentsInChildren(true, teleportInteractables);
    }

    private void OnEnable()
    {
        foreach (BaseTeleportationInteractable teleportInteractable in teleportInteractables)
        {
            teleportInteractable.teleporting.AddListener(OnTeleportQueued);
        }
    }

    private void OnDisable()
    {
        foreach (BaseTeleportationInteractable teleportInteractable in teleportInteractables)
        {
            teleportInteractable.teleporting.RemoveListener(OnTeleportQueued);
        }
    }

    private void OnTeleportQueued(TeleportingEventArgs args)
    {
        if (!photonView.IsMine || teleportBroadcastPending)
        {
            return;
        }

        StartCoroutine(BroadcastTeleportAfterFrame());
    }

    private IEnumerator BroadcastTeleportAfterFrame()
    {
        teleportBroadcastPending = true;
        yield return new WaitForEndOfFrame();

        if (photonView.IsMine)
        {
            Vector3 teleportedPosition = transform.position;
            Quaternion teleportedRotation = transform.rotation;

            photonView.RPC(nameof(ApplyRemoteTeleport), RpcTarget.Others, teleportedPosition, teleportedRotation);
            Debug.Log($"[VRTeleportSync] Broadcast teleport to {teleportedPosition}.");
        }

        teleportBroadcastPending = false;
    }

    [PunRPC]
    private void ApplyRemoteTeleport(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (photonView.IsMine)
        {
            return;
        }

        transform.SetPositionAndRotation(targetPosition, targetRotation);
        Debug.Log($"[VRTeleportSync] Applied remote teleport to {targetPosition}.");
    }
}
