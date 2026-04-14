using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Handles the initial PUN connection flow for the firefighter training scene.
/// Player instantiation is split into a dedicated method so later steps can add
/// the actual networked VR rig spawn without changing the room lifecycle.
///
/// Local two-player test:
/// 1. Open this scene in the Unity Editor and press Play.
/// 2. Launch a second peer using a built player, or a second Unity instance if your workflow supports it.
/// 3. Confirm both peers connect to the same room and each spawns exactly one owned rig.
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    private const string RoomName = "FireTrainingRoom";
    private const string PlayerPrefabName = "NetworkedVRPlayer";
    private const byte MaxPlayersPerRoom = 8;

    [SerializeField]
    private Transform bootstrapRig;

    private GameObject localPlayerInstance;

    private void Start()
    {
        if (bootstrapRig == null)
        {
            bootstrapRig = FindBootstrapRig();
        }

        Debug.Log($"[NetworkManager] Bootstrap rig lookup result: {(bootstrapRig != null ? bootstrapRig.name : "not found")}.");

        if (PhotonNetwork.IsConnected)
        {
            Debug.Log("[NetworkManager] Photon is already connected.");
            return;
        }

        PhotonNetwork.AutomaticallySyncScene = false;

        Debug.Log("[NetworkManager] Connecting to Photon using configured settings.");
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log($"[NetworkManager] Connected to Photon Master Server as actor {PhotonNetwork.LocalPlayer?.ActorNumber}.");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = MaxPlayersPerRoom
        };

        Debug.Log($"[NetworkManager] Joining or creating room '{RoomName}' with max {MaxPlayersPerRoom} players.");
        PhotonNetwork.JoinOrCreateRoom(RoomName, roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[NetworkManager] Joined room '{PhotonNetwork.CurrentRoom.Name}' with {PhotonNetwork.CurrentRoom.PlayerCount} player(s).");
        TriggerPlayerSpawn();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[NetworkManager] Disconnected from Photon. Reason: {cause}");
    }

    private void TriggerPlayerSpawn()
    {
        if (localPlayerInstance != null)
        {
            Debug.Log("[NetworkManager] Local player is already spawned for this client.");
            return;
        }

        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (bootstrapRig != null)
        {
            spawnPosition = bootstrapRig.position;
            spawnRotation = bootstrapRig.rotation;
            Debug.Log($"[NetworkManager] Using bootstrap rig '{bootstrapRig.name}' as spawn point at {spawnPosition}.");
            bootstrapRig.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[NetworkManager] No bootstrap XR rig found. Spawning player at world origin.");
        }

        localPlayerInstance = PhotonNetwork.Instantiate(PlayerPrefabName, spawnPosition, spawnRotation);
        Debug.Log($"[NetworkManager] Spawned local player instance '{localPlayerInstance.name}' for actor {PhotonNetwork.LocalPlayer.ActorNumber}.");
    }

    private Transform FindBootstrapRig()
    {
        GameObject xrRig = GameObject.Find("XR Origin (XR Rig)");
        if (xrRig != null)
        {
            Debug.Log("[NetworkManager] Found bootstrap rig named 'XR Origin (XR Rig)'.");
            return xrRig.transform;
        }

        GameObject vrOrigin = GameObject.Find("XR Origin (VR)");
        if (vrOrigin != null)
        {
            Debug.Log("[NetworkManager] Found bootstrap rig named 'XR Origin (VR)'.");
            return vrOrigin.transform;
        }

        return null;
    }
}
