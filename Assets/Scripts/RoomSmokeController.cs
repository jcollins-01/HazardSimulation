using System;
using System.Collections.Generic;
using Ignis;
using UnityEngine;

/// <summary>
/// Synchronizes manually placed Ignis room smoke with a selected stove fire.
/// Smoke stays hidden before ignition, restores while the fire burns, and disperses
/// after extinguishment using the fire's shared presentation timeline when available.
/// </summary>
[DisallowMultipleComponent]
public class RoomSmokeController : MonoBehaviour
{
    private const float FinalVisibleAlpha = 0.001f;

    [Serializable]
    public class SmokeZone
    {
        [Tooltip("Ignis smoke object controlled by this zone.")]
        public FlammableObject smokeSource;
    }

    private enum SmokeState
    {
        Dormant,
        Restoring,
        Visible,
        Dispersing,
        Dispersed
    }

    private class SmokeZoneRuntime
    {
        public SmokeZone settings;
        public float authoredAlpha;
        public float authoredColorIntensity;
        public float currentAlpha;
        public float currentColorIntensity;
        public float transitionStartAlpha;
        public float transitionStartColorIntensity;
        public float dispersalDelay;
    }

    [Header("Fire Source")]
    [Tooltip("Stove fire whose synchronized lifecycle controls the room smoke.")]
    public FireProfileController fireSource;

    [Header("Smoke Zones")]
    [Tooltip("Manually placed Ignis smoke objects that should fade after the fire is extinguished.")]
    public List<SmokeZone> smokeZones = new List<SmokeZone>();

    [Header("Dispersal Timing")]
    [Tooltip("Seconds each smoke zone takes to fade completely.")]
    [Min(0f)]
    public float fadeDuration = 60f;

    [Tooltip("Maximum extra delay applied to the smoke zone farthest from the stove.")]
    [Min(0f)]
    public float maximumDistanceDelay = 15f;

    [Tooltip("Seconds used to restore smoke if the stove reignites during or after dispersal.")]
    [Min(0f)]
    public float reignitionRestoreDuration = 3f;

    [Header("Dispersal Appearance")]
    [Tooltip("Lowest smoke color intensity permitted while a zone fades.")]
    [Min(0f)]
    public float minimumColorIntensity = 75f;

    private readonly List<SmokeZoneRuntime> runtimeZones = new List<SmokeZoneRuntime>();
    private NetworkedFireState networkedFireState;
    private SmokeState currentState = SmokeState.Dormant;
    private double stateStartTime;
    private int observedIgnitionEpoch;
    private bool hasObservedIgnition;

    /// <summary>
    /// Caches every zone's authored appearance after all scene objects have completed Awake,
    /// prepares distance delays, and hides the smoke until the fire lifecycle is ready.
    /// </summary>
    private void Start()
    {
        InitializeSmokeZones();
        networkedFireState = fireSource != null
            ? fireSource.GetComponent<NetworkedFireState>()
            : null;
        HideSmokeImmediately();
    }

