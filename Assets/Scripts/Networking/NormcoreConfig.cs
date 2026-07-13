using UnityEngine;

/// <summary>
/// Stores Normcore configuration and credentials.
/// Fill in your API key and project ID here.
/// </summary>
public class NormcoreConfig : MonoBehaviour
{
    [SerializeField] private string apiKey = "6734abe5-a4af-44b8-8a0f-ae2f7f48100c";
    [SerializeField] private string projectId = "inflectionpoints"; // Normcore project name
    
    public static NormcoreConfig Instance { get; private set; }

    public string ApiKey => apiKey;
    public string ProjectId => projectId;

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

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(projectId) || projectId == "YOUR_PROJECT_ID_HERE")
        {
            Debug.LogWarning("Normcore Project ID not set! Get it from your normcore.io dashboard and add it here.");
        }
    }
}
