using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Logs audio transcripts for individual players.
/// Stores timestamps and transcript text for later analysis.
/// </summary>
public class AudioTranscriptLogger : MonoBehaviour
{
    [System.Serializable]
    public struct TranscriptEntry
    {
        public float timestamp;
        public string transcript;
        public float confidence;
    }

    private int playerId;
    private List<TranscriptEntry> transcripts = new List<TranscriptEntry>();

    public List<TranscriptEntry> Transcripts => transcripts;
    public int PlayerId => playerId;

    public void Initialize(int id)
    {
        playerId = id;
    }

    /// <summary>
    /// Add a transcript entry (called by transcription service).
    /// </summary>
    public void AddTranscript(string text, float confidence = 1.0f)
    {
        TranscriptEntry entry = new TranscriptEntry
        {
            timestamp = Time.time,
            transcript = text,
            confidence = confidence
        };

        transcripts.Add(entry);
        Debug.Log($"[Player {playerId}] Transcript added: {text}");
    }

    /// <summary>
    /// Export audio data as JSON for logging.
    /// </summary>
    public string ExportAsJSON()
    {
        var export = new
        {
            playerId = playerId,
            transcriptCount = transcripts.Count,
            transcripts = transcripts
        };

        return JsonUtility.ToJson(new { audio = export }, prettyPrint: true);
    }

    /// <summary>
    /// Clear recorded transcripts.
    /// </summary>
    public void ClearTranscripts()
    {
        transcripts.Clear();
    }
}