    /// <summary>
    /// Reads the stove's coherent lifecycle and advances smoke restoration or dispersal
    /// on the same presentation clock used by the networked fire.
    /// </summary>
    private void Update()
    {
        if (fireSource == null || runtimeZones.Count == 0)
            return;

        if (!TryGetFireLifecycle(out NetworkedFireState.FireLifecycleSnapshot lifecycle))
            return;

        bool hasIgnited = lifecycle.IgnitionEpoch > 0 || lifecycle.IsBurning;
        bool enteredNewIgnition = lifecycle.IgnitionEpoch > 0 &&
                                  lifecycle.IgnitionEpoch > observedIgnitionEpoch;
        if (hasIgnited)
        {
            hasObservedIgnition = true;
            observedIgnitionEpoch = Mathf.Max(observedIgnitionEpoch, lifecycle.IgnitionEpoch);
        }

        // Extinguishment wins over burning because Ignis can keep onFire true during
        // its post-extinguish visual fade.
        if (lifecycle.IsExtinguished || lifecycle.IsBurnedOut)
        {
            if (currentState == SmokeState.Dormant)
                PrepareAuthoredAppearanceForLateExtinguish();

            if (currentState != SmokeState.Dispersing && currentState != SmokeState.Dispersed)
                BeginDispersal(GetTransitionStartTime(
                    lifecycle.ExtinguishmentStartTime,
                    lifecycle.CurrentTime));

            if (currentState == SmokeState.Dispersing)
                UpdateDispersal(lifecycle.CurrentTime);

            return;
        }

        if (lifecycle.IsBurning)
        {
            if (currentState == SmokeState.Dormant ||
                currentState == SmokeState.Dispersing ||
                currentState == SmokeState.Dispersed ||
                enteredNewIgnition)
            {
                BeginRestoration(GetTransitionStartTime(
                    lifecycle.IgnitionStartTime,
                    lifecycle.CurrentTime));
            }

            if (currentState == SmokeState.Restoring)
                UpdateRestoration(lifecycle.CurrentTime);

            return;
        }

        if (!hasObservedIgnition && currentState != SmokeState.Dormant)
            HideSmokeImmediately();
    }

    /// <summary>
    /// Obtains lifecycle data from NetworkedFireState. The temperature fallback keeps
    /// legacy scene objects functional if their required network component is absent.
    /// </summary>
    private bool TryGetFireLifecycle(out NetworkedFireState.FireLifecycleSnapshot lifecycle)
    {
        if (networkedFireState != null)
            return networkedFireState.TryGetLifecycleSnapshot(out lifecycle);

        lifecycle = default;
        if (fireSource == null)
            return false;

        bool isBurning = GetNormalizedFireIntensity() > 0f;
        lifecycle = new NetworkedFireState.FireLifecycleSnapshot(
            isBurning,
            !isBurning && hasObservedIgnition,
            false,
            isBurning || hasObservedIgnition ? 1 : 0,
            0.0,
            0.0,
            Time.time);
        return true;
    }

    /// <summary>
    /// Validates zone references, records their original alpha and color intensity,
    /// and calculates how much later each zone should begin fading.
    /// </summary>
    private void InitializeSmokeZones()
    {
        runtimeZones.Clear();

        if (fireSource == null)
            Debug.LogWarning("RoomSmokeController needs a FireProfileController assigned in the Inspector.", this);

        if (smokeZones == null || smokeZones.Count == 0)
        {
            Debug.LogWarning("RoomSmokeController has no smoke zones assigned in the Inspector.", this);
            return;
        }

        HashSet<FlammableObject> registeredSources = new HashSet<FlammableObject>();

        for (int i = 0; i < smokeZones.Count; i++)
        {
            SmokeZone zone = smokeZones[i];
            if (zone == null || zone.smokeSource == null)
            {
                Debug.LogWarning($"RoomSmokeController smoke zone {i} has no FlammableObject assigned and will be ignored.", this);
                continue;
            }

            if (!registeredSources.Add(zone.smokeSource))
            {
                Debug.LogWarning($"RoomSmokeController smoke zone {i} is assigned more than once and will be ignored.", this);
                continue;
            }

            float authoredColorIntensity = zone.smokeSource.smokeColorIntensity;
            if (authoredColorIntensity < minimumColorIntensity)
            {
                Debug.LogWarning(
                    $"Smoke zone '{zone.smokeSource.name}' had a color intensity below the configured minimum and was raised to {minimumColorIntensity}.",
                    zone.smokeSource);
                authoredColorIntensity = minimumColorIntensity;
                zone.smokeSource.smokeColorIntensity = authoredColorIntensity;
            }

            runtimeZones.Add(new SmokeZoneRuntime
            {
                settings = zone,
                authoredAlpha = Mathf.Max(0f, zone.smokeSource.smokeAlpha),
                authoredColorIntensity = authoredColorIntensity,
                currentAlpha = Mathf.Max(0f, zone.smokeSource.smokeAlpha),
                currentColorIntensity = authoredColorIntensity
            });
        }

        CalculateDistanceDelays();
    }

