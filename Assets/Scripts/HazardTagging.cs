using System.Collections.Generic;
using UnityEngine;
using Ignis;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HazardTagging : MonoBehaviour
{
    public enum TaggingMode { AutoTagging, ManualTagging }

    [Header("Configuration")]
    public TaggingMode mode = TaggingMode.AutoTagging;

    [Tooltip("Drag the parent room object here. All children will be evaluated.")]
    public GameObject hazardRoom;

    [Header("Hazard Tracking")]
    [Tooltip("Tracks all specific objects that could potentially be hazards.")]
    public List<GameObject> possibleHazards = new List<GameObject>();

    [Tooltip("Tracks the specific objects that have been assigned as active hazards.")]
    public List<GameObject> activeHazards = new List<GameObject>();

    // Called by the hazard controller when an object is touched / made active
    public void CheckHazardStatus(Dictionary<GameObject, Material> hazards)
    {
        foreach (var pair in hazards)
        {
            if (pair.Key.CompareTag("Active Hazard"))
            {
                if (possibleHazards.Contains(pair.Key)) possibleHazards.Remove(pair.Key);
                if (!activeHazards.Contains(pair.Key)) activeHazards.Add(pair.Key);
            }
            else if (pair.Key.CompareTag("Possible Hazard"))
            {
                if (!possibleHazards.Contains(pair.Key)) possibleHazards.Add(pair.Key);
                if (activeHazards.Contains(pair.Key)) activeHazards.Remove(pair.Key);
            }
        }
    }

    // Scans the hazardRoom and automatically applies 'Possible Hazard' tags to all children
    public void RunAutoTagging()
    {
        if (hazardRoom == null)
        {
            Debug.LogWarning("HazardTagging: Please assign a Hazard Room!");
            return;
        }

#if UNITY_EDITOR
        Undo.RecordObject(this, "Run Auto-Tagging");
#endif

        ClearHazards(); // Clean up previous runs

        // Tag all descendants of the hazard room
        Transform[] allChildren = hazardRoom.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            if (child.gameObject == hazardRoom) continue; // Skip the parent room itself

            // Skip empty container objects based on their name prefix
            if (child.name.StartsWith("Floor_") || child.name.StartsWith("Room_") || child.name.StartsWith("Teleport Area") || child.name.StartsWith("Hitbox"))
                continue;

            child.gameObject.tag = "Possible Hazard";
            possibleHazards.Add(child.gameObject);

#if UNITY_EDITOR
            EditorUtility.SetDirty(child.gameObject);
#endif
        }

        // Apply fire profiles to all newly auto-tagged hazards
        AddFireProfiles(possibleHazards);

        Debug.Log($"HazardTagging: Auto-tagged {possibleHazards.Count} objects as Possible Hazards.");
    }

    // Selects 1 to 3 objects currently marked 'Possible Hazard' and escalates them to 'Active Hazard'
    public void AssignActiveHazards()
    {
        if (hazardRoom == null) return;

#if UNITY_EDITOR
        Undo.RecordObject(this, "Assign Active Hazards");
#endif

        if (possibleHazards.Count == 0 && activeHazards.Count == 0)
        {
            Debug.LogWarning("HazardTagging: No objects tagged as 'Possible Hazard' found to upgrade, and no manual Active Hazards found.");
            return;
        }

        // If in manual mode, ensure our lists are up to date with what the user tagged in the editor
        if (mode == TaggingMode.ManualTagging)
        {
            ScanManualTags();

            // If there were no active hazards manually tagged, pick a few, otherwise, only use the manual ones
            if (activeHazards.Count == 0)
                PickRandomHazards();
        }
        else // in auto-tagging mode, we leave it entirely up to fate
        {
            // Pick 1-3 random hazards to assign as active
            PickRandomHazards();
        }

        // Guarantee that ALL active hazards (randomized OR manually tagged) are set to ignite on start
        foreach (GameObject activeObj in activeHazards)
        {
            FireProfileController fireProfile = activeObj.GetComponent<FireProfileController>();
            if (fireProfile != null && fireProfile.flammableObject != null)
            {
                fireProfile.flammableObject.setThisOnFireOnStart = true;
#if UNITY_EDITOR
                EditorUtility.SetDirty(fireProfile.flammableObject);
#endif
            }
        }
    }

    private void PickRandomHazards()
    {
        // Pick 1 to 3 items
        // Only pick new active hazards if we have possible hazards to pick from
        if (possibleHazards.Count > 0)
        {
            int itemsToActivate = Random.Range(1, 4); // Exclusive max, so 4 = 1, 2, or 3
            itemsToActivate = Mathf.Min(itemsToActivate, possibleHazards.Count);

            for (int i = 0; i < itemsToActivate; i++)
            {
                int randomIndex = Random.Range(0, possibleHazards.Count);
                GameObject chosenItem = possibleHazards[randomIndex];

                // Escalate
                chosenItem.tag = "Active Hazard";
                activeHazards.Add(chosenItem);
                possibleHazards.RemoveAt(randomIndex);

                Debug.Log($"HazardTagging: {chosenItem.name} has been activated as an ACTIVE HAZARD!");

#if UNITY_EDITOR
                EditorUtility.SetDirty(chosenItem);
#endif
            }
        }
    }

    // Demotes all current Active Hazards back to Possible Hazards
    public void DemoteActiveHazards()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Demote Active Hazards");
