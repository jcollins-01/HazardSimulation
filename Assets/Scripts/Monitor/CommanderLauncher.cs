using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Entry point of the Commander scene. Pick the scenario the VR users are in and the
/// commander joins its Normcore room seated in the fire truck (<see cref="CommanderCab"/>):
/// no avatar, XR off, never owns any fire, hears every VR user.
///
/// Every scenario joins the same room and shares most fire IDs, so choosing a different
/// scenario than the VR users would silently show the wrong fires; the menu therefore
/// always asks unless <see cref="autoJoinScenario"/> is set.
/// </summary>
public sealed class CommanderLauncher : MonoBehaviour
{
    [SerializeField] private CommanderCab cab;

    [Tooltip("Scenario scenes the commander can join (project-relative paths).")]
    [SerializeField] private string[] scenarios =
    {
        "Assets/SCENARIOS/Scenario 1 (controlled; 1 story; daytime) 1.unity",
        "Assets/SCENARIOS/Scenario 2 (controlled; 2 story; daytime) 1.unity",
        "Assets/SCENARIOS/Scenario 3 (controlled;1 story nighttime) 1.unity",
        "Assets/SCENARIOS/Smoke-VFX-Test/Scenario1-ImprovedSmoke_Test0.unity",
        "Assets/SCENARIOS/Non-Controlled/Scenario 1 (1 story; daytime).unity",
        "Assets/SCENARIOS/Non-Controlled/Scenario 2 (2 story; daytime).unity",
        "Assets/SCENARIOS/Non-Controlled/Scenario 3 (1 story nighttime).unity",
    };

    [Tooltip("Join this scenario immediately instead of showing the menu. Leave empty to choose on Play.")]
    [SerializeField] private string autoJoinScenario = string.Empty;

    private bool joining;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;

    private void Awake()
    {
        // Switch XR off right away so the menu and the cab preview render to the Game view.
        MonitorMode.BeginCommander(cab);
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(autoJoinScenario))
            Join(autoJoinScenario);
    }

    /// <summary>Loads <paramref name="scenePath"/> with the commander seated in its truck.</summary>
    public void Join(string scenePath)
    {
        if (joining)
            return;
        joining = true;
        DontDestroyOnLoad(cab.gameObject);
#if UNITY_EDITOR
        // Works in Play mode without adding every scenario to the Build Settings.
        EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scenePath);
#endif
    }

    private void OnGUI()
    {
        if (joining)
            return;

        titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
        buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 6, 6) };

        const float rowHeight = 38f;
        float width = Mathf.Min(560f, Screen.width - 40f);
        float height = 96f + scenarios.Length * (rowHeight + 4f);
        Rect area = new((Screen.width - width) * 0.5f, Mathf.Max(20f, (Screen.height - height) * 0.5f), width, height);

        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 12f, area.width - 32f, area.height - 24f));
        GUILayout.Label("COMMANDER VIEW", titleStyle);
        GUILayout.Label("Choose the scenario the VR users are in:");
        GUILayout.Space(6f);
        foreach (string scenePath in scenarios)
        {
            bool available = IsLoadable(scenePath);
            GUI.enabled = available;
            string label = DisplayName(scenePath) + (available ? string.Empty : "   (not in Build Settings)");
            if (GUILayout.Button(label, buttonStyle, GUILayout.Height(rowHeight)))
                Join(scenePath);
            GUI.enabled = true;
        }
        GUILayout.EndArea();
    }

    // Scenes in sub-folders (Non-Controlled, Smoke-VFX-Test) share names with the main
    // scenarios, so show the folder too.
    private static string DisplayName(string scenePath)
    {
        string name = Path.GetFileNameWithoutExtension(scenePath);
        string folder = Path.GetFileName(Path.GetDirectoryName(scenePath));
        return folder == "SCENARIOS" ? name : $"{folder} / {name}";
    }

    private static bool IsLoadable(string scenePath)
    {
#if UNITY_EDITOR
        return File.Exists(scenePath);
#else
        return SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0;
#endif
    }
}
