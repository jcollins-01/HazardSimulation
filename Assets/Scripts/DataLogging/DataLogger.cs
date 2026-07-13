using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

/// <summary>
/// Centralized system for logging all player data (movement, audio, etc).
/// Saves data to disk at regular intervals and when session ends.
/// </summary>
public class DataLogger : MonoBehaviour
{
    [SerializeField] private float autoSaveInterval = 60f; // Auto-save every 60 seconds
    [SerializeField] private string logDirectory = "SessionLogs";
    
    private Dictionary<int, PlayerMovementTracker> trackedPlayers = new Dictionary<int, PlayerMovementTracker>();
    private Dictionary<int, AudioTranscriptLogger> audioTrackers = new Dictionary<int, AudioTranscriptLogger>();
    private float lastSaveTime;
    private string sessionId;

    public static DataLogger Instance { get; private set; }

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
        InitializeSession();
    }

    private void Update()
    {
        // Auto-save at intervals
        if (Time.time - lastSaveTime > autoSaveInterval)
        {
            SaveAllData();
            lastSaveTime = Time.time;
        }
    }

    private void OnApplicationQuit()
    {
        SaveAllData();
        Debug.Log("All session data saved on application quit.");
    }

    private void InitializeSession()
    {
        sessionId = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        string sessionPath = Path.Combine(logDirectory, sessionId);
        if (!Directory.Exists(sessionPath))
        {
            Directory.CreateDirectory(sessionPath);
        }

        lastSaveTime = Time.time;
        Debug.Log($"Data logging session started. Session ID: {sessionId}");
        Debug.Log($"Logs will be saved to: {Path.GetFullPath(sessionPath)}");
    }

    /// <summary>
    /// Register a player's movement tracker for logging.
    /// </summary>
    public void RegisterPlayerTracker(int playerId, PlayerMovementTracker tracker)
    {
        trackedPlayers[playerId] = tracker;
        Debug.Log($"Registered movement tracker for Player {playerId}");
    }

    /// <summary>
    /// Register a player's audio tracker for logging.
    /// </summary>
    public void RegisterAudioTracker(int playerId, AudioTranscriptLogger tracker)
    {
        audioTrackers[playerId] = tracker;
        Debug.Log($"Registered audio tracker for Player {playerId}");
    }

    /// <summary>
    /// Save all player data to disk.
    /// </summary>
    public void SaveAllData()
    {
        string sessionPath = Path.Combine(logDirectory, sessionId);
        
        foreach (var kvp in trackedPlayers)
        {
            int playerId = kvp.Key;
            PlayerMovementTracker tracker = kvp.Value;
            
            string movementFile = Path.Combine(sessionPath, $"player_{playerId}_movement.csv");
            string csv = tracker.ExportAsCSV();
            File.WriteAllText(movementFile, csv);
        }

        foreach (var kvp in audioTrackers)
        {
            int playerId = kvp.Key;
            AudioTranscriptLogger tracker = kvp.Value;
            
            string audioFile = Path.Combine(sessionPath, $"player_{playerId}_audio.json");
            string json = tracker.ExportAsJSON();
            File.WriteAllText(audioFile, json);
        }

        Debug.Log($"Saved all player data at {System.DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// Get the current session ID.
    /// </summary>
    public string GetSessionId()
    {
        return sessionId;
    }

    /// <summary>
    /// Get the path to the current session's log directory.
    /// </summary>
    public string GetSessionPath()
    {
        return Path.Combine(logDirectory, sessionId);
    }
}
