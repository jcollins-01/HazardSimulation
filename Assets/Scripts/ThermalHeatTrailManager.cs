using System.Collections.Generic;
using UnityEngine;

public sealed class ThermalHeatTrailManager : MonoBehaviour
{
    private const string ThermalFxLayerName = "ThermalFX";
    private const string ThermalMaterialPath = "Materials/M_ThermalHeatFX";
    private const int MaximumMarks = 96;

    private sealed class HeatMark
    {
        public GameObject gameObject;
        public Renderer renderer;
        public MaterialPropertyBlock properties;
        public int sourceId;
        public float age;
        public float lifetime;
        public float intensity;
    }

    private static ThermalHeatTrailManager instance;

    public static bool HasInstance => instance != null;

    public static ThermalHeatTrailManager Instance
    {
        get
        {
            if (instance != null)
                return instance;

            GameObject managerObject = new GameObject("Thermal Heat Trail Manager");
            instance = managerObject.AddComponent<ThermalHeatTrailManager>();
            return instance;
        }
    }

    private readonly List<HeatMark> marks = new List<HeatMark>();
    private Material heatMaterial;
    private Mesh quadMesh;
    private int thermalFxLayer;

    private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int EffectModeId = Shader.PropertyToID("_EffectMode");

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        thermalFxLayer = LayerMask.NameToLayer(ThermalFxLayerName);
        heatMaterial = Resources.Load<Material>(ThermalMaterialPath);
        quadMesh = CreateQuadMesh();
    }

    private void Update()
    {
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            HeatMark mark = marks[i];
            mark.age += Time.deltaTime;

            if (mark.age >= mark.lifetime)
            {
                Destroy(mark.gameObject);
                marks.RemoveAt(i);
                continue;
            }

            ApplyMarkProperties(mark);
        }
    }

    public void Deposit(int sourceId, Vector3 point, Vector3 normal, float intensity,
        float diameter, float lifetime, float mergeDistance)
    {
        if (heatMaterial == null || thermalFxLayer < 0)
            return;

        HeatMark nearbyMark = FindNearbyMark(sourceId, point, mergeDistance);
        if (nearbyMark != null)
        {
            nearbyMark.age = 0f;
            nearbyMark.lifetime = Mathf.Max(nearbyMark.lifetime, lifetime);
            nearbyMark.intensity = Mathf.Max(nearbyMark.intensity, intensity);
            nearbyMark.gameObject.transform.position = point + normal * 0.006f;
            nearbyMark.gameObject.transform.rotation = Quaternion.FromToRotation(Vector3.back, normal);
            nearbyMark.gameObject.transform.localScale = Vector3.one * diameter;
            ApplyMarkProperties(nearbyMark);
            return;
        }

        if (marks.Count >= MaximumMarks)
            RemoveOldestMark();

        GameObject markObject = new GameObject("Lingering Thermal Heat");
        markObject.layer = thermalFxLayer;
        markObject.transform.SetParent(transform, true);
        markObject.transform.position = point + normal * 0.006f;
        markObject.transform.rotation = Quaternion.FromToRotation(Vector3.back, normal);
        markObject.transform.localScale = Vector3.one * diameter;

        MeshFilter filter = markObject.AddComponent<MeshFilter>();
        filter.sharedMesh = quadMesh;

        MeshRenderer renderer = markObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = heatMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        HeatMark mark = new HeatMark
        {
            gameObject = markObject,
            renderer = renderer,
            properties = new MaterialPropertyBlock(),
            sourceId = sourceId,
            age = 0f,
            lifetime = lifetime,
            intensity = intensity
        };

        marks.Add(mark);
        ApplyMarkProperties(mark);
    }

    public void ClearSource(int sourceId)
    {
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            if (marks[i].sourceId != sourceId)
                continue;

            Destroy(marks[i].gameObject);
            marks.RemoveAt(i);
        }
    }

    private HeatMark FindNearbyMark(int sourceId, Vector3 point, float mergeDistance)
    {
        float maximumDistanceSquared = mergeDistance * mergeDistance;

        for (int i = marks.Count - 1; i >= 0; i--)
        {
            HeatMark mark = marks[i];
            if (mark.sourceId == sourceId &&
                (mark.gameObject.transform.position - point).sqrMagnitude <= maximumDistanceSquared)
            {
                return mark;
            }
        }

        return null;
    }

    private void RemoveOldestMark()
    {
        if (marks.Count == 0)
            return;

        int oldestIndex = 0;
        float oldestNormalizedAge = -1f;

        for (int i = 0; i < marks.Count; i++)
        {
            float normalizedAge = marks[i].age / Mathf.Max(0.001f, marks[i].lifetime);
            if (normalizedAge > oldestNormalizedAge)
            {
                oldestNormalizedAge = normalizedAge;
                oldestIndex = i;
            }
        }

        Destroy(marks[oldestIndex].gameObject);
        marks.RemoveAt(oldestIndex);
    }

    private static void ApplyMarkProperties(HeatMark mark)
    {
        float remaining = 1f - Mathf.Clamp01(mark.age / Mathf.Max(0.001f, mark.lifetime));
        float opacity = mark.intensity * remaining * remaining;

        mark.renderer.GetPropertyBlock(mark.properties);
        mark.properties.SetFloat(TemperatureId, mark.intensity * Mathf.Lerp(0.35f, 1f, remaining));
        mark.properties.SetFloat(OpacityId, opacity);
        mark.properties.SetFloat(SoftnessId, 0f);
        mark.properties.SetFloat(EffectModeId, 0f);
        mark.renderer.SetPropertyBlock(mark.properties);
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "Thermal Heat Mark Quad",
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
        if (instance == this)
            instance = null;

        if (quadMesh != null)
            Destroy(quadMesh);
    }
}
