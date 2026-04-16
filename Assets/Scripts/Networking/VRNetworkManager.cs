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

    [Tooltip("Maximum players per room (Normcore free tier allows 4)")]
    [SerializeField] private int _roomCapacity = 4;

    [Header("Debug")]
    [SerializeField] private bool _logConnectionEvents = true;

    private Realtime _realtime;

    public bool IsConnected => _realtime != null && _realtime.connected;
    public string RoomName => _roomName;

    private void Awake()
    {
        _realtime = GetComponent<Realtime>();

        _realtime.didConnectToRoom    += OnConnected;
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
                Debug.Log($"[VRNetworkManager] Already connected to '{_realtime.room.name}'.");
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
        if (_logConnectionEvents)
            Debug.Log($"[VRNetworkManager] Connected to room '{realtime.room.name}' " +
                      $"as client #{realtime.clientID}. " +
                      $"Players in room: {realtime.room.connectionCount}");
    }

    private void OnDisconnected(Realtime realtime)
    {
        if (_logConnectionEvents)
            Debug.Log("[VRNetworkManager] Disconnected from room.");
    }
}
