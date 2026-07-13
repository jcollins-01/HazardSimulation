using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Tracks individual player movement data over time.
/// Records position, rotation, and timestamps.
/// </summary>
public class PlayerMovementTracker : MonoBehaviour
{
    [System.Serializable]
    public struct MovementFrame
    {
        public float timestamp;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
    }

    private int playerId;
    private List<MovementFrame> movementHistory = new List<MovementFrame>();
    private Vector3 lastPosition;
    private float lastRecordTime;
    private float recordInterval = 0.1f; // Record every 0.1 seconds

    public List<MovementFrame> MovementHistory => movementHistory;
    public int PlayerId => playerId;

    public void Initialize(int id)
    {
        playerId = id;
        lastPosition = transform.position;
        lastRecordTime = Time.time - recordInterval; // Allow first recording to happen immediately
        Debug.Log($"PlayerMovementTracker initialized for player {id}");
    }

    private void Update()
    {
        // Continuously record player position during gameplay
        RecordPosition(transform.position);
    }

    public void RecordPosition(Vector3 position)
    {
        if (Time.time - lastRecordTime < recordInterval)
            return;

        Vector3 velocity = (position - lastPosition) / (Time.time - lastRecordTime);
        
        MovementFrame frame = new MovementFrame
        {
            timestamp = Time.time,
            position = position,
            rotation = transform.rotation,
            velocity = velocity
        };

        movementHistory.Add(frame);
        lastPosition = position;
        lastRecordTime = Time.time;

        // Keep memory usage reasonable - store last 5 minutes of data
        if (movementHistory.Count > 3000) // 3000 frames at 0.1s = 300s = 5 min
        {
            movementHistory.RemoveAt(0);
        }
    }

    public void RecordRotation(Quaternion rotation)
    {
        if (movementHistory.Count > 0)
        {
            MovementFrame lastFrame = movementHistory[movementHistory.Count - 1];
            lastFrame.rotation = rotation;
            movementHistory[movementHistory.Count - 1] = lastFrame;
        }
    }

    /// <summary>
    /// Export movement data as CSV for analysis.
    /// </summary>
    public string ExportAsCSV()
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,PositionX,PositionY,PositionZ,RotationX,RotationY,RotationZ,RotationW,VelocityX,VelocityY,VelocityZ");
        
        foreach (var frame in movementHistory)
        {
            csv.AppendLine($"{frame.timestamp},{frame.position.x},{frame.position.y},{frame.position.z}," +
                          $"{frame.rotation.x},{frame.rotation.y},{frame.rotation.z},{frame.rotation.w}," +
                          $"{frame.velocity.x},{frame.velocity.y},{frame.velocity.z}");
        }
        
        return csv.ToString();
    }

    /// <summary>
    /// Export movement data as JSON for logging.
    /// </summary>
    public string ExportAsJSON()
    {
        var export = new
        {
            playerId = playerId,
            startTime = movementHistory.Count > 0 ? movementHistory[0].timestamp : 0,
            endTime = movementHistory.Count > 0 ? movementHistory[movementHistory.Count - 1].timestamp : 0,
            frameCount = movementHistory.Count,
            frames = movementHistory
        };

        return JsonUtility.ToJson(new { movement = export }, prettyPrint: true);
    }

    /// <summary>
    /// Clear recorded history.
    /// </summary>
    public void ClearHistory()
    {
        movementHistory.Clear();
    }
}
