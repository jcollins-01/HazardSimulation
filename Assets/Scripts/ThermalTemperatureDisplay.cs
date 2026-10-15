using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class ThermalTemperatureDisplay : MonoBehaviour
{
    private const float AmbientCelsius = 21f;
    private const float CelsiusToFahrenheitScale = 9f / 5f;
    private const float FahrenheitToCelsiusScale = 5f / 9f;
    private const float FahrenheitOffset = 32f;
    private const float MinimumMeterFahrenheit = 32f;

    [SerializeField, Min(1f)] private float maximumDistance = 50f;
    [SerializeField] private LayerMask measurementLayers = ~0;
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;
    [SerializeField, Min(100f)] private float meterMaximumFahrenheit = 1000f;

    [Header("Sensor Response")]
    [SerializeField, Min(1f)] private float heatingResponseFahrenheitPerSecond = 65f;
    [SerializeField, Min(1f)] private float coolingResponseFahrenheitPerSecond = 120f;
    [SerializeField, Min(0f)] private float sensorVariationFahrenheit = 12f;
    [SerializeField, Min(0.1f)] private float sensorVariationPeriodSeconds = 2.25f;

    private Camera thermalCamera;
    private GameObject overlay;
    private UnityEngine.UI.Text temperatureLabel;
    private UnityEngine.UI.Text modeLabel;
    private UnityEngine.UI.Image temperatureMeterFill;
    private float nextRefresh;
    private float displayedCelsius = AmbientCelsius;
    private float lastRefreshTime = -1f;
    private bool hasInitialReading;

    private void Awake()
    {
        thermalCamera = GetComponent<Camera>();
        CreateOverlay();
        RefreshReading();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefresh)
            RefreshReading();
    }

    private void RefreshReading()
    {
        nextRefresh = Time.unscaledTime + refreshInterval;
        if (temperatureLabel == null)
            return;

        float now = Time.unscaledTime;
        float deltaTime = lastRefreshTime < 0f ? 0f : Mathf.Max(0f, now - lastRefreshTime);
        lastRefreshTime = now;

        float targetCelsius = GetSensorTargetCelsius();
        if (!hasInitialReading)
        {
            // A TIC should report the heat that is already in view when it is
            // raised, rather than pretending every established fire just
            // started. Subsequent changes retain the deliberate sensor lag.
            displayedCelsius = targetCelsius;
            hasInitialReading = true;
        }
        else
        {
            float responseRateFahrenheit = targetCelsius > displayedCelsius
                ? heatingResponseFahrenheitPerSecond
                : coolingResponseFahrenheitPerSecond;
            float responseRateCelsius = Mathf.Max(1f, responseRateFahrenheit) * FahrenheitToCelsiusScale;
            displayedCelsius = Mathf.MoveTowards(
                displayedCelsius,
                targetCelsius,
                responseRateCelsius * deltaTime);
        }

        float fahrenheit = CelsiusToFahrenheit(displayedCelsius);
        float displayFahrenheit = Mathf.Clamp(fahrenheit, MinimumMeterFahrenheit, meterMaximumFahrenheit);
        temperatureLabel.text = $"{Mathf.RoundToInt(displayFahrenheit):000} \u00B0F";

        if (temperatureMeterFill == null)
            return;

        float meterRange = Mathf.Max(1f, meterMaximumFahrenheit - MinimumMeterFahrenheit);
        float normalizedTemperature = Mathf.Clamp01((fahrenheit - MinimumMeterFahrenheit) / meterRange);
        temperatureMeterFill.fillAmount = normalizedTemperature;
        temperatureMeterFill.color = GetMeterColor(normalizedTemperature);
    }

    private float MeasureCenterPoint()
    {
        Ray ray = thermalCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maximumDistance, measurementLayers,
                QueryTriggerInteraction.Ignore))
        {
            return AmbientCelsius;
        }

        HazardTemperature heat = hit.collider.GetComponentInParent<HazardTemperature>();
        float measuredCelsius = heat != null ? heat.EstimatedCelsius : AmbientCelsius;

        if (ThermalRadiationManager.TryEstimateRadiantTemperature(hit.point, out float radiantCelsius))
            measuredCelsius = Mathf.Max(measuredCelsius, radiantCelsius);

        return measuredCelsius;
    }

    private float GetSensorTargetCelsius()
    {
        float measuredCelsius = Mathf.Max(0f, MeasureCenterPoint());
        if (sensorVariationFahrenheit <= 0f || measuredCelsius <= AmbientCelsius)
            return measuredCelsius;

        float variationWeight = Mathf.InverseLerp(AmbientCelsius, 300f, measuredCelsius);
        float noiseTime = Time.unscaledTime / Mathf.Max(0.1f, sensorVariationPeriodSeconds);
        float variation = (Mathf.PerlinNoise(noiseTime, 0.37f) - 0.5f) * 2f;
        float variationCelsius = variation * sensorVariationFahrenheit * FahrenheitToCelsiusScale * variationWeight;
        return Mathf.Max(AmbientCelsius, measuredCelsius + variationCelsius);
    }

    private void CreateOverlay()
    {
        overlay = new GameObject("TIC Overlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        overlay.transform.SetParent(transform, false);
        overlay.layer = gameObject.layer;

        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = thermalCamera;
        canvas.planeDistance = Mathf.Max(thermalCamera.nearClipPlane + 0.01f, 0.1f);
        canvas.sortingOrder = 1000;

        UnityEngine.UI.CanvasScaler scaler = overlay.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1024f, 1024f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        modeLabel = CreateText("Mode", font, 25, TextAnchor.UpperLeft);
        modeLabel.text = "TI BASIC  |  WHITE HOT";
        RectTransform modeRect = modeLabel.rectTransform;
        modeRect.anchorMin = modeRect.anchorMax = modeRect.pivot = new Vector2(0f, 1f);
        modeRect.anchoredPosition = new Vector2(25f, -22f);
        modeRect.sizeDelta = new Vector2(360f, 48f);

        temperatureLabel = CreateText("Temperature", font, 44, TextAnchor.LowerRight);
        RectTransform temperatureRect = temperatureLabel.rectTransform;
        temperatureRect.anchorMin = new Vector2(1f, 0f);
        temperatureRect.anchorMax = new Vector2(1f, 0f);
        temperatureRect.pivot = new Vector2(1f, 0f);
        temperatureRect.anchoredPosition = new Vector2(-28f, 24f);
        temperatureRect.sizeDelta = new Vector2(300f, 64f);

        temperatureMeterFill = CreateTemperatureMeter();

        UnityEngine.UI.Text reticle = CreateText("Reticle", font, 34, TextAnchor.MiddleCenter);
        reticle.text = "+";
        RectTransform reticleRect = reticle.rectTransform;
        reticleRect.anchorMin = reticleRect.anchorMax = reticleRect.pivot = new Vector2(0.5f, 0.5f);
        reticleRect.sizeDelta = new Vector2(64f, 64f);
    }

    private UnityEngine.UI.Image CreateTemperatureMeter()
    {
        GameObject meterObject = new GameObject(
            "Temperature Meter", typeof(RectTransform), typeof(UnityEngine.CanvasRenderer), typeof(UnityEngine.UI.Image));
        meterObject.transform.SetParent(overlay.transform, false);
        meterObject.layer = overlay.layer;

        RectTransform meterRect = meterObject.GetComponent<RectTransform>();
        meterRect.anchorMin = new Vector2(1f, 0f);
        meterRect.anchorMax = new Vector2(1f, 0f);
        meterRect.pivot = new Vector2(1f, 0f);
        meterRect.anchoredPosition = new Vector2(-28f, 14f);
        meterRect.sizeDelta = new Vector2(300f, 18f);

        UnityEngine.UI.Image background = meterObject.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(0f, 0f, 0f, 0.72f);
        background.raycastTarget = false;

        GameObject fillObject = new GameObject(
            "Fill", typeof(RectTransform), typeof(UnityEngine.CanvasRenderer), typeof(UnityEngine.UI.Image));
        fillObject.transform.SetParent(meterObject.transform, false);
        fillObject.layer = overlay.layer;

        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        UnityEngine.UI.Image fill = fillObject.GetComponent<UnityEngine.UI.Image>();
        fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        fill.color = GetMeterColor(0f);
        fill.raycastTarget = false;
        return fill;
    }

    private UnityEngine.UI.Text CreateText(string objectName, Font font, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(
            objectName, typeof(RectTransform), typeof(UnityEngine.CanvasRenderer), typeof(UnityEngine.UI.Text),
            typeof(UnityEngine.UI.Outline));
        textObject.transform.SetParent(overlay.transform, false);
        textObject.layer = overlay.layer;

        UnityEngine.UI.Text text = textObject.GetComponent<UnityEngine.UI.Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;

        UnityEngine.UI.Outline outline = textObject.GetComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static float CelsiusToFahrenheit(float celsius)
    {
        return celsius * CelsiusToFahrenheitScale + FahrenheitOffset;
    }

    private static Color GetMeterColor(float normalizedTemperature)
    {
        if (normalizedTemperature < 0.5f)
        {
            return Color.Lerp(
                new Color(0.1f, 0.85f, 1f),
                new Color(1f, 0.9f, 0.1f),
                normalizedTemperature * 2f);
        }

        return Color.Lerp(
            new Color(1f, 0.9f, 0.1f),
            new Color(1f, 0.12f, 0.03f),
            (normalizedTemperature - 0.5f) * 2f);
    }

    private void OnDestroy()
    {
        if (overlay != null)
            Destroy(overlay);
    }

}
