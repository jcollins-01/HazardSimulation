// Controls a reusable window smoke plume in response to an Ignis fire lifecycle,
// with an optional manual Edit Mode preview for placement and visual tuning.
using Ignis;
using UnityEngine;
using UnityEngine.VFX;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Drives a reusable window smoke plume from an Ignis fire's synchronized lifecycle.
/// The component configures a thin window emitter and gives new smoke an outward
/// velocity; the graph then slows that motion and lifts the smoke upward.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VisualEffect))]
[ExecuteAlways]
public class WindowSmokePlumeController : MonoBehaviour
{
    private enum PlumeState
    {
        Dormant,
        Building,
        Visible,
        Dispersing,
        Dispersed
    }

    private static readonly int BoxCenterId = Shader.PropertyToID("Box_center");
    private static readonly int BoxSizeId = Shader.PropertyToID("Box_size");
    private static readonly int RotationId = Shader.PropertyToID("Rotation");
    private static readonly int SpawnMultiplierId = Shader.PropertyToID("SmokeVFXMultiplier");
    private static readonly int OpacityId = Shader.PropertyToID("SmokeAlpha");
    private static readonly int ParticleSizeId = Shader.PropertyToID("SmokeParticleSize");
    private static readonly int SmokeColorId = Shader.PropertyToID("SmokeColor");
    private static readonly int WindForceId = Shader.PropertyToID("WindForce");
    private static readonly int WindMultiplierId = Shader.PropertyToID("WindMultiplier");
    private static readonly int FlameSpeedId = Shader.PropertyToID("FlameSpeed");
    private static readonly int FlameLivelinessId = Shader.PropertyToID("FlameLiveliness");
    private static readonly int FlameLivelinessSpeedId = Shader.PropertyToID("FlameLivelinessSpeed");
    private static readonly int FlameLengthId = Shader.PropertyToID("FlameLength");
    private static readonly int LodMaxDistanceId = Shader.PropertyToID("LODMaxDist");
    private static readonly int CullingDistanceId = Shader.PropertyToID("CullingDist");
    private static readonly int FireParticleMultiplierId = Shader.PropertyToID("FireParticleMultiplier");
    private static readonly int FireVfxMultiplierId = Shader.PropertyToID("FireVFXMultiplier");
    private static readonly int EmbersVfxMultiplierId = Shader.PropertyToID("EmbersVFXMultiplier");
    private static readonly int EmbersBurstVfxMultiplierId = Shader.PropertyToID("EmbersBurstVFXMultiplier");
    private static readonly int AdditionalCameraPositionId = Shader.PropertyToID("AdditionalCameraPosition");

    [Header("Fire Source")]
    [Tooltip("Ignis fire whose synchronized lifecycle controls this plume.")]
    public FireProfileController fireSource;

    [Tooltip("VFX Graph instance rendering the plume. Assigned automatically when left empty.")]
    public VisualEffect smokeEffect;

    [Header("Window Opening")]
    [Tooltip("Width and height of the emitting strip in world units.")]
    public Vector2 openingSize = new Vector2(1.2f, 0.3f);

    [Tooltip("Depth of the thin emission box in world units.")]
    [Min(0.01f)]
    public float openingDepth = 0.12f;

    [Header("Appearance")]
    [Tooltip("Maximum Ignis smoke-output multiplier used while the plume is fully established.")]
    [Min(0f)]
    public float maximumSpawnRate = 2.5f;

    [Tooltip("Maximum smoke alpha multiplier.")]
    [Min(0f)]
    public float opacity = 2f;

    [Tooltip("Base smoke particle size multiplier.")]
    [Min(0.01f)]
    public float particleSize = 1.1f;

    [Tooltip("HDR smoke tint. Values above one retain the bright Ignis smoke response.")]
    [ColorUsage(true, true)]
    public Color smokeColor = new Color(3.75f, 3.75f, 3.75f, 1f);

    [Header("Motion")]
    [Tooltip("Initial smoke velocity along this object's local forward direction, which must point outside.")]
    [Min(0f)]
    public float outwardSpeed = 1.8f;

