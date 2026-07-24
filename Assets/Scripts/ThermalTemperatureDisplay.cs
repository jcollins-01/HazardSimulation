using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class ThermalTemperatureDisplay : MonoBehaviour
{
    private const string ThermalRenderTextureName = "ThermalRenderTexture";
    private const string ThermalFxLayerName = "ThermalFX";
    private const float MeasurementInterval = 0.1f;
    private const float AmbientTemperatureCelsius = 21f;
    private const float MaximumHeatedSurfaceCelsius = 250f;
    private const float DefaultBurningTemperatureCelsius = 650f;
    private const float MaximumDisplayedTemperatureCelsius = 999f;

    [SerializeField, Min(1f)] private float maximumMeasurementDistance = 50f;
    [SerializeField] private LayerMask measurementLayers = ~0;

    private Camera thermalCamera;
    private GameObject overlayObject;
    private Text temperatureText;
    private float nextMeasurementTime;
    private float displayedTemperature = AmbientTemperatureCelsius;
    private bool hasMeasurement;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToThermalCameras()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        AddDisplayToThermalCameras();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AddDisplayToThermalCameras();
    }

    private static void AddDisplayToThermalCameras()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cameraToCheck in cameras)
        {
            if (cameraToCheck.targetTexture == null ||
                !cameraToCheck.targetTexture.name.Contains(ThermalRenderTextureName) ||
                cameraToCheck.GetComponent<ThermalTemperatureDisplay>() != null)
            {
                continue;
            }

            cameraToCheck.gameObject.AddComponent<ThermalTemperatureDisplay>();
        }
    }

    private void Awake()
    {
        thermalCamera = GetComponent<Camera>();
        ExcludeThermalEffectsFromMeasurements();
        CreateOverlay();
    }

    private void Update()
    {
        if (temperatureText == null || Time.unscaledTime < nextMeasurementTime)
            return;

        nextMeasurementTime = Time.unscaledTime + MeasurementInterval;
        float measuredTemperature = MeasureCenterSpotTemperature();

        if (!hasMeasurement)
        {
            displayedTemperature = measuredTemperature;
            hasMeasurement = true;
        }
        else
        {
            const float responseSpeed = 8f;
            float response = 1f - Mathf.Exp(-responseSpeed * MeasurementInterval);
            displayedTemperature = Mathf.Lerp(displayedTemperature, measuredTemperature, response);
        }

        int roundedTemperature = Mathf.RoundToInt(
            Mathf.Clamp(displayedTemperature, 0f, MaximumDisplayedTemperatureCelsius));
        temperatureText.text = $"{roundedTemperature:000} \u00B0C";
    }

    private void ExcludeThermalEffectsFromMeasurements()
    {
        int thermalFxLayer = LayerMask.NameToLayer(ThermalFxLayerName);
        if (thermalFxLayer >= 0)
            measurementLayers &= ~(1 << thermalFxLayer);
    }

    private float MeasureCenterSpotTemperature()
    {
        Ray measurementRay = thermalCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (!Physics.Raycast(measurementRay, out RaycastHit hit, maximumMeasurementDistance,
                measurementLayers, QueryTriggerInteraction.Ignore))
        {
            return AmbientTemperatureCelsius;
        }

        HazardTemperature heatSource = hit.collider.GetComponentInParent<HazardTemperature>();
        if (heatSource != null)
            return GetHazardTemperature(heatSource);

        if (ThermalHeatTrailManager.HasInstance &&
            ThermalHeatTrailManager.Instance.TrySampleHeat(hit.point, out float surfaceHeat))
        {
            return Mathf.Lerp(
                AmbientTemperatureCelsius,
                MaximumHeatedSurfaceCelsius,
                Mathf.Pow(surfaceHeat, 0.85f));
        }

        return AmbientTemperatureCelsius;
    }

    private static float GetHazardTemperature(HazardTemperature heatSource)
    {
        float normalizedHeat = Mathf.Clamp01(heatSource.NormalizedTemperature);
        float maximumSurfaceTemperature = GetMaximumSurfaceTemperature(heatSource);

        // A slightly eased response avoids treating the gameplay heat value as
        // a linear thermometer while keeping cooling behavior easy to read.
        float apparentHeat = Mathf.Pow(normalizedHeat, 0.85f);
        return Mathf.Lerp(AmbientTemperatureCelsius, maximumSurfaceTemperature, apparentHeat);
    }

    private static float GetMaximumSurfaceTemperature(HazardTemperature heatSource)
    {
        FireProfileController fireController = heatSource.GetComponent<FireProfileController>();
        if (fireController == null)
            return DefaultBurningTemperatureCelsius;

        switch (fireController.currentProfile)
        {
            case FireProfileController.FireProfile.InstantExtinguish:
                return 350f;
            case FireProfileController.FireProfile.SlowBurn:
                return 650f;
            case FireProfileController.FireProfile.MaxResilience:
                return 900f;
            default:
                return DefaultBurningTemperatureCelsius;
        }
    }

    private void CreateOverlay()
    {
        overlayObject = new GameObject(
            "Thermal Temperature Overlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        Canvas canvas = overlayObject.GetComponent<Canvas>();
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = overlayObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1024f, 1024f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Transform physicalScreen = FindPhysicalScreen();
        if (physicalScreen != null)
        {
            overlayObject.layer = physicalScreen.gameObject.layer;
            overlayObject.transform.SetParent(physicalScreen, false);

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.sizeDelta = new Vector2(1024f, 1024f);
            overlayRect.localPosition = new Vector3(0f, 0f, -0.01f);
            overlayRect.localRotation = Quaternion.identity;
            overlayRect.localScale = Vector3.one / 1024f;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
        }
        else
        {
            // Preserve the render-texture overlay as a fallback for TIC prefabs
            // whose physical screen is not a sibling of the thermal camera.
            overlayObject.layer = gameObject.layer;
            overlayObject.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = thermalCamera;
            canvas.planeDistance = Mathf.Max(thermalCamera.nearClipPlane + 0.01f, 0.1f);
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        temperatureText = CreateText(
            "Spot Temperature",
            overlayObject.transform,
            font,
            42,
            TextAnchor.LowerRight);

        RectTransform temperatureRect = temperatureText.rectTransform;
        temperatureRect.anchorMin = new Vector2(1f, 0f);
        temperatureRect.anchorMax = new Vector2(1f, 0f);
        temperatureRect.pivot = new Vector2(1f, 0f);
        temperatureRect.anchoredPosition = new Vector2(-32f, 28f);
        temperatureRect.sizeDelta = new Vector2(320f, 72f);

        Text reticleText = CreateText(
            "Spot Reticle",
            overlayObject.transform,
            font,
            34,
            TextAnchor.MiddleCenter);
        reticleText.text = "+";

        RectTransform reticleRect = reticleText.rectTransform;
        reticleRect.anchorMin = new Vector2(0.5f, 0.5f);
        reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
        reticleRect.pivot = new Vector2(0.5f, 0.5f);
        reticleRect.anchoredPosition = Vector2.zero;
        reticleRect.sizeDelta = new Vector2(64f, 64f);
    }

    private Transform FindPhysicalScreen()
    {
        Transform ticRoot = transform.parent;
        if (ticRoot == null)
            return null;

        Transform directScreen = ticRoot.Find("Screen");
        if (directScreen != null)
            return directScreen;

        Renderer[] renderers = ticRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer rendererToCheck in renderers)
        {
            if (rendererToCheck.name == "Screen")
                return rendererToCheck.transform;
        }

        return null;
    }

    private static Text CreateText(
        string objectName,
        Transform parent,
        Font font,
        int fontSize,
        TextAnchor alignment)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text),
            typeof(Outline));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;

        return text;
    }

    private void OnDestroy()
    {
        if (overlayObject != null)
            Destroy(overlayObject);
    }
}
