using UnityEditor;
using UnityEngine;

/// <summary>
/// Per-machine Editor toggle for <see cref="MonitorMode"/>. Stored in EditorPrefs,
/// so it is never committed and never affects teammates' VR sessions.
/// </summary>
public static class MonitorModeMenu
{
    private const string MenuPath = "Tools/Monitor Mode (Desktop Observer)";

    [MenuItem(MenuPath, priority = 0)]
    private static void Toggle()
    {
        bool enabled = !EditorPrefs.GetBool(MonitorMode.EditorPrefKey, false);
        EditorPrefs.SetBool(MonitorMode.EditorPrefKey, enabled);
        Menu.SetChecked(MenuPath, enabled);

        string state = enabled ? "ON: Play will join the room as a bird's-eye observer." : "OFF: Play runs as a normal VR client.";
        if (EditorApplication.isPlaying)
            state += " (takes effect the next time you enter Play mode)";
        Debug.Log($"[MonitorMode] {state}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(MonitorMode.EditorPrefKey, false));
        return true;
    }
}
