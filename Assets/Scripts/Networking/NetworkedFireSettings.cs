using UnityEngine;

/// <summary>
/// Build-persistent global settings for networked fire testing.
/// Select the NetworkedFireSettings asset in the Project window to edit the
/// single test checkbox used by every NetworkedFireState.
/// </summary>
public sealed class NetworkedFireSettings : ScriptableObject
{
    private static NetworkedFireSettings cachedInstance;

    [SerializeField]
    private bool allowSingleUserFireStartForTesting;

    public bool AllowSingleUserFireStartForTesting =>
        allowSingleUserFireStartForTesting;

    public static NetworkedFireSettings Instance
    {
        get
        {
            if (cachedInstance == null)
                cachedInstance = Resources.Load<NetworkedFireSettings>(nameof(NetworkedFireSettings));

            return cachedInstance;
        }
    }
}
