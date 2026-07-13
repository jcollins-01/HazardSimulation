using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles Normcore network initialization and player connection management.
/// </summary>
public class NetworkManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Vector3[] spawnPoints = new Vector3[3]
    {
        new Vector3(-2, 0, 0),
        new Vector3(0, 0, 0),
        new Vector3(2, 0, 0)
    };
    
    private Dictionary<int, GameObject> connectedPlayers = new Dictionary<int, GameObject>();
    private int localPlayerId;
    private int nextSpawnIndex = 0;
    
    public static NetworkManager Instance { get; private set; }

    public int LocalPlayerId => localPlayerId;
    public Dictionary<int, GameObject> ConnectedPlayers => connectedPlayers;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        InitializeNetworking();
    }

    private void InitializeNetworking()
    {
        localPlayerId = Random.Range(1000, 9999);
        Debug.Log($"Network Manager initialized. Local Player ID: {localPlayerId}");
        
        // Spawn local player immediately
        SpawnLocalPlayer();
    }

    private void SpawnLocalPlayer()
    {
        Debug.Log("SpawnLocalPlayer() called");
        
        if (playerPrefab == null)
        {
            Debug.LogError("ERROR: Player prefab is NULL!");
            return;
        }

        Vector3 spawnPos = spawnPoints[nextSpawnIndex % spawnPoints.Length];
        nextSpawnIndex++;
        
        Debug.Log($"Attempting to spawn local player at {spawnPos}");
        SpawnPlayer(localPlayerId, spawnPos, Quaternion.identity, isLocal: true);
    }

    /// <summary>
    /// Spawn a player at a given position.
    /// </summary>
    public void SpawnPlayer(int playerId, Vector3 position, Quaternion rotation, bool isLocal = false)
    {
        if (connectedPlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"Player {playerId} already spawned!");
            return;
        }

        GameObject player = Instantiate(playerPrefab, position, rotation);
        player.name = $"Player_{playerId}";
        
        var controller = player.GetComponent<PlayerController>();
        if (controller != null)
        {
            controller.Initialize(playerId, isLocal);
        }

        connectedPlayers[playerId] = player;
        
        // Register trackers with DataLogger
        var movementTracker = player.GetComponent<PlayerMovementTracker>();
        if (movementTracker != null && DataLogger.Instance != null)
        {
            DataLogger.Instance.RegisterPlayerTracker(playerId, movementTracker);
        }

        var audioTracker = player.GetComponent<AudioTranscriptLogger>();
        if (audioTracker != null && DataLogger.Instance != null)
        {
            DataLogger.Instance.RegisterAudioTracker(playerId, audioTracker);
        }

        Debug.Log($"Player {playerId} spawned at {position}");
    }

    /// <summary>
    /// Remove a player from the scene.
    /// </summary>
    public void DespawnPlayer(int playerId)
    {
        if (connectedPlayers.TryGetValue(playerId, out GameObject player))
        {
            connectedPlayers.Remove(playerId);
            Destroy(player);
            Debug.Log($"Player {playerId} despawned");
        }
    }
}
