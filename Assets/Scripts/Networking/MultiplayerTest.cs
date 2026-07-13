using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Multiplayer test mode for testing without multiple headsets.
/// Spawn and control multiple test players with keyboard.
/// </summary>
public class MultiplayerTest : MonoBehaviour
{
    [SerializeField] private bool enableTestMode = true;
    [SerializeField] private GameObject playerPrefab;
    
    private int testPlayerCount = 0;
    private const int maxTestPlayers = 3;

    private void Update()
    {
        if (!enableTestMode) return;

        // Press 1, 2, 3 to spawn test players (new Input System)
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                SpawnTestPlayer();
            }
            
            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                SpawnTestPlayer();
            }
            
            if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                SpawnTestPlayer();
            }

            // Press R to reset
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ResetTestPlayers();
            }
        }
    }

    private void SpawnTestPlayer()
    {
        if (testPlayerCount >= maxTestPlayers)
        {
            Debug.LogWarning("Max test players reached!");
            return;
        }

        int testPlayerId = 5000 + testPlayerCount;
        Vector3 spawnPos = new Vector3(-2 + (testPlayerCount * 2), 0, 0);
        
        NormcoreSync.Instance.OnPlayerJoined(testPlayerId);
        
        testPlayerCount++;
        Debug.Log($"Test player spawned. Count: {testPlayerCount}");
    }

    private void ResetTestPlayers()
    {
        for (int i = 0; i < testPlayerCount; i++)
        {
            int testPlayerId = 5000 + i;
            NormcoreSync.Instance.OnPlayerLeft(testPlayerId);
        }
        testPlayerCount = 0;
        Debug.Log("Test players reset");
    }
}
