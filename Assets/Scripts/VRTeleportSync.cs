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

    private void Start()
    {
        StartCoroutine(RegisterTeleportInteractables());
    }

    private void OnDisable()
    {
        foreach (BaseTeleportationInteractable teleportInteractable in teleportInteractables)
        {
            teleportInteractable.teleporting.RemoveListener(OnTeleportQueued);
        }
    }

    private IEnumerator RegisterTeleportInteractables()
    {
        BaseTeleportationInteractable[] interactables;
        do
        {
            yield return new WaitForSeconds(0.5f);
            interactables = FindObjectsByType<BaseTeleportationInteractable>(FindObjectsSortMode.None);
        }
        while (interactables.Length == 0);

        teleportInteractables.Clear();
        teleportInteractables.AddRange(interactables);

        foreach (BaseTeleportationInteractable interactable in teleportInteractables)
        {
            interactable.teleporting.AddListener(OnTeleportQueued);
        }

        Debug.Log($"[VRTeleportSync] Registered {interactables.Length} teleport interactable(s).");
    }

    private void OnTeleportQueued(TeleportingEventArgs args)
    {
        if (!photonView.IsMine || teleportBroadcastPending)
        {
            return;
        }

        Debug.Log($"[VRTeleportSync] Local teleport queued for actor {photonView.OwnerActorNr}.");
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
