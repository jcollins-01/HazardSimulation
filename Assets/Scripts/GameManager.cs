using UnityEngine;

/// <summary>
/// Main game manager that coordinates all systems.
/// Handles scene setup, player spawning, and session management.
/// </summary>
public class GameManager : MonoBehaviour
{
    [SerializeField] private int maxPlayers = 3;
    [SerializeField] private Vector3[] spawnPoints = new Vector3[3]
    {
        new Vector3(-2, 0, 0),
        new Vector3(0, 0, 0),
        new Vector3(2, 0, 0)
    };

    private int playersConnected = 0;

    public static GameManager Instance { get; private set; }

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
        InitializeGame();
    }

    private void InitializeGame()
    {
        Debug.Log("Game Manager initializing...");
        
        // Verify key systems are present
        if (NetworkManager.Instance == null)
        {
            Debug.LogWarning("NetworkManager not found in scene.");
        }

        if (DataLogger.Instance == null)
        {
            Debug.LogError("DataLogger not found in scene!");
            return;
        }

        Debug.Log("All systems initialized successfully!");
    }

    /// <summary>
    /// Called when a new player joins the session.
    /// </summary>
    public void OnPlayerJoined(int playerId)
    {
        if (playersConnected >= maxPlayers)
        {
            Debug.LogWarning($"Max players ({maxPlayers}) already connected!");
            return;
        }

        Vector3 spawnPoint = spawnPoints[playersConnected % spawnPoints.Length];
        Debug.Log($"Player {playerId} joined. Spawning at {spawnPoint}");
        
        // TODO: Call network manager to spawn player
        playersConnected++;
    }

    /// <summary>
    /// Called when a player leaves the session.
    /// </summary>
    public void OnPlayerLeft(int playerId)
    {
        Debug.Log($"Player {playerId} left the session.");
        playersConnected--;
    }

    public int GetConnectedPlayerCount()
    {
        return playersConnected;
    }
}
