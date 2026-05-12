using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public static class DecorativeDemoFloorTeleportBootstrap
{
    private const string TargetSceneName = "DecorativeDemo";
    private const string TargetFloorName = "Floors";
    private const string TeleportChildName = "Teleport Area Invisible";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConfigureDecorativeDemoFloors()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != TargetSceneName)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            ConfigureChildFloorsRecursive(root.transform);
        }
    }

    private static void ConfigureChildFloorsRecursive(Transform current)
    {
        if (current.name == TargetFloorName)
        {
            ConfigureFloorTeleportArea(current.gameObject);
        }

        foreach (Transform child in current)
        {
            ConfigureChildFloorsRecursive(child);
        }
    }

    private static void ConfigureFloorTeleportArea(GameObject floor)
    {
        if (floor == null)
            return;

        Collider floorCollider = floor.GetComponent<Collider>();
        if (floorCollider == null)
            return;

        Transform teleportChild = floor.transform.Find(TeleportChildName);
        GameObject teleportAreaObject;

        if (teleportChild != null)
        {
            teleportAreaObject = teleportChild.gameObject;
        }
        else
        {
            GameObject teleportPrefab = Resources.Load<GameObject>("Locomotion/Teleport Area Invisible");
            if (teleportPrefab == null)
            {
                Debug.LogWarning("Prefab not found at Resources/Locomotion/Teleport Area Invisible.prefab");
                return;
            }

            teleportAreaObject = Object.Instantiate(teleportPrefab, floor.transform);
            teleportAreaObject.name = TeleportChildName;
        }

        teleportAreaObject.transform.SetParent(floor.transform, false);
        teleportAreaObject.transform.localPosition = Vector3.zero;
        teleportAreaObject.transform.localRotation = Quaternion.identity;
        teleportAreaObject.transform.localScale = Vector3.one;

        foreach (Collider col in teleportAreaObject.GetComponentsInChildren<Collider>(true))
        {
            col.enabled = false;
            Object.Destroy(col);
        }

        TeleportationArea teleportationArea = teleportAreaObject.GetComponent<TeleportationArea>();
        if (teleportationArea == null)
            return;

        teleportationArea.colliders.Clear();
        teleportationArea.colliders.Add(floorCollider);
        teleportationArea.interactionLayers = InteractionLayerMask.GetMask("Teleport");
    }
}