    /// <summary>
    /// Converts the fire temperature into a safe zero-to-one value for legacy fire
    /// sources that do not have a NetworkedFireState component.
    /// </summary>
    private float GetNormalizedFireIntensity()
    {
        if (fireSource == null || fireSource.maxTemperature <= 0)
            return 0f;

        return Mathf.Clamp01(fireSource.currentTemperature / fireSource.maxTemperature);
    }

    /// <summary>
    /// Assigns each zone a delay based on its distance from the stove. The nearest
    /// zone starts immediately and the farthest receives the full configured delay.
    /// </summary>
    private void CalculateDistanceDelays()
    {
        if (fireSource == null || runtimeZones.Count == 0)
            return;

        float shortestDistance = float.PositiveInfinity;
        float longestDistance = float.NegativeInfinity;

        for (int i = 0; i < runtimeZones.Count; i++)
        {
            float distance = Vector3.Distance(
                fireSource.transform.position,
                runtimeZones[i].settings.smokeSource.transform.position);
            shortestDistance = Mathf.Min(shortestDistance, distance);
            longestDistance = Mathf.Max(longestDistance, distance);
        }

        float distanceRange = longestDistance - shortestDistance;
        for (int i = 0; i < runtimeZones.Count; i++)
        {
            float distance = Vector3.Distance(
                fireSource.transform.position,
                runtimeZones[i].settings.smokeSource.transform.position);
            float normalizedDistance = distanceRange > Mathf.Epsilon
                ? (distance - shortestDistance) / distanceRange
                : 0f;

            runtimeZones[i].dispersalDelay = normalizedDistance * Mathf.Max(0f, maximumDistanceDelay);
        }
    }

    /// <summary>
    /// Starts or restarts dispersal from each zone's current appearance. Capturing
    /// current values prevents visual jumps if the fire goes out during restoration.
    /// </summary>
    private void BeginDispersal(double transitionStartTime)
    {
        CaptureTransitionStartValues();
        stateStartTime = transitionStartTime;
        currentState = SmokeState.Dispersing;
    }

