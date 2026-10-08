// Adds manual Scene view preview controls to the window smoke plume Inspector.
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WindowSmokePlumeController))]
public class WindowSmokePlumeControllerEditor : Editor
{
    /// <summary>
    /// Draws the plume settings and starts or stops its temporary VFX preview
    /// through a button, matching Ignis's preview workflow.
    /// </summary>
    public override void OnInspectorGUI()
    {
        WindowSmokePlumeController plume = (WindowSmokePlumeController)target;

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
            plume.RefreshPreview();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(Application.isPlaying ||
                   !plume.isActiveAndEnabled ||
                   EditorUtility.IsPersistent(plume.gameObject) ||
                   plume.smokeEffect == null ||
                   plume.smokeEffect.visualEffectAsset == null))
        {
            string label = plume.IsPreviewing
                ? "Stop Previewing Window Smoke"
                : "Preview Window Smoke";
            if (GUILayout.Button(label))
            {
                if (plume.IsPreviewing)
                    plume.StopPreview();
                else
                    plume.StartPreview();
            }
        }

        if (plume.smokeEffect == null || plume.smokeEffect.visualEffectAsset == null)
            EditorGUILayout.HelpBox("Assign a Visual Effect with the WindowSmokePlume graph to preview the smoke.", MessageType.Info);
    }
}
