using Ignis;
using UnityEngine;

// OAVA can ignite any FlammableObject at runtime. This registry makes sure
// newly burning objects participate in the TIC even when they were not one of
// the preconfigured hazard profiles in the scene.
public sealed class ThermalFireRegistry : MonoBehaviour
{
    private const float ScanInterval = 0.5f;
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRegistry()
    {
        if (FindFirstObjectByType<ThermalFireRegistry>() != null)
            return;

        GameObject registryObject = new GameObject("Thermal Fire Registry");
        DontDestroyOnLoad(registryObject);
        registryObject.AddComponent<ThermalFireRegistry>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime)
            return;

        nextScanTime = Time.unscaledTime + ScanInterval;
        FlammableObject[] flammableObjects = FindObjectsByType<FlammableObject>(FindObjectsSortMode.None);

        foreach (FlammableObject flammableObject in flammableObjects)
        {
            if (!flammableObject.onFire || flammableObject.GetComponent<HazardTemperature>() != null)
                continue;

            flammableObject.gameObject.AddComponent<HazardTemperature>();
        }
    }
}
