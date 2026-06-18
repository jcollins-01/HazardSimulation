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

    [Header("Hazard Tracking")]
    [Tooltip("Tracks all specific objects that could potentially be hazards.")]
    public List<GameObject> possibleHazards = new List<GameObject>();

    [Tooltip("Tracks the specific objects that have been assigned as active hazards.")]
    public List<GameObject> activeHazards = new List<GameObject>();

    // Scans the generated house and automatically applies 'Possible Hazard' tags based on room type and contents.
    public void RunAutoTagging()
    {
        RoomGeneration gen = GetComponent<RoomGeneration>();
        if (gen == null || gen.allGeneratedRooms == null || gen.allGeneratedRooms.Count == 0)
        {
            Debug.LogWarning("HazardTagging: No generated rooms found to auto-tag.");
            return;
        }

#if UNITY_EDITOR
        Undo.RecordObject(this, "Run Auto-Tagging"); // Record list changes for Undo
#endif

        // Clean up previous runs (untags objects and clears lists)
        ClearTrackedHazards();

        int taggedZoneCount = 0;

        foreach (var room in gen.allGeneratedRooms)
        {
            if (room.RoomObject == null) continue;

            // Reset tag to Untagged first to clear previous runs
            if (room.RoomObject.tag == "Possible Hazard" || room.RoomObject.tag == "Active Hazard")
            {
                room.RoomObject.tag = "Untagged";
            }

            List<GameObject> flammableItemsInRoom = GetFlammableObjectsInZone(room.RoomObject.transform);

            bool isPotentialHazardZone = false;

            // Check 1: Is it a Kitchen?
            if (room.RoomType == "Kitchen")
            {
                Debug.Log("Found a kitchen");
                isPotentialHazardZone = true;
            }
            // Check 2: Does it contain flammable items?
            else if (flammableItemsInRoom.Count > 0)
            {
                isPotentialHazardZone = true;
            }

            // Apply tags and populate arrays if conditions are met
            if (isPotentialHazardZone)
            {
                room.RoomObject.tag = "Possible Hazard";
                taggedZoneCount++;

                // Add all the specific flammable items to our possible hazards tracking list
                foreach (GameObject item in flammableItemsInRoom)
                {
                    item.tag = "Possible Hazard";
                    possibleHazards.Add(item);

#if UNITY_EDITOR
                    EditorUtility.SetDirty(item);
#endif
                }

#if UNITY_EDITOR
                EditorUtility.SetDirty(room.RoomObject);
#endif
            }
        }

        Debug.Log($"HazardTagging: Auto-tagging complete. Found {taggedZoneCount} 'Possible Hazard' rooms.");
    }

    // Selects 1 or 2 objects currently marked 'Possible Hazard' and escalates them to 'Active Hazard'.
    public void AssignActiveHazards()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Assign Active Hazards");
