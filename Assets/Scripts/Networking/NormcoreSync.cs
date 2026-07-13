using UnityEngine;
using System.Collections.Generic;
#if NORMCORE
using Normal.Realtime;
#endif

/// <summary>
/// Normcore network synchronization system.
/// Handles player spawning, position sync, and network events.
/// </summary>
public class NormcoreSync : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private float syncInterval = 0.1f;
    
    private Dictionary<int, GameObject> connectedPlayers = new Dictionary<int, GameObject>();
    private int localPlayerId;
    private float lastSyncTime;
    private bool isConnected;

#if NORMCORE
    private Realtime _realtime;
#endif

    public static NormcoreSync Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
    }

    private void Start()
    {
        InitializeNormcore();
    }

    private void InitializeNormcore()
    {
#if NORMCORE
        // Get the Realtime component on this GameObject
        _realtime = GetComponent<Realtime>();
        
        if (_realtime == null)
        {
            Debug.LogError("NormcoreSync: No Realtime component found on this GameObject!");
            return;
        }

        // Subscribe to connection event
        _realtime.didConnectToRoom += OnConnectedToRoom;
        _realtime.didDisconnectFromRoom += OnDisconnectedFromRoom;
        
        Debug.Log("Normcore Realtime initialized and waiting for connection...");
        isConnected = false;
#else
        Debug.LogWarning("NORMCORE is not defined. Add #define NORMCORE in your build settings or run in Normcore environment.");
        isConnected = true; // Fallback to local mode
        localPlayerId = NetworkManager.Instance.LocalPlayerId;
#endif
    }

#if NORMCORE
    private void OnConnectedToRoom(Realtime realtime)
    {
        isConnected = true;
        localPlayerId = NetworkManager.Instance.LocalPlayerId;
        Debug.Log($"Connected to Normcore room. Local Player ID: {localPlayerId}");
    }

    private void OnDisconnectedFromRoom(Realtime realtime)
    {
        isConnected = false;
        Debug.LogWarning("Disconnected from Normcore room");
    }
#endif

    private void Update()
    {
        if (!isConnected) return;

        // Sync player position at intervals
        if (Time.time - lastSyncTime > syncInterval)
        {
            SyncPlayerPosition();
            lastSyncTime = Time.time;
        }
    }

    private void SyncPlayerPosition()
    {
        if (NetworkManager.Instance == null) return;

        if (NetworkManager.Instance.ConnectedPlayers.TryGetValue(localPlayerId, out GameObject localPlayer))
        {
            PlayerController controller = localPlayer.GetComponent<PlayerController>();
            if (controller == null) return;

            Vector3 position = controller.transform.position;
            Quaternion rotation = controller.transform.rotation;

#if NORMCORE
            // Normcore RealtimeTransform handles sync automatically
            // Position is synced just by moving the transform
#endif
        }
    }

    public bool IsConnected => isConnected;
    public Dictionary<int, GameObject> ConnectedPlayers => connectedPlayers;

    /// <summary>
    /// Called when a new player joins the session (for testing).
    /// </summary>
    public void OnPlayerJoined(int playerId)
    {
        if (playerId == localPlayerId) return;

        Vector3 spawnPos = new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
        
#if NORMCORE
        // Use Realtime.Instantiate for networked objects
        if (_realtime != null)
        {
            var options = new Realtime.InstantiateOptions
            {
                ownedByClient = false,
                preventOwnershipTakeover = false,
                useInstance = _realtime
            };
            GameObject player = Realtime.Instantiate(playerPrefab.name, spawnPos, Quaternion.identity, options);
            player.name = $"Player_{playerId}";

            var controller = player.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.Initialize(playerId, isLocal: false);
            }

            connectedPlayers[playerId] = player;
            Debug.Log($"Player {playerId} joined");
        }
        else
        {
            SpawnPlayerLocal(playerId, spawnPos);
        }
#else
        SpawnPlayerLocal(playerId, spawnPos);
#endif
    }

    private void SpawnPlayerLocal(int playerId, Vector3 spawnPos)
    {
        GameObject player = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        player.name = $"Player_{playerId}";

        var controller = player.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.Initialize(playerId, isLocal: false);
        }

        connectedPlayers[playerId] = player;
        Debug.Log($"Player {playerId} joined (local)");
    }

    /// <summary>
    /// Called when a player leaves the session (for testing).
    /// </summary>
    public void OnPlayerLeft(int playerId)
    {
        if (connectedPlayers.TryGetValue(playerId, out GameObject player))
        {
            connectedPlayers.Remove(playerId);
            Destroy(player);
            Debug.Log($"Player {playerId} left");
        }
    }
}
