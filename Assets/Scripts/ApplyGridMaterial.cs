using UnityEngine;
using System.Collections.Generic;

public class ApplyGridMaterial : MonoBehaviour
{
    [Header("Drag your grid material here")]
    public Material gridMaterial;

    [Header("Filter Settings")]
    public bool furnitureOnlyMode = false;

    private bool lastFurnitureOnlyMode = false;
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    private void OnValidate()
    {
        if (furnitureOnlyMode != lastFurnitureOnlyMode)
        {
            lastFurnitureOnlyMode = furnitureOnlyMode;
            if (!furnitureOnlyMode && originalMaterials.Count > 0)
                RestoreAll();
        }
    }

    [ContextMenu("Apply Grid Material Now")]
    public void ApplyToAll()
    {
        if (gridMaterial == null)
        {
            Debug.LogWarning("No grid material assigned.");
            return;
        }

        if (originalMaterials.Count > 0)
            RestoreAll();

        Renderer[] allRenderers = FindObjectsOfType<Renderer>();

        foreach (Renderer r in allRenderers)
        {
            if (furnitureOnlyMode && !IsFurniture(r.gameObject))
                continue;

            originalMaterials[r] = r.materials;

            Material[] newMats = new Material[r.materials.Length];
            for (int i = 0; i < newMats.Length; i++)
                newMats[i] = gridMaterial;

            r.materials = newMats;
        }

        Debug.Log($"Applied grid material to {originalMaterials.Count} renderers.");
    }

    [ContextMenu("Restore Original Materials")]
    public void RestoreAll()
    {
        foreach (var kvp in originalMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.materials = kvp.Value;
        }

        Debug.Log("Restored original materials.");
        originalMaterials.Clear();
    }

    private bool IsFurniture(GameObject obj)
    {
        Transform current = obj.transform;
        while (current != null)
        {
            if (current.CompareTag("Furniture"))
                return true;
            current = current.parent;
        }
        return false;
    }
}