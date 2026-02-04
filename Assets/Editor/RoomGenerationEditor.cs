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

        if (GUILayout.Button("Generate Rooms", GUILayout.Height(40)))
        {
            generator.GenerateAllRooms();
        }
    }
}