    [Tooltip("How quickly the graph turns the initial outward motion into an upward plume.")]
    [Min(0f)]
    public float buoyancy = 1.1f;

    [Tooltip("Strength of the graph's existing turbulent smoke motion.")]
    [Min(0f)]
    public float turbulence = 0.65f;

    [Tooltip("Maximum particle lifetime in seconds; individual particles use a shorter randomized lifetime.")]
    [Min(0.1f)]
    public float lifetime = 6f;

    [Tooltip("Optional world-space direction added to the smoke's initial exit velocity. Magnitude is ignored.")]
    public Vector3 windDirection = Vector3.zero;

    [Tooltip("Strength of the wind's initial deflection; this does not apply continuous drift.")]
    [Min(0f)]
    public float windStrength = 0f;

    [Header("Lifecycle")]
    [Tooltip("Seconds used to build the plume after ignition or reignition.")]
    [Min(0f)]
    public float buildupDuration = 4f;

    [Tooltip("Seconds used to fade surviving particles after extinguishment.")]
    [Min(0f)]
    public float dispersalDuration = 8f;

    [Header("VR Culling")]
    [Tooltip("Distance at which particle density begins to decrease.")]
    [Min(0f)]
    public float lodMaxDistance = 12f;

    [Tooltip("Distance at which the graph is fully culled.")]
    [Min(0f)]
    public float cullingDistance = 25f;

    [Header("Edit Mode Preview")]
    [Tooltip("Controls both emission and opacity while the Edit Mode preview is enabled.")]
    [Range(0f, 1f)]
    public float previewStrength = 1f;

    private NetworkedFireState networkedFireState;
    private PlumeState currentState = PlumeState.Dormant;
    private double stateStartTime;
    private int observedIgnitionEpoch;
    private bool hasObservedIgnition;
    private float currentOpacityWeight;
    private float transitionStartOpacityWeight;
    private bool runtimeEffectPrepared;
#if UNITY_EDITOR
    [System.NonSerialized]
    private VisualEffect previewEffect;
#endif

