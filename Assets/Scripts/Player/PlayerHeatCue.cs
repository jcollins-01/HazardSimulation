// Gives the local VR player a red edge vignette when their head is high in nearby fire or room smoke.
// Samples visible hazards and floor-relative headset height without changing shared gameplay state.
using Ignis;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class PlayerHeatCue : MonoBehaviour
{
    [Header("Local Player")]
    [SerializeField] private Camera headsetCamera;
    [Tooltip("Optional on the scene's local XR Origin. Assign only when this rig also has a PlayerController for network ownership.")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private RoomSmokeController smokeController;
    [SerializeField, Tooltip("Create this dedicated layer in Tags and Layers. Only the headset camera receives its volume.")]
    private string heatVolumeLayer = "HeatCue";

    [Header("Head Height Above Floor")]
    [SerializeField, Min(0f)] private float safeHeadHeight = 0.3f;
    [SerializeField, Min(0f)] private float fullHeatHeadHeight = 1.2f;
    [SerializeField, Min(0.1f)] private float floorRayLength = 3f;
    [SerializeField, Min(0f)] private float floorGracePeriod = 0.3f;

    [Header("Hazard Sensing")]
    [SerializeField, Min(0.1f)] private float fireRange = 4f;
    [SerializeField, Min(0.1f)] private float smokeRange = 5f;
    [SerializeField, Min(0.1f)] private float fireRefreshInterval = 1f;

    [Header("Vignette")]
    [SerializeField] private Color edgeColor = new Color(0.8f, 0.08f, 0.08f, 1f);
    [SerializeField, Range(0f, 1f)] private float maximumIntensity = 0.32f;
    [SerializeField, Range(0f, 1f)] private float edgeSmoothness = 0.65f;
    [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.5f;
    [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.8f;

    private FlammableObject[] fireSources = System.Array.Empty<FlammableObject>();
    private float nextFireRefreshTime;
    private float lastFloorY;
    private float lastFloorSampleTime = float.NegativeInfinity;
    private bool warnedAboutFloor;
    private bool warnedAboutPostProcessing;
    private bool warnedAboutSetup;
    private GameObject volumeObject;
    private VolumeProfile runtimeProfile;
    private Vignette vignette;
    private UniversalAdditionalCameraData cameraData;
    private int addedVolumeLayerBit;
    private float currentIntensity;
    private readonly RaycastHit[] surfaceHits = new RaycastHit[64];

    /// <summary>
    /// Resolves the assigned local headset and creates a private URP volume once
    /// the player rig and its camera are ready.
    /// </summary>
    private void Update()
    {
        if (headsetCamera == null)
            headsetCamera = GetComponentInChildren<Camera>(true);
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();

        if (headsetCamera == null || !headsetCamera.isActiveAndEnabled ||
            (playerController != null && !playerController.IsLocalPlayer))
        {
            SetIntensity(0f);
            return;
        }

        if (vignette == null && !TryCreateVolume())
            return;

        if (cameraData != null && !cameraData.renderPostProcessing && !warnedAboutPostProcessing)
        {
            Debug.LogWarning("PlayerHeatCue needs post processing enabled on the local headset camera.", this);
            warnedAboutPostProcessing = true;
        }

        float heightFactor = GetHeightFactor();
        float hazardFactor = heightFactor > 0f ? GetHazardExposure() : 0f;
        float targetIntensity = maximumIntensity * heightFactor * hazardFactor;
        float transitionSeconds = targetIntensity > currentIntensity ? fadeInSeconds : fadeOutSeconds;
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity,
            Time.deltaTime * Mathf.Max(maximumIntensity, currentIntensity) /
            Mathf.Max(0.01f, transitionSeconds));
        SetIntensity(currentIntensity);
    }

    /// <summary>
    /// Builds a vignette on a dedicated volume layer so other cameras and scene
    /// volume profiles do not inherit this player's heat cue.
    /// </summary>
    private bool TryCreateVolume()
    {
        int layer = LayerMask.NameToLayer(heatVolumeLayer);
        cameraData = headsetCamera.GetComponent<UniversalAdditionalCameraData>();
        if (layer < 0 || cameraData == null)
        {
            if (!warnedAboutSetup)
            {
                Debug.LogWarning("PlayerHeatCue needs a HeatCue layer and a URP headset camera. See the Unity Editor setup instructions.", this);
                warnedAboutSetup = true;
            }
            return false;
        }

        int layerBit = 1 << layer;
        if ((cameraData.volumeLayerMask.value & layerBit) == 0)
        {
            cameraData.volumeLayerMask = cameraData.volumeLayerMask.value | layerBit;
            addedVolumeLayerBit = layerBit;
        }

        volumeObject = new GameObject("Local Heat Cue Volume");
        volumeObject.layer = layer;
        volumeObject.transform.SetParent(headsetCamera.transform, false);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 1f;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "Local Heat Cue Profile";
        vignette = runtimeProfile.Add<Vignette>(true);
        vignette.color.Override(edgeColor);
        vignette.smoothness.Override(edgeSmoothness);
        vignette.intensity.Override(0f);
        volume.sharedProfile = runtimeProfile;
        return true;
    }

    /// <summary>
    /// Measures headset height above the nearest named floor or stair surface,
    /// ignoring furniture hits and tolerating brief raycast gaps.
    /// </summary>
    private float GetHeightFactor()
    {
        Vector3 head = headsetCamera.transform.position;
        Vector3 rayStart = head + Vector3.up * 0.1f;
        int hitCount = Physics.RaycastNonAlloc(rayStart, Vector3.down, surfaceHits,
            floorRayLength + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        float nearestFloorY = 0f;
        Transform localRig = playerController != null ? playerController.transform : transform.root;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = surfaceHits[i];
            if (hit.collider.transform.IsChildOf(localRig) ||
                !IsFloorOrStair(hit.collider.transform) || hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestFloorY = hit.point.y;
        }

        if (hitCount < surfaceHits.Length && nearestDistance < float.PositiveInfinity)
        {
            lastFloorY = nearestFloorY;
            lastFloorSampleTime = Time.unscaledTime;
            warnedAboutFloor = false;
        }
        else if (Time.unscaledTime - lastFloorSampleTime > floorGracePeriod)
        {
            if (!warnedAboutFloor)
            {
                Debug.LogWarning("PlayerHeatCue could not find a named floor or stair collider below the headset. Check the geometry names and ray length.", this);
                warnedAboutFloor = true;
            }
            return 0f;
        }

        float range = Mathf.Max(0.01f, fullHeatHeadHeight - safeHeadHeight);
        float fraction = Mathf.Clamp01((head.y - lastFloorY - safeHeadHeight) / range);
        return Mathf.SmoothStep(0f, 1f, fraction);
    }

    /// <summary>
    /// Takes the strongest unobstructed fire or smoke exposure; smoke strength
    /// comes from the controller's current opacity as it restores or disperses.
    /// </summary>
    private float GetHazardExposure()
    {
        if (Time.unscaledTime >= nextFireRefreshTime)
        {
            fireSources = FindObjectsByType<FlammableObject>(FindObjectsSortMode.None);
            nextFireRefreshTime = Time.unscaledTime + fireRefreshInterval;
        }

        Vector3 head = headsetCamera.transform.position;
        float strongest = 0f;
        foreach (FlammableObject source in fireSources)
        {
            if (source == null || !source.isActiveAndEnabled || !source.onFire)
                continue;

            Vector3 origin = source.GetFireOrigin();
            float distanceFactor = Mathf.Clamp01(1f - Vector3.Distance(head, origin) / fireRange);
            if (distanceFactor <= strongest || IsBlocked(head, origin, source.transform))
                continue;

            FireProfileController profile = source.GetComponent<FireProfileController>();
            float fireStrength = profile != null && profile.maxTemperature > 0
                ? Mathf.Clamp01(profile.currentTemperature / profile.maxTemperature)
                : 1f;
            strongest = Mathf.Max(strongest, distanceFactor * fireStrength);
        }

        if (smokeController == null)
            return strongest;

        for (int i = 0; i < smokeController.SmokeZoneCount; i++)
        {
            if (!smokeController.TryGetSmokeZoneExposure(i, out Transform emitter, out float smokeStrength) ||
                smokeStrength <= 0f)
                continue;

            Vector3 smokePosition = emitter.position;
            float horizontalDistance = Vector2.Distance(
                new Vector2(head.x, head.z), new Vector2(smokePosition.x, smokePosition.z));
            float distanceFactor = Mathf.Clamp01(1f - horizontalDistance / smokeRange);
            if (distanceFactor * smokeStrength <= strongest ||
                IsBlocked(head, new Vector3(smokePosition.x, head.y, smokePosition.z), emitter))
                continue;

            strongest = distanceFactor * smokeStrength;
        }

        return strongest;
    }

    /// <summary>
    /// Ignores the player's own colliders and the source object while checking
    /// whether a configured wall or door lies between the head and hazard.
    /// </summary>
    private bool IsBlocked(Vector3 head, Vector3 target, Transform source)
    {
        Vector3 atHeadHeight = new Vector3(target.x, head.y, target.z);
        Vector3 path = atHeadHeight - head;
        float distance = path.magnitude;
        if (distance < 0.01f)
            return false;

        int hitCount = Physics.RaycastNonAlloc(head, path / distance, surfaceHits,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hitCount == surfaceHits.Length)
            return true;

        Transform localRig = playerController != null ? playerController.transform : transform.root;
        for (int i = 0; i < hitCount; i++)
        {
            Transform obstacle = surfaceHits[i].collider.transform;
            if (obstacle.IsChildOf(localRig) || obstacle.IsChildOf(source))
                continue;
            if (IsWallOrDoor(obstacle))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recognizes the combined Floors objects, the authored stair prefab, and
    /// the separate ramps and landings created by the procedural generator.
    /// </summary>
    private static bool IsFloorOrStair(Transform hitTransform)
    {
        for (Transform current = hitTransform; current != null; current = current.parent)
        {
            string name = current.name;
            if (name == "Floors" || name == "Floor" || name == "Stair_Ramp" ||
                name == "Stair_Top_Landing" || name == "Stairs" ||
                name.StartsWith("Stairs (", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recognizes combined wall geometry and generated or authored door prefabs
    /// without treating furniture and other Default-layer objects as barriers.
    /// </summary>
    private static bool IsWallOrDoor(Transform hitTransform)
    {
        for (Transform current = hitTransform; current != null; current = current.parent)
        {
            string name = current.name;
            if (name == "Walls" || name == "Door" || name == "Door(Clone)" ||
                name == "Door_Interior" || name == "Door_Interior(Clone)")
                return true;
        }
        return false;
    }

    /// <summary>Applies the current strength to the local vignette.</summary>
    private void SetIntensity(float intensity)
    {
        if (vignette != null)
            vignette.intensity.value = Mathf.Clamp01(intensity);
    }

    /// <summary>Removes the private volume and restores the camera's volume layer mask.</summary>
    private void OnDisable()
    {
        SetIntensity(0f);
        currentIntensity = 0f;
        if (cameraData != null && addedVolumeLayerBit != 0)
            cameraData.volumeLayerMask = cameraData.volumeLayerMask.value & ~addedVolumeLayerBit;
        addedVolumeLayerBit = 0;
        if (volumeObject != null)
            Destroy(volumeObject);
        if (runtimeProfile != null)
            Destroy(runtimeProfile);
        volumeObject = null;
        runtimeProfile = null;
        vignette = null;
    }

    /// <summary>Keeps the adjustable height limits and effect settings valid in the Inspector.</summary>
    private void OnValidate()
    {
        fullHeatHeadHeight = Mathf.Max(safeHeadHeight + 0.01f, fullHeatHeadHeight);
        floorRayLength = Mathf.Max(fullHeatHeadHeight + 0.2f, floorRayLength);
        if (vignette != null)
        {
            vignette.color.Override(edgeColor);
            vignette.smoothness.Override(edgeSmoothness);
        }
    }
}
