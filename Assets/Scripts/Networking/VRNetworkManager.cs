using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Central networking manager for the HazardSimulation VR project.
///
/// Place this on the same GameObject as your Realtime component.
/// It handles:
///   - Connecting to the correct Normcore room on Start
///   - Logging connection events for debugging
///   - Exposing simple connection state for other scripts
///
/// The RealtimeAvatarManager (already set up in Base scene) takes care of
/// spawning and syncing each player's VR avatar automatically once connected.
/// Teleportation is synced automatically because RealtimeAvatar tracks the
/// Main Camera transform every frame.
/// </summary>
[RequireComponent(typeof(Realtime))]
public class VRNetworkManager : MonoBehaviour
{
    [Header("Room Settings")]
    [Tooltip("All players on the same room name will see each other")]
    [SerializeField] private string _roomName = "HazardSimulation";

    [Header("Debug")]
    [SerializeField] private bool _logConnectionEvents = true;

    private Realtime _realtime;
    private RealtimeAvatarManager _avatarManager;

    public bool IsConnected => _realtime != null && _realtime.connected;
    public string RoomName => _roomName;

    private void Awake()
    {
        _realtime = GetComponent<Realtime>();
        _avatarManager = GetComponent<RealtimeAvatarManager>();

        _realtime.didConnectToRoom      += OnConnected;
        _realtime.didDisconnectFromRoom += OnDisconnected;
    }

    private void Start()
    {
        Connect();
    }

    private void OnDestroy()
    {
        if (_realtime == null) return;
        _realtime.didConnectToRoom    -= OnConnected;
        _realtime.didDisconnectFromRoom -= OnDisconnected;
    }

    /// <summary>Connect to the configured Normcore room.</summary>
    public void Connect()
    {
        if (_realtime.connected)
        {
            if (_logConnectionEvents)
                Debug.Log($"[VRNetworkManager] Already connected to '{_roomName}'.");
            return;
        }

        if (_logConnectionEvents)
            Debug.Log($"[VRNetworkManager] Connecting to room '{_roomName}'...");

        _realtime.Connect(_roomName);
    }

    /// <summary>Disconnect from the current room.</summary>
    public void Disconnect()
    {
        if (!_realtime.connected) return;

        if (_logConnectionEvents)
            Debug.Log("[VRNetworkManager] Disconnecting...");

        _realtime.Disconnect();
    }

    private void OnConnected(Realtime realtime)
    {
        if (!_logConnectionEvents) return;

        int playerCount = _avatarManager != null ? _avatarManager.avatars.Count : 0;
        Debug.Log($"[VRNetworkManager] Connected to room '{_roomName}' " +
                  $"as client #{realtime.clientID}. " +
                  $"Players in room: {playerCount}");
    }

    private void OnDisconnected(Realtime realtime)
    {
        if (_logConnectionEvents)
            Debug.Log("[VRNetworkManager] Disconnected from room.");
    }
}