    /// <summary>Whether a temporary Scene view plume is currently running.</summary>
    public bool IsPreviewing
    {
        get
        {
#if UNITY_EDITOR
            return previewEffect != null;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Resolves references, configures all static graph properties, and ensures the
    /// plume cannot appear before the controlling fire has ignited.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
        ApplyStaticGraphProperties();

        if (Application.isPlaying && !runtimeEffectPrepared)
            PrepareRuntimeEffect();
        else if (!Application.isPlaying)
            SilenceSceneEffect();
    }

    /// <summary>
    /// Initializes or clears the manual preview whenever the component becomes
    /// active in Edit Mode, including after script recompilation.
    /// </summary>
    private void OnEnable()
    {
        ResolveReferences();
        ApplyStaticGraphProperties();

        if (Application.isPlaying)
        {
            if (!runtimeEffectPrepared)
                PrepareRuntimeEffect();
        }
        else
        {
            SilenceSceneEffect();
        }
    }

    /// <summary>
    /// Clears Edit Mode particles when the component is disabled so preview smoke
    /// is not left suspended in the Scene view.
    /// </summary>
    private void OnDisable()
    {
        runtimeEffectPrepared = false;

#if UNITY_EDITOR
        StopPreview();
#endif
        if (!Application.isPlaying)
            SilenceSceneEffect();
    }

    /// <summary>
    /// Reapplies authored graph values in edit-time validation so prefab instances
    /// remain predictable when their inspector settings are adjusted.
    /// </summary>
    private void OnValidate()
    {
        openingSize.x = Mathf.Max(0.01f, openingSize.x);
        openingSize.y = Mathf.Max(0.01f, openingSize.y);
        openingDepth = Mathf.Max(0.01f, openingDepth);
        maximumSpawnRate = Mathf.Max(0f, maximumSpawnRate);
        opacity = Mathf.Max(0f, opacity);
        particleSize = Mathf.Max(0.01f, particleSize);
        outwardSpeed = Mathf.Max(0f, outwardSpeed);
        buoyancy = Mathf.Max(0f, buoyancy);
        turbulence = Mathf.Max(0f, turbulence);
        lifetime = Mathf.Max(0.1f, lifetime);
        windStrength = Mathf.Max(0f, windStrength);
        buildupDuration = Mathf.Max(0f, buildupDuration);
        dispersalDuration = Mathf.Max(0f, dispersalDuration);
        lodMaxDistance = Mathf.Max(0f, lodMaxDistance);
        cullingDistance = Mathf.Max(lodMaxDistance, cullingDistance);

        if (smokeEffect == null)
            smokeEffect = GetComponent<VisualEffect>();

        if (!Application.isPlaying)
            SilenceSceneEffect();

        ApplyStaticGraphProperties();
    }

    /// <summary>
    /// Advances buildup and dispersal using the same coherent lifecycle clock used
    /// by networked fire presentation, including late-join and reignition handling.
    /// </summary>
    private void Update()
    {
        if (!Application.isPlaying)
        {
            ApplyStaticGraphProperties();
#if UNITY_EDITOR
            RefreshPreview();
#endif
            return;
        }

        // This also covers projects that enter Play Mode with domain reload disabled.
        if (!runtimeEffectPrepared)
            PrepareRuntimeEffect();

        if (fireSource == null || smokeEffect == null)
            return;

        if (networkedFireState == null)
            networkedFireState = fireSource.GetComponent<NetworkedFireState>();

        if (!TryGetFireLifecycle(out NetworkedFireState.FireLifecycleSnapshot lifecycle))
            return;

        bool hasIgnited = lifecycle.IgnitionEpoch > 0 || lifecycle.IsBurning;
        bool enteredNewIgnition = lifecycle.IgnitionEpoch > observedIgnitionEpoch;
        if (hasIgnited)
        {
            hasObservedIgnition = true;
            observedIgnitionEpoch = Mathf.Max(observedIgnitionEpoch, lifecycle.IgnitionEpoch);
        }

        // Extinguishment wins because Ignis may retain its onFire flag during visual cleanup.
        if (lifecycle.IsExtinguished || lifecycle.IsBurnedOut)
        {
            if (currentState == PlumeState.Dormant && hasIgnited)
            {
                currentOpacityWeight = GetLateJoinExtinguishmentStartWeight(lifecycle);
                ApplyLifecycleWeights(0f, currentOpacityWeight);
            }

            if (currentState != PlumeState.Dispersing && currentState != PlumeState.Dispersed)
                BeginDispersal(GetTransitionStartTime(
                    lifecycle.ExtinguishmentStartTime,
                    lifecycle.CurrentTime));

            UpdateDispersal(lifecycle.CurrentTime);
            return;
        }

        if (lifecycle.IsBurning)
        {
            if (currentState == PlumeState.Dormant ||
                currentState == PlumeState.Dispersing ||
                currentState == PlumeState.Dispersed ||
                enteredNewIgnition)
            {
                BeginBuildup(GetTransitionStartTime(
                    lifecycle.IgnitionStartTime,
                    lifecycle.CurrentTime));
            }

            UpdateBuildup(lifecycle.CurrentTime);
            return;
        }

        if (!hasObservedIgnition)
            HideImmediately();
    }

    /// <summary>
    /// Resolves component references used in both Edit Mode and Play Mode without
    /// requiring the fire source to be assigned for a manual preview.
    /// </summary>
    private void ResolveReferences()
    {
        if (smokeEffect == null)
            smokeEffect = GetComponent<VisualEffect>();

        networkedFireState = fireSource != null
            ? fireSource.GetComponent<NetworkedFireState>()
            : null;
    }

    /// <summary>
    /// Restores normal graph playback on entering Play Mode and starts with zero
    /// contribution until the assigned fire lifecycle permits smoke.
    /// </summary>
    private void PrepareRuntimeEffect()
    {
        if (smokeEffect == null)
            return;

        runtimeEffectPrepared = true;
        currentState = PlumeState.Dormant;
        currentOpacityWeight = 0f;
        transitionStartOpacityWeight = 0f;
        observedIgnitionEpoch = 0;
        hasObservedIgnition = false;
        smokeEffect.pause = false;
        ApplyLifecycleWeights(0f, 0f);
        smokeEffect.Reinit();
        smokeEffect.Play();
    }

    /// <summary>
    /// Prevents the prefab's regular VisualEffect from simulating in Edit Mode;
    /// the temporary preview VisualEffect is managed separately by the button.
    /// </summary>
    private void SilenceSceneEffect()
    {
        if (smokeEffect == null)
            return;

        smokeEffect.SetFloat(SpawnMultiplierId, 0f);
        smokeEffect.SetFloat(OpacityId, 0f);
        smokeEffect.Stop();
        smokeEffect.Reinit();
        smokeEffect.pause = true;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Creates a temporary VFX object, following Ignis's editor preview pattern.
    /// The object is excluded from scene and build serialization.
    /// </summary>
    public void StartPreview()
    {
        if (Application.isPlaying || !isActiveAndEnabled || smokeEffect == null ||
            smokeEffect.visualEffectAsset == null || previewEffect != null ||
            EditorUtility.IsPersistent(gameObject))
            return;

        GameObject previewObject = new GameObject("Window Smoke Plume Preview");
        previewObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        // Ignis keeps its generated VFX object at the origin and supplies the
        // emitter's world position through the graph's local-space Box property.
        previewObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        previewEffect = previewObject.AddComponent<VisualEffect>();
        previewEffect.visualEffectAsset = smokeEffect.visualEffectAsset;
        previewEffect.pause = false;
        previewEffect.Reinit();
        RefreshPreview();
        EditorApplication.update += TickPreview;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Removes the temporary VFX object and editor update hook when previewing ends.
    /// </summary>
    public void StopPreview()
    {
        EditorApplication.update -= TickPreview;
        if (previewEffect != null)
        {
            if (Selection.activeGameObject == previewEffect.gameObject)
                Selection.activeGameObject = gameObject;
            DestroyImmediate(previewEffect.gameObject);
        }

        previewEffect = null;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Refreshes the preview's appearance and Ignis camera position used for LOD
    /// whenever its Inspector settings or Scene view camera change.
    /// </summary>
    public void RefreshPreview()
    {
        if (previewEffect == null || Application.isPlaying)
            return;

        ApplyStaticGraphProperties();
        float strength = Mathf.Clamp01(previewStrength);
        ApplyLifecycleWeights(strength, strength);
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null && sceneView.camera != null)
            SetVector3(AdditionalCameraPositionId, sceneView.camera.transform.position);
    }

    /// <summary>
    /// Keeps the separate VFX simulating and the Scene view repainting in Edit Mode.
    /// </summary>
    private void TickPreview()
    {
        if (Application.isPlaying || !isActiveAndEnabled || previewEffect == null)
        {
            StopPreview();
            return;
        }

        RefreshPreview();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
#endif

    /// <summary>
    /// Reads the synchronized fire snapshot, falling back to local Ignis state when
    /// the fire is intentionally used without its networking component.
    /// </summary>
    private bool TryGetFireLifecycle(out NetworkedFireState.FireLifecycleSnapshot lifecycle)
    {
        if (networkedFireState != null)
            return networkedFireState.TryGetLifecycleSnapshot(out lifecycle);

        FlammableObject flammable = fireSource != null
            ? fireSource.GetComponent<FlammableObject>()
            : null;
        if (flammable == null)
        {
            lifecycle = default;
            return false;
        }

        bool isBurning = flammable.onFire;
        bool isExtinguished = flammable.IsExtinguished();
        bool isBurnedOut = flammable.hasBurnedOut();
        lifecycle = new NetworkedFireState.FireLifecycleSnapshot(
            isBurning,
            isExtinguished,
            isBurnedOut,
            isBurning || isExtinguished || isBurnedOut ? 1 : 0,
            0.0,
            0.0,
            Time.timeAsDouble);
        return true;
    }

    /// <summary>
    /// Reconstructs the opacity present when a late-joined fire was extinguished,
    /// using shared ignition and extinguishment timestamps when both are available.
    /// </summary>
    private float GetLateJoinExtinguishmentStartWeight(
        NetworkedFireState.FireLifecycleSnapshot lifecycle)
    {
        if (lifecycle.IgnitionStartTime <= 0.0 ||
            lifecycle.ExtinguishmentStartTime <= lifecycle.IgnitionStartTime)
        {
            return 1f;
        }

        if (buildupDuration <= 0f)
            return 1f;

        float burningDuration = (float)(
            lifecycle.ExtinguishmentStartTime - lifecycle.IgnitionStartTime);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(burningDuration / buildupDuration));
    }

    /// <summary>
    /// Begins a smooth plume buildup from its current opacity, allowing a reignition
    /// to reverse an in-progress dispersal without a visible jump.
    /// </summary>
    private void BeginBuildup(double transitionStartTime)
    {
        transitionStartOpacityWeight = currentOpacityWeight;
        stateStartTime = transitionStartTime;
        currentState = PlumeState.Building;
    }

    /// <summary>
    /// Ramps both emission and opacity toward their authored maximum while burning.
    /// </summary>
    private void UpdateBuildup(double currentTime)
    {
        float progress = GetTransitionProgress(currentTime, buildupDuration);
        float weight = Mathf.Lerp(
            transitionStartOpacityWeight,
            1f,
            Mathf.SmoothStep(0f, 1f, progress));
        ApplyLifecycleWeights(weight, weight);

        if (progress >= 1f)
            currentState = PlumeState.Visible;
    }

    /// <summary>
    /// Stops new emission immediately and begins fading particles already outside
    /// the window so their graph lifetime can finish naturally.
    /// </summary>
    private void BeginDispersal(double transitionStartTime)
    {
        transitionStartOpacityWeight = currentOpacityWeight;
        stateStartTime = transitionStartTime;
        currentState = PlumeState.Dispersing;
        ApplyLifecycleWeights(0f, currentOpacityWeight);
    }

    /// <summary>
    /// Fades surviving smoke on the shared lifecycle clock after extinguishment.
    /// </summary>
    private void UpdateDispersal(double currentTime)
    {
        float progress = GetTransitionProgress(currentTime, dispersalDuration);
        float opacityWeight = Mathf.Lerp(
            transitionStartOpacityWeight,
            0f,
            Mathf.SmoothStep(0f, 1f, progress));
        ApplyLifecycleWeights(0f, opacityWeight);

        if (progress >= 1f)
            currentState = PlumeState.Dispersed;
    }

    /// <summary>
    /// Configures the project-owned Ignis-derived graph as a thin, world-space
    /// window emitter with initial outward velocity, upward lift, turbulence, and VR culling.
    /// </summary>
    private void ApplyStaticGraphProperties()
    {
        if (smokeEffect == null)
            return;

        // The regular prefab VFX shares this transform, while the generated
        // editor preview sits at the world origin like Ignis's preview objects.
        bool originBasedEffect = false;
#if UNITY_EDITOR
        originBasedEffect = !Application.isPlaying;
#endif
        SetVector3(BoxCenterId, originBasedEffect ? transform.position : Vector3.zero);
        SetVector3(BoxSizeId, new Vector3(openingSize.x, openingSize.y, openingDepth));
        SetVector3(RotationId, originBasedEffect ? transform.rotation.eulerAngles : Vector3.zero);

        Vector3 additionalWind = windDirection.sqrMagnitude > Mathf.Epsilon
            ? windDirection.normalized * windStrength
            : Vector3.zero;
        // The project-owned graph uses this inherited Ignis property as the
        // launch velocity. Its continuous smoke force is disabled, so momentum
        // dies away while the relative upward force takes over.
        Vector3 launchVelocity = transform.forward * outwardSpeed + additionalWind;
        SetVector3(WindForceId, launchVelocity);
        SetFloat(WindMultiplierId, 1f);
        // The inherited FlameSpeed input controls smoke drag, which determines
        // how quickly each particle turns from its launch path toward the updraft.
        SetFloat(FlameSpeedId, Mathf.Max(0.05f, buoyancy));
        SetFloat(FlameLivelinessId, turbulence);
        SetFloat(FlameLivelinessSpeedId, Mathf.Max(0.05f, turbulence));
        // The Ignis-derived graph authors smoke lifetime as a 2-6 second range
        // multiplied by FlameLength, so normalize the inspector's maximum lifetime.
        SetFloat(FlameLengthId, lifetime / 6f);
        SetFloat(ParticleSizeId, particleSize);
        SetVector4(SmokeColorId, smokeColor);
        SetFloat(LodMaxDistanceId, lodMaxDistance);
        SetFloat(CullingDistanceId, Mathf.Max(lodMaxDistance, cullingDistance));

        // Ignis's global particle multiplier also gates smoke spawning. Leave it
        // at one and disable the individual flame and ember outputs below.
        SetFloat(FireParticleMultiplierId, 1f);
        SetFloat(FireVfxMultiplierId, 0f);
        SetFloat(EmbersVfxMultiplierId, 0f);
        SetFloat(EmbersBurstVfxMultiplierId, 0f);
    }

    /// <summary>
    /// Writes the two runtime lifecycle controls and caches the resulting opacity.
    /// </summary>
    private void ApplyLifecycleWeights(float emissionWeight, float opacityWeight)
    {
        if (smokeEffect == null)
            return;

        emissionWeight = Mathf.Clamp01(emissionWeight);
        currentOpacityWeight = Mathf.Clamp01(opacityWeight);
        SetFloat(SpawnMultiplierId, maximumSpawnRate * emissionWeight);
        SetFloat(OpacityId, opacity * currentOpacityWeight);
    }

    /// <summary>
    /// Immediately clears lifecycle contribution while keeping the VFX component
    /// ready for a later synchronized ignition.
    /// </summary>
    private void HideImmediately()
    {
        currentState = PlumeState.Dormant;
        transitionStartOpacityWeight = 0f;
        ApplyLifecycleWeights(0f, 0f);
    }

    /// <summary>
    /// Uses the synchronized timestamp when present and otherwise starts on the
    /// current local presentation clock.
    /// </summary>
    private double GetTransitionStartTime(double synchronizedStartTime, double currentTime)
    {
        return synchronizedStartTime > 0.0
            ? System.Math.Min(synchronizedStartTime, currentTime)
            : currentTime;
    }

    /// <summary>
    /// Returns a safe zero-to-one transition value for the current shared time.
    /// </summary>
    private float GetTransitionProgress(double currentTime, float duration)
    {
        float elapsed = Mathf.Max(0f, (float)(currentTime - stateStartTime));
        if (elapsed <= 0f)
            return 0f;

        return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
    }

    private void SetFloat(int propertyId, float value)
    {
        VisualEffect effect = GetActiveEffect();
        if (effect != null && effect.HasFloat(propertyId))
            effect.SetFloat(propertyId, value);
    }

    private void SetVector3(int propertyId, Vector3 value)
    {
        VisualEffect effect = GetActiveEffect();
        if (effect != null && effect.HasVector3(propertyId))
            effect.SetVector3(propertyId, value);
    }

    private void SetVector4(int propertyId, Vector4 value)
    {
        VisualEffect effect = GetActiveEffect();
        if (effect != null && effect.HasVector4(propertyId))
            effect.SetVector4(propertyId, value);
    }

    /// <summary>Targets the temporary preview in Edit Mode and the prefab VFX in Play Mode.</summary>
    private VisualEffect GetActiveEffect()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return previewEffect;
#endif
        return smokeEffect;
    }
}