#endif

        // Handle assignment based on the selected Mode
        Transform[] allChildren = GetComponentsInChildren<Transform>();

        if (mode == TaggingMode.AutoTagging)
        {
            // --- AUTO MODE LOGIC (Zones first, then items inside) ---
            // Only clear the active list and demote current active hazards if we're in active mode
            foreach (GameObject obj in activeHazards)
            {
                if (obj != null) obj.tag = "Possible Hazard"; // Demote back to possible
            }
            activeHazards.Clear();

            List<GameObject> possibleHazardZones = new List<GameObject>();

            foreach (var child in allChildren)
            {
                // In auto mode, we look for the zones we tagged
                if (child.gameObject.tag == "Possible Hazard" && child.GetComponent<RoomGeneration>() == null) // Basic check to ensure it's a zone
                {
                    possibleHazardZones.Add(child.gameObject);
                }
            }

            if (possibleHazardZones.Count == 0)
            {
                Debug.LogWarning("HazardTagging: No zones with 'Possible Hazard' tag found.");
                return;
            }

            int targetZoneCount = Random.Range(1, 3);
            targetZoneCount = Mathf.Min(targetZoneCount, possibleHazardZones.Count);

            for (int i = 0; i < targetZoneCount; i++)
            {
                int randomZoneIndex = Random.Range(0, possibleHazardZones.Count);
                GameObject chosenZone = possibleHazardZones[randomZoneIndex];
                chosenZone.tag = "Active Hazard";
                possibleHazardZones.RemoveAt(randomZoneIndex);

                List<GameObject> itemsInZone = GetFlammableObjectsInZone(chosenZone.transform);

                if (itemsInZone.Count > 0)
                {
                    int itemsToActivate = Random.Range(1, 3);
                    itemsToActivate = Mathf.Min(itemsToActivate, itemsInZone.Count);

                    for (int j = 0; j < itemsToActivate; j++)
                    {
                        int randomItemIndex = Random.Range(0, itemsInZone.Count);
                        GameObject chosenItem = itemsInZone[randomItemIndex];

                        chosenItem.tag = "Active Hazard";
                        activeHazards.Add(chosenItem);
                        itemsInZone.RemoveAt(randomItemIndex);

                        Debug.Log($"HazardTagging: {chosenItem.name} in zone {chosenZone.name} is now ACTIVE!");
#if UNITY_EDITOR
                        EditorUtility.SetDirty(chosenItem);
#endif
                    }
                }
#if UNITY_EDITOR
                EditorUtility.SetDirty(chosenZone);
#endif
            }
        }
        else
        {
            // --- MANUAL MODE LOGIC (Directly target the tagged objects) ---

            // Clear the tracking array so we can rebuild it purely from the manual tags
            possibleHazards.Clear();
            activeHazards.Clear();

            List<GameObject> manuallyTaggedItems = new List<GameObject>();

            // Find everything the user manually tagged and rebuild the possibleHazards list
            foreach (var child in allChildren)
            {
                if (child.gameObject.tag == "Possible Hazard")
                {
                    manuallyTaggedItems.Add(child.gameObject);
                    possibleHazards.Add(child.gameObject); // Track it for the inspector!
#if UNITY_EDITOR
                    EditorUtility.SetDirty(child.gameObject);
#endif
                }
                else if (child.gameObject.CompareTag("Active Hazard"))
                {
                    manuallyTaggedItems.Add(child.gameObject);
                    activeHazards.Add(child.gameObject);
#if UNITY_EDITOR
                    EditorUtility.SetDirty(child.gameObject);
#endif
                }
            }

            if (manuallyTaggedItems.Count == 0)
            {
                Debug.LogWarning("HazardTagging: You have no objects manually tagged as 'Possible Hazard'.");
                return;
            }

            // Pick 1 or 2 items directly
            int itemsToActivate = Random.Range(1, 3);
            itemsToActivate = Mathf.Min(itemsToActivate, manuallyTaggedItems.Count);

            for (int i = 0; i < itemsToActivate; i++)
            {
                int randomItemIndex = Random.Range(0, manuallyTaggedItems.Count);
                GameObject chosenItem = manuallyTaggedItems[randomItemIndex];

                // Escalate
                chosenItem.tag = "Active Hazard";
                activeHazards.Add(chosenItem);
                manuallyTaggedItems.RemoveAt(randomItemIndex);

                Debug.Log($"HazardTagging (Manual): {chosenItem.name} has been activated as an ACTIVE HAZARD!");

#if UNITY_EDITOR
                EditorUtility.SetDirty(chosenItem);
#endif
            }
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

    private List<GameObject> GetFlammableObjectsInZone(Transform zoneRoot)
    {
        List<GameObject> foundObjects = new List<GameObject>();
        Transform[] allChildren = zoneRoot.GetComponentsInChildren<Transform>();

        foreach (var child in allChildren)
        {
            if (ContainsFlammableKeyword(child.name))
            {
                foundObjects.Add(child.gameObject);
            }
        }
        return foundObjects;
    }

    // Wipes arrays clean. Called only by Auto-Tagging to reset the simulation state.
    private void ClearTrackedHazards()
    {
        foreach (GameObject obj in possibleHazards)
        {
            if (obj != null) obj.tag = "Untagged";
        }
        foreach (GameObject obj in activeHazards)
        {
            if (obj != null) obj.tag = "Untagged";
        }

        possibleHazards.Clear();
        activeHazards.Clear();
    }
}

// Custom Inspector layout
#if UNITY_EDITOR
[CustomEditor(typeof(HazardTagging))]
public class HazardTaggingEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

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
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
#endif