using UnityEngine;

/// <summary>
/// UI Manager for session info and debugging.
/// Displays current player count, session ID, and other stats.
/// </summary>
public class UIManager : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Text sessionInfoText;
    [SerializeField] private UnityEngine.UI.Text playerCountText;
    [SerializeField] private UnityEngine.UI.Button exitButton;

    private void Start()
    {
        if (exitButton != null)
        {
            exitButton.onClick.AddListener(ExitApplication);
        }

        UpdateDisplay();
    }

    private void Update()
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (DataLogger.Instance != null && sessionInfoText != null)
        {
            sessionInfoText.text = $"Session: {DataLogger.Instance.GetSessionId()}";
        }

        if (GameManager.Instance != null && playerCountText != null)
        {
            playerCountText.text = $"Players: {GameManager.Instance.GetConnectedPlayerCount()}/3";
        }
    }

    private void ExitApplication()
    {
        Debug.Log("Exiting application...");
        Application.Quit();
    }
}