    /// <summary>
    /// Fades each zone after its distance-based delay. Alpha reaches zero while
    /// color intensity eases toward, but never below, the configured minimum.
    /// </summary>
    private void UpdateDispersal(double currentTime)
    {
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - stateStartTime));
        bool everyZoneFinished = true;

        for (int i = 0; i < runtimeZones.Count; i++)
        {
            SmokeZoneRuntime zone = runtimeZones[i];
            float zoneElapsedTime = elapsedTime - zone.dispersalDelay;
            float progress = GetTransitionProgress(zoneElapsedTime, fadeDuration);

            if (progress < 1f)
                everyZoneFinished = false;

            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            float alpha = CalculateDispersingAlpha(zone.transitionStartAlpha, progress);
            float colorIntensity = Mathf.Lerp(
                zone.transitionStartColorIntensity,
                minimumColorIntensity,
                easedProgress);

            ApplySmokeAppearance(zone, alpha, colorIntensity);
        }

        if (everyZoneFinished)
            currentState = SmokeState.Dispersed;
    }

    /// <summary>
    /// Uses exponential decay so dense smoke loses opacity quickly at first, while
    /// the final traces fade slowly toward 0.001 before disappearing completely.
    /// </summary>
    private float CalculateDispersingAlpha(float startingAlpha, float progress)
    {
        progress = Mathf.Clamp01(progress);

        if (progress >= 1f || startingAlpha <= 0f)
            return 0f;

        if (startingAlpha <= FinalVisibleAlpha)
            return Mathf.Lerp(startingAlpha, 0f, progress);

        return startingAlpha * Mathf.Pow(FinalVisibleAlpha / startingAlpha, progress);
    }

    /// <summary>
    /// Begins a smooth return to every zone's authored appearance when the stove
    /// reignites during or after smoke dispersal.
    /// </summary>
    private void BeginRestoration(double transitionStartTime)
    {
        CaptureTransitionStartValues();
        stateStartTime = transitionStartTime;
        currentState = SmokeState.Restoring;
    }

    /// <summary>
    /// Restores all zones together without reapplying their distance delays. Once
    /// complete, the controller waits for the next full extinguishment.
    /// </summary>
    private void UpdateRestoration(double currentTime)
    {
        float elapsedTime = Mathf.Max(0f, (float)(currentTime - stateStartTime));
        float progress = GetTransitionProgress(elapsedTime, reignitionRestoreDuration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

        for (int i = 0; i < runtimeZones.Count; i++)
        {
            SmokeZoneRuntime zone = runtimeZones[i];
            float alpha = Mathf.Lerp(zone.transitionStartAlpha, zone.authoredAlpha, easedProgress);
            float colorIntensity = Mathf.Lerp(
                zone.transitionStartColorIntensity,
                zone.authoredColorIntensity,
                easedProgress);

            ApplySmokeAppearance(zone, alpha, colorIntensity);
        }

        if (progress >= 1f)
            currentState = SmokeState.Visible;
    }

    /// <summary>
    /// Uses a synchronized timestamp when one is available and otherwise starts the
    /// transition on the current local lifecycle clock.
    /// </summary>
    private double GetTransitionStartTime(double synchronizedStartTime, double currentTime)
    {
        return synchronizedStartTime > 0.0
            ? Math.Min(synchronizedStartTime, currentTime)
            : currentTime;
    }

    /// <summary>
    /// Restores cached authored values before evaluating a late-join extinguishment,
    /// allowing elapsed shared time to place the smoke directly at the correct fade.
    /// </summary>
    private void PrepareAuthoredAppearanceForLateExtinguish()
    {
        for (int i = 0; i < runtimeZones.Count; i++)
        {
            SmokeZoneRuntime zone = runtimeZones[i];
            ApplySmokeAppearance(zone, zone.authoredAlpha, zone.authoredColorIntensity);
        }
    }

    /// <summary>
    /// Hides all registered smoke zones while preserving their cached authored values.
    /// </summary>
    private void HideSmokeImmediately()
    {
        for (int i = 0; i < runtimeZones.Count; i++)
            ApplySmokeAppearance(runtimeZones[i], 0f, minimumColorIntensity);

        currentState = SmokeState.Dormant;
    }

    /// <summary>
    /// Records each zone's current appearance before a fade or restoration begins,
    /// allowing either transition to reverse smoothly from its present state.
    /// </summary>
    private void CaptureTransitionStartValues()
    {
        for (int i = 0; i < runtimeZones.Count; i++)
        {
            SmokeZoneRuntime zone = runtimeZones[i];
            zone.transitionStartAlpha = zone.currentAlpha;
            zone.transitionStartColorIntensity = zone.currentColorIntensity;
        }
    }

    /// <summary>
    /// Returns a clamped transition percentage and handles zero-duration settings
    /// without dividing by zero.
    /// </summary>
    private float GetTransitionProgress(float elapsedTime, float duration)
    {
        if (elapsedTime <= 0f)
            return 0f;

        if (duration <= 0f)
            return 1f;

        return Mathf.Clamp01(elapsedTime / duration);
    }

    /// <summary>
    /// Stores the new values on the FlammableObject and explicitly refreshes all
    /// live Ignis emitters so the visual change is visible during runtime.
    /// </summary>
    private void ApplySmokeAppearance(SmokeZoneRuntime zone, float alpha, float colorIntensity)
    {
        FlammableObject smokeSource = zone.settings.smokeSource;
        if (smokeSource == null)
            return;

        zone.currentAlpha = Mathf.Max(0f, alpha);
        zone.currentColorIntensity = Mathf.Max(minimumColorIntensity, colorIntensity);
        smokeSource.smokeAlpha = zone.currentAlpha;
        smokeSource.smokeColorIntensity = zone.currentColorIntensity;
        smokeSource.RefreshSmokeAppearance();
    }
}
