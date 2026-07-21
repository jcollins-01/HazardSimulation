using UnityEngine;
using Ignis;

public class HazardTemperature : MonoBehaviour
{
    private FireProfileController fireController;
    private FlammableObject flammableObject;

    [Range(0f, 1f)] public float temperature = 0.0f;

    private Renderer[] thermalRenderers;
    private MaterialPropertyBlock propBlock;
    private ThermalHeatEffects heatEffects;

    public float NormalizedTemperature => temperature;

    public float heatUpSpeed = 0.5f; // Controls how fast it turns red (higher = faster)
    public float coolDownSpeed = 0.5f; // Track dynamic cooling as the user sprays

    void Start()
    {
        fireController = GetComponent<FireProfileController>();
        flammableObject = GetComponent<FlammableObject>();
        thermalRenderers = GetComponentsInChildren<Renderer>(true);
        propBlock = new MaterialPropertyBlock();

        heatEffects = GetComponent<ThermalHeatEffects>();
        if (heatEffects == null)
            heatEffects = gameObject.AddComponent<ThermalHeatEffects>();
    }

    void Update()
    {
        if (fireController != null)
        {
            // Each profile has a different maximum. A fully burning fire should
            // always reach the hottest TIC color, regardless of its profile.
            float maximum = Mathf.Max(1f, fireController.maxTemperature);
            temperature = Mathf.Clamp01(fireController.currentTemperature / maximum);
        }
        else if (flammableObject != null)
        {
            float targetTemperature = GetFlammableTemperature();
            float transitionSpeed = targetTemperature > temperature ? heatUpSpeed : coolDownSpeed;
            temperature = Mathf.MoveTowards(temperature, targetTemperature, transitionSpeed * Time.deltaTime);
        }

        ApplyTemperatureToRenderer();
    }

    private float GetFlammableTemperature()
    {
        if (!flammableObject.onFire)
            return 0f;

        float brightnessTime = Mathf.Max(0.1f, flammableObject.achieveMaxBrightness_s);
        float ignitionHeat = Mathf.Lerp(0.72f, 1f,
            Mathf.Clamp01(flammableObject.onFireTimer / brightnessTime));

        if (flammableObject.onFireTimer <= flammableObject.burnOutStart_s)
            return ignitionHeat;

        float burnoutLength = Mathf.Max(0.1f, flammableObject.burnOutLength_s);
        float burnoutProgress = Mathf.Clamp01(
            (flammableObject.onFireTimer - flammableObject.burnOutStart_s) / burnoutLength);
        return Mathf.Lerp(ignitionHeat, 0.35f, burnoutProgress);
    }

    private void ApplyTemperatureToRenderer()
    {
        if (thermalRenderers == null || thermalRenderers.Length == 0)
            return;

        foreach (Renderer thermalRenderer in thermalRenderers)
        {
            if (thermalRenderer == null)
                continue;

            thermalRenderer.GetPropertyBlock(propBlock);
            propBlock.SetFloat("_Temperature", temperature);
            thermalRenderer.SetPropertyBlock(propBlock);
        }
    }

    // Call this when the simulation is ready for a full reset
    public void ResetTemperature()
    {
        temperature = 0.0f;

        // Force an immediate update to the renderer so it snaps to blue instantly
        if (thermalRenderers == null || thermalRenderers.Length == 0)
            thermalRenderers = GetComponentsInChildren<Renderer>(true);
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        ApplyTemperatureToRenderer();

        if (heatEffects == null)
            heatEffects = GetComponent<ThermalHeatEffects>();
        heatEffects?.ResetLingeringHeat();
    }
}
