using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RoomGeneration))]
public class RoomGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RoomGeneration generator = (RoomGeneration)target;
        GUILayout.Space(10);

        EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying);
        if (GUILayout.Button("Generate Rooms", GUILayout.Height(40)))
        {
            generator.GenerateAllRooms();
        }
        EditorGUI.EndDisabledGroup();
    }
}