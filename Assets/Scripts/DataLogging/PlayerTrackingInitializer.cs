using UnityEngine;

/// <summary>
/// Auto-registers player tracking with DataLogger when spawned.
/// </summary>
public class PlayerTrackingInitializer : MonoBehaviour
{
    private void Start()
    {
        // Get or add tracking components
        PlayerMovementTracker movementTracker = GetComponent<PlayerMovementTracker>();
        if (movementTracker == null)
        {
            movementTracker = gameObject.AddComponent<PlayerMovementTracker>();
        }
        
        AudioTranscriptLogger audioTracker = GetComponent<AudioTranscriptLogger>();
        if (audioTracker == null)
        {
            audioTracker = gameObject.AddComponent<AudioTranscriptLogger>();
        }

        // Initialize with a player ID (use transform hash as unique ID)
        int playerId = gameObject.GetInstanceID();
        
        movementTracker.Initialize(playerId);
        audioTracker.Initialize(playerId);

        // Register with DataLogger
        if (DataLogger.Instance != null)
        {
            DataLogger.Instance.RegisterPlayerTracker(playerId, movementTracker);
            DataLogger.Instance.RegisterAudioTracker(playerId, audioTracker);
            Debug.Log($"Player {playerId} tracking initialized");
        }
    }
}