#endif
        // Copy the list so we can safely iterate through it while modifying the original list
        List<GameObject> hazardsToDemote = new List<GameObject>(activeHazards);

        foreach (GameObject obj in hazardsToDemote)
        {
            if (obj == null) continue;

            // Demote the tag
            obj.tag = "Possible Hazard";

            // Move between lists
            possibleHazards.Add(obj);
            activeHazards.Remove(obj);

            // Turn off setThisOnFireOnStart
            FireProfileController fireProfile = obj.GetComponent<FireProfileController>();
            if (fireProfile != null && fireProfile.flammableObject != null)
            {
                fireProfile.flammableObject.setThisOnFireOnStart = false;
#if UNITY_EDITOR
                EditorUtility.SetDirty(fireProfile.flammableObject); // Ensure Editor sees the component change
#endif
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(obj); // Ensure Editor sees the tag change
#endif
        }

        Debug.Log($"HazardTagging: Demoted {hazardsToDemote.Count} Active Hazards back to Possible Hazards.");
    }

    // Wipes all tags clean and clears lists
    public void ClearHazards()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Clear Hazards");
#endif

        // Remove the fire profiles and their dependencies before we clear the lists
        // Helps us reset which are the fire sources + which are flammable
        RemoveFireProfiles(possibleHazards);
        RemoveFireProfiles(activeHazards);

        foreach (GameObject obj in possibleHazards)
        {
            if (obj != null) obj.tag = "Untagged";
        }
        foreach (GameObject obj in activeHazards)
        {
            if (obj != null)
            {
                obj.tag = "Untagged";
            }
        }

        possibleHazards.Clear();
        activeHazards.Clear();
    }

    // Reads the hierarchy and populates the lists based on existing manual tags
    public void ScanManualTags()
    {
#if UNITY_EDITOR
        Undo.RecordObject(this, "Scan Manual Tags");
#endif
        possibleHazards.Clear();
        activeHazards.Clear();

        if (hazardRoom == null) return;

        Transform[] allChildren = hazardRoom.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            if (child.gameObject.CompareTag("Possible Hazard"))
            {
                possibleHazards.Add(child.gameObject);
            }
            else if (child.gameObject.CompareTag("Active Hazard"))
            {
                activeHazards.Add(child.gameObject);
            }
        }

        // Assign the fire controllers to make all hazards (both possible and active) flammable
        AddFireProfiles(possibleHazards);
        AddFireProfiles(activeHazards);
    }

    private void AddFireProfiles(List<GameObject> currentHazards)
    {
        foreach (GameObject child in currentHazards)
        {
            if (child.GetComponent<FireProfileController>() == null)
            {
                child.AddComponent<FireProfileController>();
            }
        }
    }

    // Thoroughly resets and removes all fire profiles, their required components, and generated children
    private void RemoveFireProfiles(List<GameObject> objectsList)
    {
        foreach (GameObject child in objectsList)
        {
            if (child == null) continue;

            // Remove the Networked fire state
            NetworkedFireState fireState = child.GetComponent<NetworkedFireState>();
            if (fireState != null)
            {
                DestroyImmediate(fireState);
            }

            // Remove the Controller
            FireProfileController fireProfile = child.GetComponent<FireProfileController>();
            if (fireProfile != null)
            {
                DestroyImmediate(fireProfile);
            }

            // Remove the FlammableObject dependency that was forced by [RequireComponent]
            FlammableObject flammableObj = child.GetComponent<FlammableObject>();
            if (flammableObj != null)
            {
                DestroyImmediate(flammableObj);
            }

            // Remove the Hitbox child that was generated by SetupHitbox()
            Transform hitbox = child.transform.Find("Hitbox");
            if (hitbox != null)
            {
                DestroyImmediate(hitbox.gameObject);
            }

            // Remove the BoxCollider that we created
            BoxCollider collider = child.GetComponent<BoxCollider>();
            if (collider != null)
            {
                DestroyImmediate(collider);
            }
        }
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
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            GUILayout.Space(5);
        }
        else
        {
            EditorGUILayout.HelpBox("Manual Tagging Mode: Manually tag objects as 'Possible Hazard' or 'Active Hazard' in the hierarchy, then scan.", MessageType.Info);

            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.3f);
            if (GUILayout.Button("1. Scan Manual Tags", GUILayout.Height(30)))
            {
                script.ScanManualTags();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            GUILayout.Space(5);
        }

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("2. Assign Active Hazards", GUILayout.Height(35)))
        {
            script.AssignActiveHazards();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        GUILayout.Space(5);

        GUI.backgroundColor = new Color(1.0f, 0.6f, 0.2f);
        if (GUILayout.Button("3. Demote Active Hazards", GUILayout.Height(35)))
        {
            script.DemoteActiveHazards();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        GUILayout.Space(5);

        GUI.backgroundColor = new Color(0.3f, 1.0f, 0.3f);
        if (GUILayout.Button("4. Clear All Hazard Tags", GUILayout.Height(35)))
        {
            script.ClearHazards();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
#endif