using UnityEngine;

/// <summary>
/// Player prefab setup script.
/// Add this to your player prefab to ensure it's properly configured.
/// </summary>
public class PlayerSetup : MonoBehaviour
{
    private void Awake()
    {
        // Ensure components are present
        if (GetComponent<PlayerController>() == null)
        {
            gameObject.AddComponent<PlayerController>();
        }

        if (GetComponent<PlayerMovementTracker>() == null)
        {
            gameObject.AddComponent<PlayerMovementTracker>();
        }

        if (GetComponent<AudioTranscriptLogger>() == null)
        {
            gameObject.AddComponent<AudioTranscriptLogger>();
        }

        if (GetComponent<CharacterController>() == null)
        {
            gameObject.AddComponent<CharacterController>();
        }

        if (GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }
}
