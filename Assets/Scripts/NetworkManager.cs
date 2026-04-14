using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Handles the initial PUN connection flow for the firefighter training scene.
/// Player instantiation is split into a dedicated method so later steps can add
/// the actual networked VR rig spawn without changing the room lifecycle.
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    private const string RoomName = "FireTrainingRoom";
    private const byte MaxPlayersPerRoom = 8;

    private void Start()
    {
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
        Debug.Log("[NetworkManager] Connected to Photon Master Server.");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = MaxPlayersPerRoom
        };

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
        // Step 3 will replace this placeholder with PhotonNetwork.Instantiate.
        Debug.Log("[NetworkManager] Player spawn trigger reached.");
    }
}
