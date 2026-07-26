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

    [HideInInspector] public float heatUpSpeed = 0.5f; // Retained for existing serialized scenes.
    public float coolDownSpeed = 0.5f; // Track dynamic cooling as the user sprays

    [Header("Temperature Response")]
    [SerializeField, Min(0f)] private float heatUpDelay = 0.5f;
    [SerializeField, Min(0.1f)] private float heatUpDuration = 10f;

    [Header("Heat Spread")]
    [SerializeField, Min(0.1f)] private float heatSpreadDuration = 8f;
    [SerializeField, Min(1f)] private float fullHeatRadiusMultiplier = 12f;

    private Vector3 heatOriginWorld;
    private Vector3 heatOriginLocal;
    private float maximumHeatDistance = 1f;
    private float heatSpreadProgress;
    private float timeSinceIgnition;
    private bool wasOnFire;
    private bool isIgnited;

    private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");
    private static readonly int HeatOriginId = Shader.PropertyToID("_HeatOrigin");
    private static readonly int HeatRadiusId = Shader.PropertyToID("_HeatRadius");
    private static readonly int LocalizedHeatId = Shader.PropertyToID("_UseLocalizedHeat");

    void Start()
    {
        fireController = GetComponent<FireProfileController>();
        flammableObject = GetComponent<FlammableObject>();
        thermalRenderers = GetComponentsInChildren<Renderer>(true);
        propBlock = new MaterialPropertyBlock();
        heatOriginWorld = transform.position;
        heatOriginLocal = Vector3.zero;
        UpdateMaximumHeatDistance();

        heatEffects = GetComponent<ThermalHeatEffects>();
        if (heatEffects == null)
            heatEffects = gameObject.AddComponent<ThermalHeatEffects>();
    }

    void Update()
    {
        UpdateHeatSpread();

        float targetTemperature = temperature;
        if (fireController != null)
        {
            // Each profile has a different maximum. A fully burning fire should
            // always reach the hottest TIC color, regardless of its profile.
            float maximum = Mathf.Max(1f, fireController.maxTemperature);
            targetTemperature = Mathf.Clamp01(fireController.currentTemperature / maximum);
        }
        else if (flammableObject != null)
        {
            targetTemperature = GetFlammableTemperature();
        }

        // NetworkedFireState replicates an explicit heat-visual flag. Preserve that
        // contract even on clients where the local fire simulation is only a puppet.
        if (isIgnited)
            targetTemperature = Mathf.Max(targetTemperature, 1f);

        UpdateTemperature(targetTemperature);
        ApplyTemperatureToRenderer();
    }

    private void UpdateTemperature(float targetTemperature)
    {
        targetTemperature = Mathf.Clamp01(targetTemperature);

        if (targetTemperature > temperature)
        {
            bool canHeatUp = isIgnited || flammableObject == null || flammableObject.onFire;
            if (!canHeatUp || timeSinceIgnition < heatUpDelay)
                return;

            float heatUpRate = 1f / Mathf.Max(0.1f, heatUpDuration);
            temperature = Mathf.MoveTowards(
                temperature,
                targetTemperature,
                heatUpRate * Time.deltaTime);
            return;
        }

        temperature = Mathf.MoveTowards(
            temperature,
            targetTemperature,
            Mathf.Max(0f, coolDownSpeed) * Time.deltaTime);
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

    private void UpdateHeatSpread()
    {
        bool isOnFire = isIgnited || (flammableObject != null && flammableObject.onFire);

        if (isOnFire && !wasOnFire)
        {
            heatOriginWorld = flammableObject != null
                ? flammableObject.GetFireOrigin()
                : transform.position;
            heatOriginLocal = transform.InverseTransformPoint(heatOriginWorld);
            heatSpreadProgress = 0f;
            timeSinceIgnition = 0f;
            UpdateMaximumHeatDistance();
        }

        if (flammableObject != null && (isOnFire || heatSpreadProgress > 0f))
            heatOriginWorld = transform.TransformPoint(heatOriginLocal);

        if (isOnFire)
        {
            timeSinceIgnition += Time.deltaTime;
            heatSpreadProgress = Mathf.MoveTowards(
                heatSpreadProgress,
                1f,
                Time.deltaTime / Mathf.Max(0.1f, heatSpreadDuration));
        }

        wasOnFire = isOnFire;
    }

    private void UpdateMaximumHeatDistance()
    {
        bool foundBounds = false;
        Bounds combinedBounds = new Bounds(transform.position, Vector3.one);

        if (thermalRenderers != null)
        {
            foreach (Renderer thermalRenderer in thermalRenderers)
            {
                if (thermalRenderer == null || thermalRenderer is ParticleSystemRenderer)
                    continue;

                if (!foundBounds)
                {
                    combinedBounds = thermalRenderer.bounds;
                    foundBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(thermalRenderer.bounds);
                }
            }
        }

        maximumHeatDistance = foundBounds
            ? Vector3.Distance(heatOriginWorld, combinedBounds.center) + combinedBounds.extents.magnitude
            : 1f;
        maximumHeatDistance = Mathf.Max(0.1f, maximumHeatDistance);
    }

    private void ApplyTemperatureToRenderer()
    {
        if (thermalRenderers == null || thermalRenderers.Length == 0)
            return;

        float growth = Mathf.SmoothStep(0f, 1f, heatSpreadProgress);
        float initialRadius = Mathf.Max(0.12f, maximumHeatDistance * 0.05f);
        float fullRadius = maximumHeatDistance * Mathf.Max(1f, fullHeatRadiusMultiplier);
        float heatRadius = Mathf.Lerp(initialRadius, fullRadius, growth);
        float useLocalizedHeat = flammableObject != null ? 1f : 0f;

        foreach (Renderer thermalRenderer in thermalRenderers)
        {
            if (thermalRenderer == null)
                continue;

            thermalRenderer.GetPropertyBlock(propBlock);
            propBlock.SetFloat(TemperatureId, temperature);
            propBlock.SetVector(HeatOriginId, heatOriginWorld);
            propBlock.SetFloat(HeatRadiusId, heatRadius);
            propBlock.SetFloat(LocalizedHeatId, useLocalizedHeat);
            thermalRenderer.SetPropertyBlock(propBlock);
        }
    }

    // Retained for the Normcore heat-visual replication contract.
    public void Ignite()
    {
        isIgnited = true;
    }

    public void Extinguish()
    {
        isIgnited = false;
    }

    // Call this when the simulation is ready for a full reset
    public void ResetTemperature()
    {
        isIgnited = false;
        temperature = 0.0f;
        heatOriginWorld = transform.position;
        heatOriginLocal = Vector3.zero;
        heatSpreadProgress = 0f;
        timeSinceIgnition = 0f;
        wasOnFire = false;

        // Force an immediate update to the renderer so it snaps to blue instantly
        if (thermalRenderers == null || thermalRenderers.Length == 0)
            thermalRenderers = GetComponentsInChildren<Renderer>(true);
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        UpdateMaximumHeatDistance();
        ApplyTemperatureToRenderer();

        if (heatEffects == null)
            heatEffects = GetComponent<ThermalHeatEffects>();
        heatEffects?.ResetLingeringHeat();
    }
}
