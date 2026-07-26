using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(HazardTemperature))]
public sealed class ThermalHeatEffects : MonoBehaviour
{
    private const string ThermalFxLayerName = "ThermalFX";
    private const string ThermalMaterialPath = "Materials/M_ThermalHeatFX";

    [Header("Heat Aura")]
    [SerializeField, Range(0f, 1f)] private float auraThreshold = 0.015f;
    [SerializeField, Min(1f)] private float auraWidthExpansion = 1.15f;
    [SerializeField, Min(1f)] private float auraHeightExpansion = 1.75f;
    [SerializeField, Range(0f, 1f)] private float maximumAuraOpacity = 0.82f;

    [Header("Lingering Surface Heat")]
    [SerializeField] private bool leaveSurfaceHeat = true;
    [SerializeField, Range(0f, 1f)] private float trailThreshold = 0.03f;
    [SerializeField, Min(0.05f)] private float depositInterval = 0.35f;
    [SerializeField, Min(0.01f)] private float depositSpacing = 0.2f;
    [SerializeField, Min(0.05f)] private float markDiameter = 0.75f;
    [SerializeField, Min(0.1f)] private float markLifetime = 24f;
    [SerializeField, Min(0.1f)] private float surfaceSearchDistance = 2f;
    [SerializeField] private LayerMask heatReceivingLayers = ~0;

    private HazardTemperature temperatureSource;
    private Renderer sourceRenderer;
    private Renderer[] sourceRenderers;
    private GameObject auraObject;
    private MeshRenderer auraRenderer;
    private Mesh auraMesh;
    private MaterialPropertyBlock auraProperties;
    private Material auraMaterial;
    private Camera thermalCamera;
    private float nextCameraSearchTime;
    private float nextDepositTime;
    private Vector3 lastDepositPosition;
    private bool hasDepositPosition;
    private int thermalFxLayer;

    private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int EffectModeId = Shader.PropertyToID("_EffectMode");

    private void Awake()
    {
        temperatureSource = GetComponent<HazardTemperature>();
        sourceRenderer = GetComponent<Renderer>();
        if (sourceRenderer == null)
            sourceRenderer = GetComponentInChildren<Renderer>();
        sourceRenderers = GetComponentsInChildren<Renderer>(true);

        thermalFxLayer = LayerMask.NameToLayer(ThermalFxLayerName);
        if (thermalFxLayer < 0)
        {
            Debug.LogError($"The '{ThermalFxLayerName}' layer is missing. Thermal heat effects cannot be isolated to the TIC camera.", this);
            enabled = false;
            return;
        }

        CreateAura();
    }

    private void Update()
    {
        if (temperatureSource == null || auraRenderer == null)
            return;

        float heat = Mathf.Clamp01(temperatureSource.NormalizedTemperature);
        float visualHeat = Mathf.Sqrt(heat);

        UpdateAuraTransform();
        UpdateAura(heat, visualHeat);

        if (leaveSurfaceHeat && heat >= trailThreshold && Time.time >= nextDepositTime)
        {
            DepositSurfaceHeat(heat);
            nextDepositTime = Time.time + depositInterval;
        }
    }

    private void CreateAura()
    {
        Material sharedThermalMaterial = Resources.Load<Material>(ThermalMaterialPath);
        if (sharedThermalMaterial == null)
        {
            Debug.LogError($"Missing Resources/{ThermalMaterialPath}.mat. Thermal heat aura was not created.", this);
            enabled = false;
            return;
        }

        auraMaterial = new Material(sharedThermalMaterial)
        {
            name = $"{sharedThermalMaterial.name} ({name})"
        };

        auraMesh = CreateQuadMesh();
        auraProperties = new MaterialPropertyBlock();

        auraObject = new GameObject($"{name} Thermal Heat Aura");
        auraObject.layer = thermalFxLayer;

        MeshFilter meshFilter = auraObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = auraMesh;

        auraRenderer = auraObject.AddComponent<MeshRenderer>();
        auraRenderer.sharedMaterial = auraMaterial;
        auraRenderer.shadowCastingMode = ShadowCastingMode.Off;
        auraRenderer.receiveShadows = false;
        auraRenderer.enabled = false;

        UpdateAuraTransform();
    }

