using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HazardTagging : MonoBehaviour
{
    public enum TaggingMode { AutoTagging, ManualTagging }

    [Header("Configuration")]
    public TaggingMode mode = TaggingMode.AutoTagging;

    [Header("Flammable Objects List")]
    [Tooltip("Any prefab/object containing these keywords in their name will flag a room as a potential hazard.")]
    public List<string> flammableKeywords = new List<string> { "Electric Scooter", "Small Table", "Oven", "Fridge", "Toaster" };

    // Scans the generated house and automatically applies 'Possible Hazard' tags based on room type and contents.
    public void RunAutoTagging()
    {
        RoomGeneration gen = GetComponent<RoomGeneration>();
        if (gen == null || gen.allGeneratedRooms == null || gen.allGeneratedRooms.Count == 0)
        {
            Debug.LogWarning("HazardTagging: No generated rooms found to auto-tag.");
            return;
        }

        int taggedCount = 0;

        foreach (var room in gen.allGeneratedRooms)
        {
            if (room.RoomObject == null) continue;

            // Reset tag to Untagged first to clear previous runs
            if (room.RoomObject.tag == "Possible Hazard" || room.RoomObject.tag == "Active Hazard")
            {
                room.RoomObject.tag = "Untagged";
            }

            bool isPotentialHazard = false;

            // Check 1: Is it a Kitchen?
            if (room.RoomType == "Kitchen")
            {
                Debug.Log("Found a kitchen");
                isPotentialHazard = true;
            }
            // Check 2: Does it contain a flammable item?
            else
            {
                Transform[] allChildren = room.RoomObject.GetComponentsInChildren<Transform>();
                foreach (var child in allChildren)
                {
                    if (ContainsFlammableKeyword(child.name))
                    {
                        isPotentialHazard = true;
                        break; // No need to check further items in this room
                    }
                }
            }

            // Apply tag if conditions are met
            if (isPotentialHazard)
            {
                //Debug.Log("Should be setting a hazard");
                room.RoomObject.tag = "Possible Hazard";
                taggedCount++;

#if UNITY_EDITOR
                EditorUtility.SetDirty(room.RoomObject); // Ensure Unity registers the change in-editor
#endif
            }
        }

        Debug.Log($"HazardTagging: Auto-tagging complete. Found {taggedCount} 'Possible Hazard' rooms.");
    }

    // Selects 1 or 2 rooms currently marked 'Possible Hazard' and escalates them to 'Active Hazard'.
    public void AssignActiveHazards()
    {
        // Find all rooms currently tagged as 'Possible Hazard' anywhere under this generator
        List<GameObject> possibleHazardRooms = new List<GameObject>();

        // We scan children of this GameObject to avoid pulling random objects from the rest of the scene
        Transform[] allChildren = GetComponentsInChildren<Transform>();
        foreach (var child in allChildren)
        {
            // Clear out any stale Active Hazards from a previous button press
            if (child.gameObject.tag == "Active Hazard")
            {
                child.gameObject.tag = "Possible Hazard";
            }

            if (child.gameObject.tag == "Possible Hazard")
            {
                possibleHazardRooms.Add(child.gameObject);
            }
        }

        if (possibleHazardRooms.Count == 0)
        {
            Debug.LogWarning("HazardTagging: No rooms with 'Possible Hazard' tag found. Cannot assign active hazards.");
            return;
        }

        // Determine how many hazards to activate (1 or 2, capped by total possible available)
        int targetActiveCount = Random.Range(1, 3);
        targetActiveCount = Mathf.Min(targetActiveCount, possibleHazardRooms.Count);

        // Randomly pick unique rooms from our list
        for (int i = 0; i < targetActiveCount; i++)
        {
            int randomIndex = Random.Range(0, possibleHazardRooms.Count);
            GameObject chosenRoom = possibleHazardRooms[randomIndex];

            chosenRoom.tag = "Active Hazard";
            possibleHazardRooms.RemoveAt(randomIndex); // Prevent picking the same room twice

            Debug.Log($"HazardTagging: {chosenRoom.name} ({chosenRoom.gameObject.layer}) has been activated as an ACTIVE HAZARD!");

#if UNITY_EDITOR
            EditorUtility.SetDirty(chosenRoom);
#endif
        }
    }

    private bool ContainsFlammableKeyword(string objectName)
    {
        foreach (var keyword in flammableKeywords)
        {
            if (objectName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }
}

// Custom Inspector layout to draw buttons in the Editor without needing Play mode
#if UNITY_EDITOR
[CustomEditor(typeof(HazardTagging))]
public class HazardTaggingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // Draw standard variables

        HazardTagging script = (HazardTagging)target;
        GUILayout.Space(15);

        if (script.mode == HazardTagging.TaggingMode.AutoTagging)
        {
            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
            if (GUILayout.Button("1. Run Auto-Tagging Scan", GUILayout.Height(30)))
            {
                script.RunAutoTagging();
            }
            GUILayout.Space(5);
        }
        else
        {
            EditorGUILayout.HelpBox("Manual Tagging Mode Active: Switch your desired Room GameObjects' tags to 'Possible Hazard' in the hierarchy manually before proceeding.", MessageType.Info);
            GUILayout.Space(5);
        }

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("2. Assign Active Hazards", GUILayout.Height(35)))
        {
            script.AssignActiveHazards();
            // Mark the scene dirty so Unity knows changes were made pre-start and saves them
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
#endif