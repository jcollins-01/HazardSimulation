using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TIC-only surface heat transfer. This never ignites an Ignis object and never
/// changes fire-spread gameplay; it only supplies apparent surface temperature.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class ThermalRadiationManager : MonoBehaviour
{
    private const float AmbientCelsius = 21f;
    private const float ApparentSurfaceMaximumCelsius = 450f;
    private const float UpdateInterval = 0.2f;
    private const float RadiationRadius = 3f;
    private const float MaximumTransferredHeat = 0.28f;
    private const float MinimumSourceHeat = 0.02f;
    private const int MaximumNearbyColliders = 256;
    private const int MaximumNewReceiversPerUpdate = 24;

    private static readonly List<HazardTemperature> Surfaces = new List<HazardTemperature>();
    private static ThermalRadiationManager instance;

    private readonly Collider[] nearbyColliders = new Collider[MaximumNearbyColliders];
    private float nextUpdateTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeManager()
    {
        Surfaces.Clear();
        if (instance != null)
            return;

        GameObject managerObject = new GameObject("[TIC Thermal Radiation]");
        managerObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(managerObject);
        instance = managerObject.AddComponent<ThermalRadiationManager>();
    }

    public static void Register(HazardTemperature surface)
    {
        if (surface != null && !Surfaces.Contains(surface))
            Surfaces.Add(surface);
    }

    public static void Unregister(HazardTemperature surface)
    {
        Surfaces.Remove(surface);
    }

    public static void ResetAllTemperatures()
    {
        // Full resets are rare, so favor completeness over the runtime registry:
        // this also catches inactive, late-created, and domain-reloaded surfaces.
        HazardTemperature[] allSurfaces = FindObjectsByType<HazardTemperature>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < allSurfaces.Length; i++)
            allSurfaces[i].ResetTemperature();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextUpdateTime)
            return;

        nextUpdateTime = Time.unscaledTime + UpdateInterval;
        UpdateRadiation();
    }

    private void UpdateRadiation()
    {
        RemoveMissingSurfaces();

        int receiversAdded = 0;
        int sourceCount = Surfaces.Count;
        for (int i = 0; i < sourceCount && receiversAdded < MaximumNewReceiversPerUpdate; i++)
        {
            HazardTemperature source = Surfaces[i];
            if (!IsActiveSource(source))
                continue;

            receiversAdded += DiscoverNearbyReceivers(source, MaximumNewReceiversPerUpdate - receiversAdded);
        }

        for (int i = 0; i < Surfaces.Count; i++)
            Surfaces[i]?.SetRadiantTarget(0f);

        sourceCount = Surfaces.Count;
        for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
        {
            HazardTemperature source = Surfaces[sourceIndex];
            if (IsActiveSource(source))
                ApplySourceToReceivers(source);
        }
    }

    private int DiscoverNearbyReceivers(HazardTemperature source, int budget)
    {
        if (budget <= 0)
            return 0;

        int added = 0;
        Vector3 origin = source.GetRadiationOrigin();
        int hitCount = Physics.OverlapSphereNonAlloc(
            origin,
            RadiationRadius,
            nearbyColliders,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount && added < budget; i++)
        {
            Collider nearby = nearbyColliders[i];
            if (nearby == null || IsPartOf(nearby.transform, source.transform))
                continue;

            HazardTemperature existing = nearby.GetComponentInParent<HazardTemperature>();
            if (existing != null)
                continue;

            Renderer targetRenderer = nearby.GetComponent<Renderer>();
            if (targetRenderer == null)
                targetRenderer = nearby.GetComponentInChildren<Renderer>();
            if (targetRenderer == null)
                targetRenderer = nearby.GetComponentInParent<Renderer>();
            if (!IsEligibleReceiver(targetRenderer))
                continue;

            targetRenderer.gameObject.AddComponent<HazardTemperature>();
            added++;
        }

        return added;
    }

    private static bool IsEligibleReceiver(Renderer targetRenderer)
    {
        if (targetRenderer == null || targetRenderer is ParticleSystemRenderer)
            return false;
        if (!targetRenderer.enabled || !targetRenderer.gameObject.activeInHierarchy)
            return false;
        if (targetRenderer.GetComponentInParent<Camera>() != null ||
            targetRenderer.GetComponentInParent<Canvas>() != null)
            return false;

        // A single temperature property would tint an entire building-sized wall
        // or floor. Keep those as occluders; only bounded nearby objects become
        // uniform-temperature receivers until authored thermal maps are available.
        if (targetRenderer.bounds.extents.magnitude > 3f)
            return false;

        Transform current = targetRenderer.transform;
        while (current != null)
        {
            if (current.CompareTag("Player"))
                return false;
            current = current.parent;
        }

        return true;
    }

    private void ApplySourceToReceivers(HazardTemperature source)
    {
        Vector3 origin = source.GetRadiationOrigin();
        float sourceHeat = source.RadiantHeat;

        for (int i = 0; i < Surfaces.Count; i++)
        {
            HazardTemperature receiver = Surfaces[i];
            if (receiver == null || receiver == source)
                continue;

            Vector3 receiverPoint = receiver.GetClosestThermalPoint(origin);
            float distance = Vector3.Distance(origin, receiverPoint);
            if (distance >= RadiationRadius || IsObstructed(source, receiver, origin, receiverPoint))
                continue;

            float transferredHeat = CalculateTransferredHeat(sourceHeat, distance);
            receiver.AccumulateRadiantTarget(transferredHeat, receiverPoint);
        }
    }

    public static float CalculateTransferredHeat(float sourceHeat, float distance)
    {
        float falloff = 1f - Mathf.Clamp01(Mathf.Max(0f, distance) / RadiationRadius);
        return Mathf.Clamp01(sourceHeat) * MaximumTransferredHeat * falloff * falloff * falloff;
    }

    public static bool TryEstimateRadiantTemperature(Vector3 point, out float celsius)
    {
        RemoveMissingSurfaces();

        float strongestHeat = 0f;
        for (int i = 0; i < Surfaces.Count; i++)
        {
            HazardTemperature source = Surfaces[i];
            if (!IsActiveSource(source))
                continue;

            Vector3 origin = source.GetRadiationOrigin();
            float distance = Vector3.Distance(origin, point);
            if (distance >= RadiationRadius || IsObstructed(source, origin, point))
                continue;

            strongestHeat = Mathf.Max(
                strongestHeat,
                CalculateTransferredHeat(source.RadiantHeat, distance));
        }

        if (strongestHeat <= 0.001f)
        {
            celsius = AmbientCelsius;
            return false;
        }

        // This is a local TIC estimate for surfaces too large to receive a
        // uniform HazardTemperature component, such as floors and walls.
        celsius = Mathf.Lerp(AmbientCelsius, ApparentSurfaceMaximumCelsius, strongestHeat);
        return true;
    }

    public static bool IsLineObstructed(
        HazardTemperature source,
        HazardTemperature receiver,
        Vector3 origin,
        Vector3 destination)
    {
        return IsObstructed(source, receiver, origin, destination);
    }

    private static bool IsActiveSource(HazardTemperature surface)
    {
        return surface != null && surface.isActiveAndEnabled &&
               surface.RadiantHeat > MinimumSourceHeat;
    }

    private static bool IsObstructed(
        HazardTemperature source,
        HazardTemperature receiver,
        Vector3 origin,
        Vector3 destination)
    {
        Vector3 delta = destination - origin;
        float distance = delta.magnitude;
        if (distance <= 0.02f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            delta / distance,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].collider != null ? hits[i].collider.transform : null;
            if (hit == null || IsPartOf(hit, source.transform) || IsPartOf(hit, receiver.transform))
                continue;
            return true;
        }

        return false;
    }

    private static bool IsObstructed(
        HazardTemperature source,
        Vector3 origin,
        Vector3 destination)
    {
        Vector3 delta = destination - origin;
        float distance = delta.magnitude;
        float queryDistance = distance - 0.02f;
        if (queryDistance <= 0.02f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            delta / distance,
            queryDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].collider != null ? hits[i].collider.transform : null;
            if (hit == null || IsPartOf(hit, source.transform))
                continue;
            return true;
        }

        return false;
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        return candidate == root || candidate.IsChildOf(root) || root.IsChildOf(candidate);
    }

    private static void RemoveMissingSurfaces()
    {
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (Surfaces[i] == null)
                Surfaces.RemoveAt(i);
        }
    }
}