    private void UpdateAura(float heat, float visualHeat)
    {
        bool isHot = heat >= auraThreshold;
        auraRenderer.enabled = isHot;
        auraRenderer.GetPropertyBlock(auraProperties);
        auraProperties.SetFloat(TemperatureId, heat);
        auraProperties.SetFloat(OpacityId,
            isHot ? Mathf.Lerp(0.3f, maximumAuraOpacity, visualHeat) : 0f);
        auraProperties.SetFloat(SoftnessId, 0.08f);
        auraProperties.SetFloat(EffectModeId, 1f);
        auraRenderer.SetPropertyBlock(auraProperties);
    }

    private void UpdateAuraTransform()
    {
        if (auraObject == null)
            return;

        Bounds bounds = GetSourceBounds(Vector3.one);

        if ((thermalCamera == null || !thermalCamera.isActiveAndEnabled) &&
            Time.unscaledTime >= nextCameraSearchTime)
        {
            nextCameraSearchTime = Time.unscaledTime + 1f;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera.targetTexture != null &&
                    camera.targetTexture.name.Contains("ThermalRenderTexture"))
                {
                    thermalCamera = camera;
                    break;
                }
            }
        }

        float auraHeight = Mathf.Max(0.35f, bounds.size.y * auraHeightExpansion);
        Vector3 auraPosition = new Vector3(
            bounds.center.x,
            bounds.min.y + auraHeight * 0.5f,
            bounds.center.z);
        auraObject.transform.position = auraPosition;
        auraObject.transform.localScale = new Vector3(
            Mathf.Max(0.25f, Mathf.Max(bounds.size.x, bounds.size.z) * auraWidthExpansion),
            auraHeight,
            1f);

        if (thermalCamera != null)
        {
            Vector3 cameraDirection = thermalCamera.transform.position - auraPosition;
            if (cameraDirection.sqrMagnitude > 0.0001f)
                auraObject.transform.rotation = Quaternion.LookRotation(cameraDirection, thermalCamera.transform.up);
        }
    }

    private void DepositSurfaceHeat(float heat)
    {
        Bounds bounds = GetSourceBounds(Vector3.one * 0.5f);

        Vector3 rayOrigin = new Vector3(bounds.center.x, bounds.min.y + 0.25f, bounds.center.z);
        Vector3 point;
        Vector3 normal;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, surfaceSearchDistance,
                heatReceivingLayers, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
        }
        else
        {
            point = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            normal = Vector3.up;
        }

        float footprintDiameter = Mathf.Max(
            markDiameter,
            Mathf.Max(bounds.size.x, bounds.size.z) * 2.4f);

        if (hasDepositPosition && Vector3.Distance(point, lastDepositPosition) < depositSpacing)
        {
            ThermalHeatTrailManager.Instance.Deposit(
                GetInstanceID(), point, normal, heat, footprintDiameter, markLifetime, depositSpacing);
            return;
        }

        lastDepositPosition = point;
        hasDepositPosition = true;
        ThermalHeatTrailManager.Instance.Deposit(
            GetInstanceID(), point, normal, heat, footprintDiameter, markLifetime, depositSpacing);
    }

    private Bounds GetSourceBounds(Vector3 fallbackSize)
    {
        Bounds bounds = new Bounds(transform.position, fallbackSize);
        bool foundRenderer = false;

        if (sourceRenderers != null)
        {
            foreach (Renderer rendererToInclude in sourceRenderers)
            {
                if (rendererToInclude == null || rendererToInclude is ParticleSystemRenderer)
                    continue;

                if (!foundRenderer)
                {
                    bounds = rendererToInclude.bounds;
                    foundRenderer = true;
                }
                else
                {
                    bounds.Encapsulate(rendererToInclude.bounds);
                }
            }
        }

        if (!foundRenderer && sourceRenderer != null)
            bounds = sourceRenderer.bounds;

        return bounds;
    }

    public void ResetLingeringHeat()
    {
        if (ThermalHeatTrailManager.HasInstance)
            ThermalHeatTrailManager.Instance.ClearSource(GetInstanceID());

        hasDepositPosition = false;
        nextDepositTime = 0f;

        if (auraRenderer != null)
            auraRenderer.enabled = false;
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "Thermal Heat Aura Quad",
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            colors = new[] { Color.white, Color.white, Color.white, Color.white },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (auraObject != null)
            Destroy(auraObject);

        if (auraMesh != null)
            Destroy(auraMesh);

        if (auraMaterial != null)
            Destroy(auraMaterial);
    }
}
