using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace Ignis
{
    public class FlammableObject : MonoBehaviour
    {
        public struct MatProps
        {
            public bool isIgnisShader;
            public Renderer renderer;
            public string originalMainColorName;
            public Color originalMainColor;
            public string originalEmissionColorName;
            public Color originalEmissionColor;
            public Dictionary<string, float> originalNameFloatPairs;
        }
        public enum CalculationArea
        {
            None,
            Object,
            Vegetation
        }

        public enum ReIgnitable
        {
            No,
            Only_After_Extinguish,
            Always
        }

        [Range(0.1f, 10000f)]
        [Tooltip("How far can fire spread (m)")]
        public float maxSpread = 10000f;

        [Min(0)]
        [Tooltip("How fast does the fire move (time... m / s). Set this to 0 to stop the automatic spread and fire timer.")]
        public float fireCrawlSpeed = 1f;

        [Tooltip("Override the VFX Variant set in the FlameEngine? With this you can change instance VFX.")]
        public bool overrideFireVFX = false;

        [Tooltip("Override default VFX with this Variant.")]
        public FlameEngine.FireVFXVariant overrideVFXVariant = FlameEngine.FireVFXVariant.Wild;

        [Tooltip("Color to blend in fire.")]
        public Color fireColor = new Color(191f / 255f, 168 / 255f, 0f, 1f);

        [Min(0)]
        [Tooltip("Color blend intensity.")]
        public float colorIntensityMultiplier = 3;

        [Tooltip("Blend amount")]
        [Range(0, 1)]
        public float fireColorBlend = 0.1f;

        [Range(1, 20)]
        [Tooltip("Particle fire max lifetime (Seconds / 4... value 4 = 1 second lifetime average.)")]
        public float flameLength = 2f;

        [Tooltip("Smoke color")]
        public Color smokeColor = new Color(46f / 255f, 46f / 255f, 46f / 255f, 1f);

        [Min(0)]
        [Tooltip("Smoke color intensity")]
        public float smokeColorIntensity = 10f;

        [Min(0)]
        [Tooltip("Smoke alpha")]
        public float smokeAlpha = 0.7f;

        [Range(0, 10)]
        [Tooltip("Smoke amount, compared to other amounts. Final val = Global*smoke multiplier")]
        public float smokeVFXMultiplier = 0.5f;

        [Range(0, 10)]
        [Tooltip("Smoke particle size. Multiplier. 1 = default size.")]
        public float smokeParticleSize = 1f;

        [Range(0, 10)]
        [Tooltip("Embers continous spawn amount. Multiplier. 1 = default amount for certain size of object")]
        public float embersVFXMultiplier = 1;

        [Range(0, 10)]
        [Tooltip("Embers burst spawn amount. Multiplier. 1 = default amount for certain size of object")]
        public float embersBurstVFXMultiplier = 1;

        [Tooltip("Embers burst random delay. Time (s). x = minimum time between bigger burst, y = maximum time between bigger burst ")]
        public Vector2 embersBurstDelayMinMax = new Vector2(4, 6);

        [Range(0, 10)]
        [Tooltip("Embers particle size. 1 = default size.")]
        public float embersParticleSize = 1;

        [Tooltip("Do you want steam to come out from the extinguish position when you extinguish?")]
        public bool useExtinguishSteam = true;

        [Tooltip("Enables Material animation if your shader has support created for it.")]
        public bool enableMaterialAnimation = true;

        [Tooltip("Shader emission color")]
        public Color shaderEmissionColor = new Color(241f / 255f, 121f / 255f, 11f / 255, 1f);

        [Tooltip("Shader Burnt color. Only applies to non-ignis shaders")]
        public Color shaderBurntColor = new Color(0.2f, 0.2f, 0.2f);

        [Range(0f, 0.75f)]
        [Tooltip("Maximum amount that non-Ignis materials darken after burning. The darkening is based on each material's original color and remains after the fire.")]
        public float maximumBurnDarkening = 0.5f;

        [Tooltip("How fast shader changes to the burnt color? Lerp percentage multiplier.")]
        [Range(0, 10)]
        public float shaderToBurntInterpolateSpeed = 0.03f;

        [Min(0)]
        [Tooltip("How strong is emission in the shader")]
        public float shaderEmissionMultiplier = 1;

        [Min(0)]
        [Tooltip("How long does it to reach max brightness in material shader. (Seconds)")]
        public float achieveMaxBrightness_s = 20;

        [Range(0, 1f)]
        [Tooltip("Shader Color noise multiplier min max")]
        public float shaderColorNoise = 0.05f;

        [Range(0, 10)]
        [Tooltip("Shader Color noise change speed.")]
        public float shaderColorNoiseSpeed = 1;

        [Tooltip("Do you want to animate fire on all renderers?")]
        public bool useMaterialAnimationOnAllRenderers = true;

        [Tooltip("Animate fire on these renderers only")]
        public List<Renderer> animateMaterialsRenderers = new List<Renderer>();

        [Tooltip("How large area should be added to collider for flame to catch (Meters)")]
        public Vector3 flameCatchAreaAddition = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("Sets this object on fire on Start()")]
        public bool setThisOnFireOnStart = false;

        [Tooltip("How far has the fire spread already")]
        public float fireSpread = 0;

        [Min(0)]
        [Tooltip("How long has the object been on fire? (Seconds)")]
        public float onFireTimer = 0;

        [Tooltip("Should the flame burn forever?")]
        public bool isEffectOnForever = false;

        [Min(0)]
        [Tooltip("When fire starts to burn out and the material become charred. (Seconds)")]
        public float burnOutStart_s = 30;

        [Min(0)]
        [Tooltip("How long the transition will take. (Seconds)")]
        public float burnOutLength_s = 10;

        [Tooltip("Can object be reignited after burnout?")]
        public ReIgnitable isReignitable = ReIgnitable.No;

        [Tooltip("Delete the gameObject after burnout? Use this only on invisible meshes e.g. joints otherwise it will not look right")]
        public bool deleteAfterBurnout = false;

        [Tooltip("(ONLY NON-MOVING OBJECTS) If you dont want to place colliders use this for fast old school fire effect, Remember to make mesh non-static and enable Read/Write")]
        public bool useMeshFire = false;

        [Tooltip("How many small fires should be placed around the mesh.")]
        public float meshFireCount = 10;

        [Tooltip("Use these meshes to determine mesh fire placements. Empty = Use all meshes in children")]
        public List<MeshFilter> meshFireMeshFilters = new List<MeshFilter>();

        [Tooltip("Calculates approx flammable area from the boundaries of the mesh. Not recommended to use with complex shapes or curved meshes. Usually good for vegetation")]
        public CalculationArea calculateFlammationAreaFromMesh = CalculationArea.None;

        [Tooltip("Is this object's fires affected by wind?")]
        public bool affectedByWind = true;

        [Min(0)]
        [Tooltip("How long will the ignition take to catch fire from other objects? (Seconds touching burning one fire), If there are two fires this is halved and so on.")]
        public float ignitionTime = 0;

        [Tooltip("By turning this off, you are only left with smoke and embers")]
        public bool enableFlameVFX = true;

        [Range(0, 10)]
        [Tooltip("How much the flames are affected by wind and air (realistic upwards motion). (Multiplier)")]
        public float flameEnvironmentalSpeed = 1;

        [Range(0f, 10f)]
        [Tooltip("How lively are the flames. Simulates random air drafts/flows pushing the flames together. (Multiplier)")]
        public float flameLiveliness = 1;

        [Range(0f, 10f)]
        [Tooltip("How much the wind will affect the flame particles. (Multiplier)")]
        public float windForceMultiplier = 1;

        [Range(0f, 3f)]
        [Tooltip("How fast the flames will change. Simulates changes in random air drafts and flows. 0 = Slow, 3 = fast. (Multiplier)")]
        public float flameLivelinessSpeed = 1;

        [Range(0.1f, 10f)]
        [Tooltip("How big are flame particles. Multiplier. 1 = default size.")]
        public float flameParticleSize = 1;

        [Tooltip("0 - Infinity. How much there is delay between the bursts. Randomized between x = min, y = max.")]
        public Vector2 flameBurstDelayMinMax = Vector2.zero;

        [Tooltip("0-Infinity. Flame spawn will vary between x = min and y = max values")]
        public Vector2 flameAreaNoiseMinMaxMultiplier = new Vector2(0.9f, 1.1f);

        [Range(0, 10)]
        [Tooltip("Final multiplier is calculated from local multiplier * global multiplier (FlameEngine)")]
        public float flameVFXMultiplier = 1;

        [Range(0.25f, 1f)]
        [Tooltip("Reduces flame particle density for better visibility without changing fire strength or gameplay.")]
        public float flameVisibilityMultiplier = 1f;

        [HideInInspector]
        public bool onFire = false;

        [Tooltip("Colliders to place the fires. If left empty, all box colliders in the object and children are used")]
        public List<BoxCollider> flammableColliders = new List<BoxCollider>();

        [Tooltip("Material indexes to be affected by fire. If left empty, all materials will be used.")]
        public List<int> flammableMaterialIndexes = new List<int>();

        [Tooltip("Do you want to use SFX for this flame?")]
        public bool useFireSFX = true;

        [Tooltip("Prefab for the fire sound to be added when the object is ignited. If left empty, default SFX will be used")]
        public GameObject customFireSFX;

        [Tooltip("If you want to set custom fire origin on the start assign this to transform")]
        public Transform customFireOriginOnStart;

        [Tooltip("Lights to be associated with the flame. These lights will reperesent the general illumination from the flame.")]
        public List<Light> flameLights = new List<Light>();

        [Range(0.0f, 1.0f)]
        [Tooltip("How hard is it to fully extinguish this object? 0 = one drop will extinguish the whole object, 1 = object needs to be fully extinguished before it burns out. Starts the burn out when: extinguish diameter > (object approx size * fullyExtinguishToughness)")]
        public float fullExtinguishToughness = 0.7f;

        [Min(0.01f)]
        [Tooltip("Seconds before fire starts to spread back after extinguish attempt.")]
        public float backSpreadCoolDown_s = 5;

        [Tooltip("To which layers this fire should spread? This could be used as conditional or to optimize.")]
        public LayerMask spreadLayerMask = ~0;

        public int openTabUpper = 0;
        public int openTabLower = 0;

        private List<VisualEffect> fires = new List<VisualEffect>();
        private List<FireTrigger> fireTriggers = new List<FireTrigger>();
        private List<AudioSource> fireSFX = new List<AudioSource>();
        private FlameEventInvoker flameEventInvoker;
        private float approxSize = 5;
        private float currentIgnitionProgress_s = 0;
        private float currentIgnitionCoolingCooldown_s = 0;
        private float ignitionCoolingCooldown_s = 1;
        private bool reIgnited = false;

        private Vector3 _fireOriginLocal = Vector3.zero;

        // NetworkedFireState configures this before ignition. Mesh placement uses
        // a local System.Random so deterministic setup does not alter Unity's global
        // random sequence or unrelated gameplay systems.
        private int _networkVfxSeed = 1;
        private bool _networkVfxSeedConfigured = false;
        private bool _localLifecycleSimulationEnabled = true;
        private bool _applyingNetworkLifecycleState = false;
        private int _networkVfxLogicalTick = -1;

        private const float NetworkVfxFixedStep = 1f / 30f;
        private const int NetworkVfxMaxBackfillSteps = 90;


        private float putOutRadius = 0;
        private Vector3 putOutAreaCenter = Vector3.zero;
        private float curBackSpreadCoolDown_s = 0;

        private AudioSource curAudio;
        private bool extinguished = false;
        private bool firstSetup = false;
        private bool sceneViewActive = false;
        private bool burntOut = false;

        private Dictionary<Material, MatProps> originalMaterialValues = new Dictionary<Material, MatProps>();

        private void Awake()
        {
            onFire = false;
            firstSetup = false;
            burntOut = false;
        }

        private void Start()
        {
            flameEventInvoker = GetComponent<FlameEventInvoker>();
            if (calculateFlammationAreaFromMesh != CalculationArea.None)
            {
                GenerateColliderFromRenderer(gameObject);
            }
            else
            {
                CalculateApproxSize();
            }

            if (isEffectOnForever)
            {
                burnOutStart_s = float.MaxValue;
                burnOutLength_s = float.MaxValue;
            }

            if (!enableFlameVFX)
            {
                flameVFXMultiplier = 0;
            }

            FlammableOnStart();
            firstSetup = true;
        }

        // Start is called before the first frame update
        void OnEnable()
        {
            if (firstSetup)
            {
                FlammableOnStart();
            }
        }

        private void FlammableOnStart()
        {
            SaveOriginalMaterialShaderProperties();

            if (useMaterialAnimationOnAllRenderers)
            {
                animateMaterialsRenderers = gameObject.GetComponentsInChildren<Renderer>().ToList();
            }

            foreach (Light light in flameLights)
            {
                if (light)
                    light.gameObject.SetActive(false);
            }

            if (customFireSFX == null)
                customFireSFX = FlameEngine.instance.defaultFireSFX;

            if (FlameEngine.instance.fireOnStart || setThisOnFireOnStart)
            {
                // Ignite
                currentIgnitionProgress_s = ignitionTime + 1;

                if (!customFireOriginOnStart)
                {
                    TryToSetOnFire(GetAutomaticFireOrigin(), 1);
                }
                else
                {
                    TryToSetOnFire(customFireOriginOnStart.position, 1);
                }

            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ResetObj();
                ResetMaterialFromIgnis();
            }
        }



        // Update is called once per frame
        void Update()
        {
            // Network puppets are display-only. NetworkedFireState applies their
            // timer, spread, extinguish and VFX state from the authority each frame.
            // Returning here prevents local frame rate and Ignis triggers from
            // independently causing reignition, burnout or delayed cleanup.
            if (!_localLifecycleSimulationEnabled)
                return;

            if (!FlameEngine.instance.pause && !burntOut)
            {
                if (currentIgnitionCoolingCooldown_s > 0)
                {
                    currentIgnitionCoolingCooldown_s -= Time.deltaTime;
                }
                else if (currentIgnitionProgress_s > 0 && currentIgnitionCoolingCooldown_s <= 0)
                {
                    currentIgnitionProgress_s -= Time.deltaTime;
                }
                else if (currentIgnitionProgress_s < 0)
                {
                    currentIgnitionProgress_s = 0;
                }

                if (onFire)
                {
                    if (fireCrawlSpeed > 0.00001f)
                        onFireTimer += Time.deltaTime;

                    //Stop the spread at some point
                    if (fireSpread < maxSpread)
                    {
                        fireSpread += fireCrawlSpeed * Time.deltaTime * (0.95f + Mathf.PerlinNoise(onFireTimer, 0) * 0.1f);
                        if (putOutRadius > 0 && curBackSpreadCoolDown_s < 0)
                        {
                            putOutRadius -= Time.deltaTime * fireSpread * 0.01f;

                        }
                        else if (curBackSpreadCoolDown_s > 0 && putOutRadius > 0)
                        {
                            curBackSpreadCoolDown_s -= Time.deltaTime;
                        }
                        UpdateVFX();

                        UpdateLights();
                    }

                    // Material damage is time-based, so it must keep progressing even
                    // after the physical fire spread has reached its maximum radius.
                    UpdateShaders();

                    if (onFireTimer > burnOutStart_s + burnOutLength_s)
                    {
                        burntOut = true;
                        onFire = false;
                        if (flameEventInvoker) flameEventInvoker.BurntOut.Invoke();
                    }

                    if (flameEventInvoker)
                    {
                        if (onFireTimer > burnOutStart_s)
                        {
                            flameEventInvoker.InvokeBurnOutStartedIfNotAlreadyInvoked();
                        }
                        if (onFireTimer > achieveMaxBrightness_s)
                        {
                            flameEventInvoker.InvokeMaxBrightnessReachedIfNotAlreadyInvoked();
                        }
                    }



                }
            }

            if (burntOut)
            {
                if (fires.Count > 0)
                    onFireTimer += Time.deltaTime;

                CheckForRemove();
            }
        }

        /// <summary>
        /// Saves the original shader/material properties which are to be animated by Ignis.
        /// </summary>
        public void SaveOriginalMaterialShaderProperties()
        {
            originalMaterialValues.Clear();
            Renderer[] rends = gameObject.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                foreach (Renderer rend in rends)
                {
                    for (int i = 0; i < rend.materials.Length; i++)
                    {
                        Material mat = rend.materials[i];
                        OAVAShaderCompatibilitySO comp = FlameEngine.instance.GetCompatibleShaders().FirstOrDefault(o => o != null && (mat.HasProperty(o.ShaderCheckProperty) || (o.ShaderCheckProperty.Equals(string.Empty) && mat.shader.name.Equals(o.ShaderName))));
                        if (comp)
                        {
                            MatProps origMatProps = new MatProps
                            {
                                renderer = rend,
                                originalNameFloatPairs = new Dictionary<string, float>()
                            };

                            if (mat.HasProperty(comp.ShaderMainColorPropertyName))
                            {
                                origMatProps.originalMainColor = mat.GetColor(comp.ShaderMainColorPropertyName);
                                origMatProps.originalMainColorName = comp.ShaderMainColorPropertyName;
                            }

                            if (mat.HasProperty(comp.ShaderEmissionColorPropertyName))
                            {
                                origMatProps.originalEmissionColor = mat.GetColor(comp.ShaderEmissionColorPropertyName);
                                origMatProps.originalEmissionColorName = comp.ShaderEmissionColorPropertyName;
                            }

                            foreach (OAVAShaderCompatibilitySO.ShaderProperty shprop in comp.onFireChangeProperties)
                            {
                                if (mat.HasProperty(shprop.name))
                                    origMatProps.originalNameFloatPairs.Add(shprop.name, mat.GetFloat(shprop.name));
                            }

                            foreach (OAVAShaderCompatibilitySO.ShaderProperty shprop in comp.onBurnoutChangeProperties)
                            {
                                if (mat.HasProperty(shprop.name) && !origMatProps.originalNameFloatPairs.ContainsKey(shprop.name))
                                {
                                    origMatProps.originalNameFloatPairs.Add(shprop.name, mat.GetFloat(shprop.name));
                                }

                            }
                            originalMaterialValues.Add(mat, origMatProps);
                        }
                        else if (mat.shader == FlameEngine.instance.flameableShader)
                        {
                            // Just save reference to the material. We only need to set couple of values to 0 to reset Ignis shader.
                            originalMaterialValues.Add(mat, new MatProps() { renderer = rend });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resets the material values which were animated by Ignis to the originals. Does not reset other material shader properties.
        /// </summary>
        public void ResetMaterialFromIgnis()
        {
            if (FlameEngine.instance)
            {
                foreach (Material mat in originalMaterialValues.Keys)
                {
                    MatProps props = originalMaterialValues[mat];

                    if (mat.shader == FlameEngine.instance.flameableShader)
                    {
                        mat.SetFloat("Fire_spread", 0);
                        mat.SetFloat("Fire_bright", 0);
                        mat.SetFloat("Charred_wood_blend", 0);
                        DynamicGI.SetEmissive(props.renderer, shaderEmissionColor * shaderEmissionMultiplier * mat.GetFloat("Fire_bright"));
                    }
                    else
                    {
                        if (mat.HasProperty(props.originalMainColorName))
                        {
                            mat.SetColor(props.originalMainColorName, props.originalMainColor);
                        }

                        if (mat.HasProperty(props.originalEmissionColorName))
                        {
                            mat.SetColor(props.originalEmissionColorName, props.originalEmissionColor);
                            DynamicGI.SetEmissive(props.renderer, mat.GetColor(props.originalEmissionColorName));
                        }

                        foreach (string floatPropName in props.originalNameFloatPairs.Keys)
                        {
                            mat.SetFloat(floatPropName, props.originalNameFloatPairs[floatPropName]);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Resets flammable object values. Does not reset the shader. Please Call ResetMaterialFromIgnis() to reset the shader.
        /// </summary>
        public void ResetObj()
        {
            onFire = false;
            onFireTimer = 0;
            fireSpread = 0;
            extinguished = false;
            burntOut = false;
            putOutRadius = 0;
            putOutAreaCenter = Vector3.zero;
            foreach (VisualEffect fire in fires)
            {
                if (fire)
                {
                    FlameEngine.instance.RemoveFlame(fire.gameObject);
                    Destroy(fire.gameObject);
                }

            }
            foreach (FireTrigger trigger in fireTriggers)
            {
                if (trigger)
                    Destroy(trigger.gameObject);
            }
            fireSFX.Clear();
            fires.Clear();
            fireTriggers.Clear();
            _networkVfxLogicalTick = -1;
        }

        /// <summary>
        /// Enables the autonomous Ignis lifecycle only on the network authority.
        /// Puppets continue rendering through the explicit network adapter methods.
        /// </summary>
        public void SetLocalLifecycleSimulationEnabled(bool enabled)
        {
            _localLifecycleSimulationEnabled = enabled;

            if (!enabled)
            {
                currentIgnitionProgress_s = 0f;
                currentIgnitionCoolingCooldown_s = 0f;
            }
        }

        private Vector3 GetAutomaticFireOrigin()
        {
            Camera mainCamera = Camera.main;
            RoomGeneration roomGeneration = FindFirstObjectByType<RoomGeneration>();
            Vector3 interiorReference = transform.position;
            bool hasInteriorReference = roomGeneration != null &&
                                        roomGeneration.TryGetRoomInteriorReference(transform, out interiorReference);
            if (!hasInteriorReference && mainCamera != null)
            {
                interiorReference = mainCamera.transform.position;
                hasInteriorReference = true;
            }
            else if (!hasInteriorReference)
            {
                interiorReference = transform.position;
            }

            BoxCollider[] boxes = flammableColliders.Count > 0
                ? flammableColliders.Where(box => box != null).ToArray()
                : GetComponentsInChildren<BoxCollider>();

            if (boxes.Length > 0)
            {
                if (!hasInteriorReference)
                {
                    Bounds bounds = boxes[0].bounds;
                    return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                }

                BoxCollider closestBox = boxes[0];
                Vector3 closestPoint = closestBox.ClosestPoint(interiorReference);
                float closestDistance = (closestPoint - interiorReference).sqrMagnitude;

                for (int i = 1; i < boxes.Length; i++)
                {
                    Vector3 point = boxes[i].ClosestPoint(interiorReference);
                    float distance = (point - interiorReference).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestBox = boxes[i];
                        closestPoint = point;
                        closestDistance = distance;
                    }
                }

                if (IsWallCollider(closestBox, closestBox.size))
                    return closestPoint;

                Vector3 topCenter = closestBox.center + Vector3.up * (closestBox.size.y * 0.5f);
                return closestBox.transform.TransformPoint(topCenter);
            }

            Renderer objectRenderer = GetComponentInChildren<Renderer>();
            if (objectRenderer != null)
            {
                Bounds bounds = objectRenderer.bounds;
                return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            }

            return transform.position;
        }

        /// <summary>
        /// Network-only ignition entry point. This deliberately bypasses the local
        /// lifecycle guard so a puppet can recreate visuals demanded by its model.
        /// </summary>
        public void SetOnFireFromCenterFromNetwork()
        {
            bool previousApplyingState = _applyingNetworkLifecycleState;
            _applyingNetworkLifecycleState = true;

            try
            {
                if (!onFire && (burntOut || extinguished))
                    ResetObj();

                SetOnFireFromCenter();
            }
            finally
            {
                _applyingNetworkLifecycleState = previousApplyingState;
            }
        }

        /// <summary>
        /// Removes any locally-created flame and records the replicated terminal or
        /// dormant state. This also repairs puppets that burned out before authority.
        /// </summary>
        public void ApplyNetworkInactiveState(bool shouldBeExtinguished, bool shouldBeBurnedOut)
        {
            bool alreadyMatches = !onFire &&
                                  extinguished == shouldBeExtinguished &&
                                  burntOut == shouldBeBurnedOut &&
                                  fires.Count == 0 &&
                                  fireTriggers.Count == 0;
            if (alreadyMatches)
                return;

            bool hadLocalFireState = onFire || burntOut || extinguished ||
                                     fires.Count > 0 || fireTriggers.Count > 0;
            if (hadLocalFireState)
                ResetObj();

            onFire = false;
            extinguished = shouldBeExtinguished;
            burntOut = shouldBeBurnedOut;

            if (shouldBeBurnedOut)
                onFireTimer = burnOutStart_s + burnOutLength_s + 0.01f;
            else if (shouldBeExtinguished)
                onFireTimer = burnOutStart_s + burnOutLength_s;

            if (hadLocalFireState && !shouldBeExtinguished && !shouldBeBurnedOut)
                ResetMaterialFromIgnis();
        }

        /// <summary>
        /// Sets the object on the fire from middle
        /// </summary>
        public void SetOnFireFromCenter()
        {
            currentIgnitionProgress_s = ignitionTime + 1;
            TryToSetOnFire(transform.position, 1);
        }

        /// <summary>
        /// Tries to set object on fire. Automatically uses formula addToIgniteProgress = Time.deltaTime * ignitePower.
        /// Use this function with Ignite Power 1, if you use it on Update and don't know how to use Ignite Timer
        /// </summary>
        /// <param name="fireOrigin">Where to start the fire</param>
        public void TryToSetOnFire(Vector3 fireOrigin, float ignitePower)
        {
            TryToSetOnFireIgniteProgressIncrease(fireOrigin, Time.deltaTime * ignitePower);

        }

        /// <summary>
        /// Same as TryToSetOnFire, but lets you specify exact amount of ignite Time decrease. If you use this on update addToIgniteProgress = Time.deltaTime gives you natural ignition time.
        /// </summary>
        /// <param name="fireOrigin">Where to start the fire</param>
        /// <param name="addToIgniteProgress">How much value should be added to currentIgnitionProgress. When current ignition progress > ignition time, object ignites.</param>
        public void TryToSetOnFireIgniteProgressIncrease(Vector3 fireOrigin, float addToIgniteProgress)
        {
            if (!_localLifecycleSimulationEnabled && !_applyingNetworkLifecycleState)
                return;

            if (onFire)
            {
                if (onFireTimer > burnOutStart_s)
                {
                    if (isReignitable == ReIgnitable.Always || (isReignitable == ReIgnitable.Only_After_Extinguish && extinguished))
                    {
                        extinguished = false;
                        onFireTimer = 0;
                        reIgnited = true;
                    }
                }

                return;
            }

            if (burntOut)
            {
                if (isReignitable == ReIgnitable.Always || (isReignitable == ReIgnitable.Only_After_Extinguish && extinguished))
                {
                    extinguished = false;
                    onFireTimer = 0;
                    burntOut = false;
                    onFire = true;
                    reIgnited = true;
                }
                return;
            }

            currentIgnitionCoolingCooldown_s = ignitionCoolingCooldown_s;
            currentIgnitionProgress_s += addToIgniteProgress;
            if (currentIgnitionProgress_s < ignitionTime)
            {
                return;
            }

            if (!_applyingNetworkLifecycleState &&
                FlameEngine.instance.FlameCount() >= FlameEngine.instance.maxFlamesInWorld)
            {
                return;
            }

            if (flameEventInvoker) flameEventInvoker.Ignited.Invoke();

            onFire = true;
            _fireOriginLocal = transform.InverseTransformPoint(fireOrigin);
            InvokeCallbacks();

            if (flammableColliders.Count <= 0)
            {
                flammableColliders = GetComponentsInChildren<BoxCollider>().ToList();
            }

            // Shader setup
            SetupShaders();

            // Fire VFX
            if (useMeshFire)
            {
                SetMeshOnFire();

                if (flammableColliders.Count > 0)
                {
                    foreach (BoxCollider box in flammableColliders)
                    {
                        GameObject trigger = Instantiate(FlameEngine.instance.fireTriggerPrefab, box.transform.TransformPoint(box.center), box.transform.rotation, FlameEngine.instance.triggerParent);
                        FireTrigger triggerComp = trigger.GetComponent<FireTrigger>();


                        triggerComp.size = Vector3.Scale(box.size, box.transform.lossyScale) + flameCatchAreaAddition;

                        triggerComp.flameObj = this;
                        triggerComp.boundBoxCollider = box;
                        triggerComp.mask = spreadLayerMask;

                        fireTriggers.Add(triggerComp);

                        triggerComp.StartTrigger();
                    }
                }
            }
            else
            {
                SetBoxesOnFire();
            }
        }

        /// <summary>
        /// Incrementally extinguishes the fire from position.
        /// </summary>
        /// <param name="position">Current extinguish position</param>
        /// <param name="startRadius">Radius of the extinguish</param>
        /// <param name="radiusIncrement">Radius incremented in every call, if new position is not extinguished</param>
        public void IncrementalExtinguish(Vector3 position, float startRadius, float radiusIncrement)
        {
            if (!_localLifecycleSimulationEnabled && !_applyingNetworkLifecycleState)
                return;

            if (!onFire)
            {
                return;
            }

            if (flameEventInvoker) flameEventInvoker.BeingExtinguished.Invoke();

            curBackSpreadCoolDown_s = backSpreadCoolDown_s;

            Vector3 localPos = transform.InverseTransformPoint(position);

            if (putOutRadius < 0.05f || (Vector3.Distance(putOutAreaCenter, localPos) - putOutRadius) > startRadius * 3)
            {
                putOutAreaCenter = localPos;
                putOutRadius = startRadius;
            }
            else
            {
                if (Vector3.Distance(localPos, putOutAreaCenter) > (putOutRadius - startRadius))
                {
                    Vector3 prevCenter = putOutAreaCenter;
                    float distanceToPos = Vector3.Distance(putOutAreaCenter, localPos);
                    putOutAreaCenter = Vector3.Lerp(putOutAreaCenter, Vector3.MoveTowards(putOutAreaCenter, localPos, (distanceToPos + startRadius - putOutRadius)), 0.2f);
                    putOutRadius += (Vector3.Distance(prevCenter, putOutAreaCenter));
                }
                else
                {
                    putOutRadius += radiusIncrement;
                }
            }

            foreach (VisualEffect fire in fires)
            {
                fire.SetFloat("PutOutArea_radius", putOutRadius);
                fire.SetVector3("PutOutArea_center", transform.TransformPoint(putOutAreaCenter));
            }

            if (fires.Count > 0 && useExtinguishSteam)
            {
                fires[0].SetVector3("SteamPos", position);
                fires[0].SendEvent("SteamBurst");
            }

            if (putOutRadius > ((approxSize * fullExtinguishToughness * 0.5f)))
            {

                if (onFireTimer < burnOutStart_s)
                {
                    onFireTimer = burnOutStart_s + 0.1f;
                    extinguished = true;
                    if (flameEventInvoker) flameEventInvoker.Extinguished.Invoke();
                }
                else
                {
                    onFireTimer += (radiusIncrement / 4) * Time.fixedDeltaTime;
                }
            }
        }

        /// <summary>
        /// Applies an authoritative extinguish state without accumulating another
        /// local simulation step. Network puppets use this to correct both ahead
        /// and behind states, including reductions caused by fire back-spread.
        /// </summary>
        public void ApplyNetworkExtinguishState(Vector3 position, float radius, bool shouldBeExtinguished)
        {
            if (!onFire)
            {
                return;
            }

            putOutAreaCenter = transform.InverseTransformPoint(position);
            putOutRadius = Mathf.Max(0f, radius);
            curBackSpreadCoolDown_s = backSpreadCoolDown_s;

            foreach (VisualEffect fire in fires)
            {
                if (!fire)
                {
                    continue;
                }

                fire.SetFloat("PutOutArea_radius", putOutRadius);
                fire.SetVector3("PutOutArea_center", position);
            }

            if (shouldBeExtinguished && !extinguished)
            {
                extinguished = true;
                if (onFireTimer < burnOutStart_s)
                {
                    onFireTimer = burnOutStart_s + 0.1f;
                }
            }
            else if (!shouldBeExtinguished && onFireTimer < burnOutStart_s)
            {
                extinguished = false;
            }
        }

        /// <summary>
        /// Configures the deterministic seed that will be applied when this object's
        /// fire VFX are next created. Network puppets call this before ignition.
        /// </summary>
        public void ConfigureNetworkVfxSeed(int seed)
        {
            _networkVfxSeed = seed == 0 ? 1 : seed;
            _networkVfxSeedConfigured = true;
        }

        /// <summary>
        /// Applies a shared ignition age and authoritative spread. A new ignition
        /// epoch restarts every emitter from a deterministic sub-seed and advances
        /// it with fixed simulation steps so late clients join the current phase.
        /// </summary>
        public void ApplyNetworkFireVisualState(int seed, float fireAge, float spread, bool restartVfx)
        {
            ConfigureNetworkVfxSeed(seed);

            if (!onFire)
                return;

            onFireTimer = Mathf.Max(0f, fireAge);
            fireSpread = Mathf.Clamp(spread, 0f, maxSpread);

            UpdateShaders();
            UpdateVFX();
            UpdateLights();

            // Exposed VFX properties must be current before advancing the shared
            // tick; otherwise one client can simulate a tick with stale spread.
            ApplyNetworkVfxSimulationState(seed, fireAge, restartVfx);
        }

        /// <summary>
        /// Refreshes exposed shader, particle and light parameters after network
        /// code changes an extinguish timer or put-out area on a strict puppet.
        /// </summary>
        public void RefreshNetworkFireVisualState()
        {
            if (!onFire)
                return;

            UpdateShaders();
            UpdateVFX();
            UpdateLights();
        }

        /// <summary>
        /// Advances every fire emitter on a shared 30 Hz logical clock. VFX Graph's
        /// automatic per-render-frame clock is paused, so a dropped Quest frame no
        /// longer changes the particle path. Late clients backfill at most 3 seconds;
        /// the join reset normally starts all clients at tick zero together.
        /// </summary>
        public void ApplyNetworkVfxSimulationState(int seed, float fireAge, bool restartVfx)
        {
            ConfigureNetworkVfxSeed(seed);

            int targetTick = Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(0f, fireAge) / NetworkVfxFixedStep));
            bool mustRestart = restartVfx || _networkVfxLogicalTick < 0 || targetTick < _networkVfxLogicalTick;
            int stepsToSimulate = mustRestart
                ? Mathf.Min(targetTick, NetworkVfxMaxBackfillSteps)
                : Mathf.Min(targetTick - _networkVfxLogicalTick, NetworkVfxMaxBackfillSteps);

            for (int i = 0; i < fires.Count; i++)
            {
                VisualEffect fire = fires[i];
                if (!fire)
                    continue;

                ApplyNetworkSeed(fire, i, mustRestart);
                fire.pause = true;

                if (stepsToSimulate > 0)
                    fire.Simulate(NetworkVfxFixedStep, (uint)stepsToSimulate);
            }

            // Treat an excessive hitch as a logical fast-forward instead of doing
            // an unbounded amount of VFX work in one frame on a headset.
            _networkVfxLogicalTick = targetTick;
        }

        private void ApplyNetworkSeed(VisualEffect fireEffect, int effectIndex, bool restartVfx)
        {
            if (!_networkVfxSeedConfigured || !fireEffect)
                return;

            fireEffect.resetSeedOnPlay = false;
            fireEffect.startSeed = DeriveEffectSeed(_networkVfxSeed, effectIndex);

            if (!restartVfx)
                return;

            fireEffect.Reinit();
        }

        private static uint DeriveEffectSeed(int baseSeed, int effectIndex)
        {
            unchecked
            {
                uint value = (uint)baseSeed + 0x9E3779B9u * (uint)(effectIndex + 1);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value == 0u ? 1u : value;
            }
        }

        private void UpdateLights()
        {
            for (int i = 0; i < flameLights.Count; i++)
            {
                Light light = flameLights[i];
                if (!light)
                {
                    flameLights.RemoveAt(i);
                    i--;
                    continue;
                }
                if (onFireTimer > burnOutStart_s)
                {
                    if (light.gameObject.activeInHierarchy)
                    {
                        FlameLightFlicker flicker = light.gameObject.GetComponent<FlameLightFlicker>();
                        if (flicker)
                        {
                            flicker.SlowDisable();
                        }
                        else
                        {
                            light.gameObject.SetActive(false);
                        }
                    }
                    continue;
                }

                if ((Vector3.Distance(light.transform.position, GetFireOrigin()) <= fireSpread) && (GetPutOutRadius() < 0.05 || Vector3.Distance(light.transform.position, GetPutOutCenter()) > GetPutOutRadius()))
                {
                    light.gameObject.SetActive(true);
                }
                else
                {
                    if (light.gameObject.activeInHierarchy)
                    {
                        FlameLightFlicker flicker = light.gameObject.GetComponent<FlameLightFlicker>();
                        if (flicker)
                        {
                            flicker.SlowDisable();
                        }
                        else
                        {
                            light.gameObject.SetActive(false);
                        }

                    }
                }
            }
        }

        private void UpdateShaders()
        {
            if (enableMaterialAnimation)
            {
                Renderer[] rends = animateMaterialsRenderers.ToArray();
                if (rends.Length > 0)
                {
                    foreach (Renderer rend in rends)
                    {
                        for (int i = 0; i < rend.materials.Length; i++)
                        {
                            Material mat = rend.materials[i];
                            if (flammableMaterialIndexes.Count <= 0 || flammableMaterialIndexes.Contains(i))
                            {
                                if (mat.shader == FlameEngine.instance.flameableShader)
                                {
                                    UpdateIgnisShader(mat, rend);
                                }
                                else
                                {
                                    UpdateSupportedThirdPartyShaders(mat, rend);
                                }
                            }

                            if (FlameEngine.instance.modifyFlamesOnRuntime)
                            {
                                if (flammableMaterialIndexes.Count <= 0 || flammableMaterialIndexes.Contains(i))
                                {
                                    if (mat.shader == FlameEngine.instance.flameableShader)
                                    {
                                        SetupIgnisShader(mat);
                                    }
                                    else
                                    {
                                        KeywordsEnableCompatibleShaders(mat);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SetupShaders()
        {
            if (enableMaterialAnimation)
            {
                Renderer[] rends = gameObject.GetComponentsInChildren<Renderer>();

                if (rends.Length > 0)
                {
                    foreach (Renderer rend in rends)
                    {
                        for (int i = 0; i < rend.materials.Length; i++)
                        {
                            Material mat = rend.materials[i];
                            if (flammableMaterialIndexes.Count <= 0 || flammableMaterialIndexes.Contains(i))
                            {
                                if (mat.shader == FlameEngine.instance.flameableShader)
                                {
                                    SetupIgnisShader(mat);
                                }
                                else
                                {
                                    KeywordsEnableCompatibleShaders(mat);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SetupIgnisShader(Material mat)
        {
            mat.SetVector("Fire_origin", transform.TransformPoint(_fireOriginLocal));
            mat.SetFloat("Voronoi_move", shaderColorNoiseSpeed * 2);
            mat.SetColor("Fire_color", shaderEmissionColor);
            mat.SetFloat("Fire_max_brightness", shaderEmissionMultiplier);
            mat.SetFloat("Fire_min_brightness", shaderEmissionMultiplier / 50);
        }

        private void KeywordsEnableCompatibleShaders(Material mat)
        {
            List<OAVAShaderCompatibilitySO> compShaders = FlameEngine.instance.GetCompatibleShaders();
            foreach (OAVAShaderCompatibilitySO shaderComp in compShaders)
            {
                if (mat.HasProperty(shaderComp.ShaderCheckProperty) ||
                    (shaderComp.ShaderCheckProperty.Equals(string.Empty) && mat.shader.name.Equals(shaderComp.ShaderName)))
                {
                    foreach (string keyword in shaderComp.onFireStartEnableKeywords)
                    {
                        mat.EnableKeyword(keyword);

                    }
                    foreach (MaterialGlobalIlluminationFlags flag in shaderComp.onFireStartEnableIlluminationFlag)
                    {
                        mat.globalIlluminationFlags = flag;
                    }
                    break;
                }
            }
        }

        private void GenerateColliderFromRenderer(GameObject flamObj)
        {
            Quaternion origRot = flamObj.transform.rotation;

            flamObj.transform.rotation = Quaternion.Euler(Vector3.zero);
            List<Renderer> rends = flamObj.GetComponentsInChildren<Renderer>().ToList();
            if (rends.Count <= 0)
            {
                Debug.LogWarning("Automatic collider calculation failed: No renderers on the object");
                return;
            }

            Bounds combinedRendBounds = rends[0].bounds;

            foreach (Renderer rend in rends)
            {
                if (rend != rends[0]) combinedRendBounds.Encapsulate(rend.bounds);
            }

            GameObject collider = new GameObject
            {
                name = "Runtime collider"
            };
            collider.transform.position = combinedRendBounds.center;
            collider.transform.localScale = new Vector3(1, 1, 1);

            BoxCollider colliderComp = collider.AddComponent<BoxCollider>();
            colliderComp.isTrigger = true;

            colliderComp.size = combinedRendBounds.size;
            if (calculateFlammationAreaFromMesh == CalculationArea.Vegetation)
            {
                collider.transform.position = new Vector3(collider.transform.position.x, combinedRendBounds.min.y, collider.transform.position.z);
                colliderComp.size = new Vector3(colliderComp.size.x * 0.8f, 0.1f, colliderComp.size.z * 0.8f);
            }

            collider.transform.parent = this.transform;

            collider.transform.localRotation = Quaternion.Euler(Vector3.zero);

            flamObj.transform.rotation = origRot;

            flammableColliders.Add(colliderComp);

            approxSize = colliderComp.size.magnitude;
        }

        private void CalculateApproxSize()
        {
            List<Renderer> rends = GetComponentsInChildren<Renderer>().ToList();
            if (rends.Count <= 0)
            {
                approxSize = 1;
                return;
            }

            Bounds combinedRendBounds = rends[0].bounds;

            foreach (Renderer rend in rends)
            {
                if (rend != rends[0]) combinedRendBounds.Encapsulate(rend.bounds);
            }

            approxSize = combinedRendBounds.size.magnitude;
        }

        private void UpdateSupportedThirdPartyShaders(Material mat, Renderer rend)
        {
            UpdateCompatibleShaders(mat, rend);
        }

        private float GetBurnDarkening()
        {
            float preBurnDuration = Mathf.Max(0.01f, Mathf.Max(achieveMaxBrightness_s, burnOutStart_s));
            float preBurnProgress = Mathf.Clamp01(onFireTimer / preBurnDuration);
            float darkeningBeforeBurnout = maximumBurnDarkening * 0.65f * preBurnProgress;

            if (onFireTimer <= burnOutStart_s)
                return darkeningBeforeBurnout;

            float burnoutProgress = Mathf.Clamp01(
                (onFireTimer - burnOutStart_s) / Mathf.Max(0.01f, burnOutLength_s));
            float darkeningAtBurnoutStart = maximumBurnDarkening * 0.65f *
                                            Mathf.Clamp01(burnOutStart_s / preBurnDuration);
            return Mathf.Lerp(darkeningAtBurnoutStart, maximumBurnDarkening, burnoutProgress);
        }

        private static Color DarkenOriginalColor(Color original, float amount)
        {
            float multiplier = 1f - Mathf.Clamp01(amount);
            return new Color(
                original.r * multiplier,
                original.g * multiplier,
                original.b * multiplier,
                original.a);
        }

        private void UpdateCompatibleShaders(Material mat, Renderer rend)
        {
            List<OAVAShaderCompatibilitySO> compShaders = FlameEngine.instance.GetCompatibleShaders();
            foreach (OAVAShaderCompatibilitySO shaderComp in compShaders)
            {
                if (mat.HasProperty(shaderComp.ShaderCheckProperty) ||
                    (shaderComp.ShaderCheckProperty.Equals(string.Empty) && mat.shader.name.Equals(shaderComp.ShaderName)))
                {
                    if (onFireTimer > burnOutStart_s)
                    {
                        float timeSinceBurnOutStart = onFireTimer - burnOutStart_s;
                        float percentageToburnOutEnd = timeSinceBurnOutStart / (burnOutLength_s);
                        float burnDarkening = GetBurnDarkening();
                        if (mat.HasProperty(shaderComp.ShaderMainColorPropertyName))
                        {
                            Color originalColor = originalMaterialValues[mat].originalMainColor;
                            mat.SetColor(shaderComp.ShaderMainColorPropertyName,
                                DarkenOriginalColor(originalColor, burnDarkening));
                        }
                        else if (!shaderComp.ShaderMainColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + mat.shader.name + " does not contain color property: " + shaderComp.ShaderMainColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        if (mat.HasProperty(shaderComp.ShaderEmissionColorPropertyName))
                        {
                            Color originalEmission = originalMaterialValues[mat].originalEmissionColor;
                            mat.SetColor(shaderComp.ShaderEmissionColorPropertyName,
                                DarkenOriginalColor(originalEmission, burnDarkening));
                            DynamicGI.SetEmissive(rend, mat.GetColor(shaderComp.ShaderEmissionColorPropertyName));
                        }
                        else if (!shaderComp.ShaderEmissionColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + mat.shader.name + " does not contain color property: " + shaderComp.ShaderEmissionColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        foreach (OAVAShaderCompatibilitySO.ShaderProperty prop in shaderComp.onBurnoutChangeProperties)
                        {
                            if (mat.HasProperty(prop.name))
                            {
                                if (_networkVfxSeedConfigured &&
                                    originalMaterialValues[mat].originalNameFloatPairs.TryGetValue(prop.name, out float originalValue))
                                {
                                    mat.SetFloat(prop.name, Mathf.Lerp(
                                        originalValue,
                                        prop.targetValue,
                                        Mathf.Clamp01(timeSinceBurnOutStart * prop.speedMultiplier)));
                                }
                                else
                                {
                                    mat.SetFloat(prop.name, Mathf.MoveTowards(
                                        mat.GetFloat(prop.name),
                                        prop.targetValue,
                                        Time.deltaTime * prop.speedMultiplier));
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!reIgnited)
                        {
                            float burnDarkening = GetBurnDarkening();
                            if (mat.HasProperty(shaderComp.ShaderMainColorPropertyName))
                            {
                                Color originalColor = originalMaterialValues[mat].originalMainColor;
                                mat.SetColor(shaderComp.ShaderMainColorPropertyName,
                                    DarkenOriginalColor(originalColor, burnDarkening));
                            }
                            else if (!shaderComp.ShaderMainColorPropertyName.Equals(string.Empty))
                            {
                                Debug.LogWarning("Shader " + mat.shader.name + " does not contain color property: " + shaderComp.ShaderMainColorPropertyName + " which has been added to ShaderCompabilities");
                            }

                            if (mat.HasProperty(shaderComp.ShaderEmissionColorPropertyName))
                            {
                                Color originalEmission = originalMaterialValues[mat].originalEmissionColor;
                                mat.SetColor(shaderComp.ShaderEmissionColorPropertyName,
                                    DarkenOriginalColor(originalEmission, burnDarkening));
                                DynamicGI.SetEmissive(rend, mat.GetColor(shaderComp.ShaderEmissionColorPropertyName));
                            }
                            else if (!shaderComp.ShaderEmissionColorPropertyName.Equals(string.Empty))
                            {
                                Debug.LogWarning("Shader " + mat.shader.name + " does not contain color property: " + shaderComp.ShaderEmissionColorPropertyName + " which has been added to ShaderCompabilities");
                            }
                        }

                        foreach (OAVAShaderCompatibilitySO.ShaderProperty prop in shaderComp.onFireChangeProperties)
                        {
                            if (mat.HasProperty(prop.name))
                            {
                                if (_networkVfxSeedConfigured &&
                                    originalMaterialValues[mat].originalNameFloatPairs.TryGetValue(prop.name, out float originalValue))
                                {
                                    float elapsed = Mathf.Max(0f, onFireTimer);
                                    mat.SetFloat(prop.name, Mathf.Lerp(
                                        originalValue,
                                        prop.targetValue,
                                        Mathf.Clamp01(elapsed * prop.speedMultiplier)));
                                }
                                else
                                {
                                    mat.SetFloat(prop.name, Mathf.MoveTowards(
                                        mat.GetFloat(prop.name),
                                        prop.targetValue,
                                        Time.deltaTime * prop.speedMultiplier));
                                }
                            }
                        }
                    }
                    break;
                }
            }
        }



        private void UpdateIgnisShader(Material mat, Renderer rend)
        {
            mat.SetFloat("Fire_spread", fireSpread);
            mat.SetVector("Fire_origin", transform.TransformPoint(_fireOriginLocal));

            if (onFireTimer >= burnOutStart_s)
            {
                if (_networkVfxSeedConfigured)
                {
                    float burnoutProgress = Mathf.Clamp01(
                        (onFireTimer - burnOutStart_s) / Mathf.Max(0.0001f, burnOutLength_s));
                    mat.SetFloat("Fire_bright", 1f - burnoutProgress);
                }
                else
                {
                    mat.SetFloat("Fire_bright",
                        Mathf.MoveTowards(mat.GetFloat("Fire_bright"), 0, (1 / burnOutLength_s) * Time.deltaTime));
                }
            }
            else
            {
                if (curBackSpreadCoolDown_s <= 0)
                {
                    float currentProgress = Mathf.Min(1, onFireTimer / achieveMaxBrightness_s);
                    mat.SetFloat("Fire_bright",
                    Mathf.Lerp(0, 1, currentProgress) * ((1 - shaderColorNoise) + (Mathf.PerlinNoise(fireSpread * shaderColorNoiseSpeed, 0) * (shaderColorNoise * 2))));
                }
                else
                {
                    mat.SetFloat("Fire_bright", Mathf.MoveTowards(mat.GetFloat("Fire_bright"), 0.1f, (1 / achieveMaxBrightness_s / 2) * Time.deltaTime));
                }

                if (!reIgnited)
                {
                    mat.SetFloat("Charred_wood_blend",
                                    Mathf.Clamp(onFireTimer / burnOutStart_s, 0, 1));
                }

            }
            DynamicGI.SetEmissive(rend, shaderEmissionColor * shaderEmissionMultiplier * mat.GetFloat("Fire_bright"));
        }

        private void UpdateVFX()
        {
            List<int> destroyIndexes = new List<int>();
            for (int i = 0; i < fires.Count; i++)
            {
                VisualEffect fire = fires[i];

                if (fire.HasFloat("Spread_radius"))
                {
                    fire.SetFloat("Spread_radius", fireSpread);
                }

                if (putOutRadius > 0)
                {
                    fire.SetVector3("PutOutArea_center", transform.TransformPoint(putOutAreaCenter));

                    if (curBackSpreadCoolDown_s <= 0)
                    {
                        fire.SetFloat("PutOutArea_radius", putOutRadius);
                    }
                }

                if (fire.HasVector3("Spread_center"))
                {
                    fire.SetVector3("Spread_center", transform.TransformPoint(_fireOriginLocal));
                }

                if (!useMeshFire)
                {
                    BoxCollider box = flammableColliders[i];
                    if (box)
                    {
                        if (fire.HasVector3("Box_center"))
                        {
                            GetFireEmissionBox(box, out Vector3 emissionCenter, out Vector3 emissionSize);
                            fire.SetVector3("Box_center", emissionCenter);
                            fire.SetVector3("Rotation", Clamp0360Vector(box.transform.rotation.eulerAngles));

                            if (FlameEngine.instance.modifyFlamesOnRuntime)
                                fire.SetVector3("Box_size", emissionSize);
                        }
                    }
                    else
                    {
                        Destroy(fire.gameObject);
                        FlameEngine.instance.RemoveFlame(fire.gameObject);
                        if (fireTriggers.Count > i && fireTriggers[i])
                            Destroy(fireTriggers[i].gameObject);
                        fires.RemoveAt(i);
                        flammableColliders.RemoveAt(i);
                        fireTriggers.RemoveAt(i);
                        if (fireSFX[i])
                        {
                            fireSFX.RemoveAt(i);
                        }
                        i--;
                    }
                }

                if (FlameEngine.instance.flameWindRetriever.OnUse() && affectedByWind && fire.HasVector3("WindForce"))
                {
                    fire.SetVector3("WindForce", FlameEngine.instance.flameWindRetriever.GetCurrentWindVelocity());
                }

                if (FlameEngine.instance.modifyFlamesOnRuntime)
                {
                    SetupFireConstants(fire);
                }

#if UNITY_EDITOR
                if (Application.isEditor)
                {
                    if (UnityEditor.SceneView.currentDrawingSceneView)
                    {
                        sceneViewActive = true;
                    }

                    if (sceneViewActive)
                    {
                        fire.SetVector3("AdditionalCameraPosition", UnityEditor.SceneView.lastActiveSceneView.camera.transform.position);
                    }
                    else
                    {
                        fire.SetVector3("AdditionalCameraPosition", new Vector3(float.MaxValue, float.MaxValue, float.MaxValue));
                    }
                }
#endif


                if (onFireTimer > burnOutStart_s)
                {
                    if (deleteAfterBurnout)
                    {
                        foreach (VisualEffect fireTemp in fires)
                        {
                            FlameEngine.instance.RemoveFlame(fireTemp.gameObject);
                            Destroy(fireTemp.gameObject);

                        }
                        foreach (FireTrigger trigger in fireTriggers)
                        {
                            Destroy(trigger.gameObject);
                        }
                        fires.Clear();
                        fireTriggers.Clear();

                        Destroy(this.gameObject);
                    }

                    float currentBurnout = Mathf.Clamp((1 - ((onFireTimer - burnOutStart_s)) / burnOutLength_s), 0, 1);

                    if (fire.HasFloat("BurnOutMultiplier"))
                    {
                        fire.SetFloat("BurnOutMultiplier", currentBurnout);
                    }

                    UpdateSFX(i, Mathf.Max(0, currentBurnout - Mathf.PerlinNoise(onFireTimer / 2, 0) * 0.1f));
                }
                else
                {
                    float distFromOrigin = Vector3.Distance(fire.GetVector3("Box_center"), GetFireOrigin());
                    if (distFromOrigin < fireSpread)
                    {
                        float currentProgress = Mathf.Clamp((fireSpread - distFromOrigin), 0, 1);
                        UpdateSFX(i, Mathf.Max(currentProgress - Mathf.PerlinNoise(onFireTimer / 2, 0) * 0.1f));
                    }

                    if (fire.HasFloat("BurnOutMultiplier"))
                    {
                        fire.SetFloat("BurnOutMultiplier", 1);
                    }
                }
            }
        }

        private void UpdateSFX(int i, float curVolume)
        {
            if (fireSFX.Count > i)
            {
                if (fireSFX[i])
                {
                    fireSFX[i].volume = curVolume;
                }
            }
        }

        private void CheckForRemove()
        {
            if (onFireTimer > (burnOutStart_s + burnOutLength_s + 7))
            {
                if (isReignitable == ReIgnitable.Always)
                {
                    ResetObj();
                    reIgnited = true;
                }
                else if (isReignitable == ReIgnitable.Only_After_Extinguish && extinguished)
                {
                    ResetObj();
                    reIgnited = true;
                }
                else
                {
                    foreach (VisualEffect fire in fires)
                    {
                        FlameEngine.instance.RemoveFlame(fire.gameObject);
                        Destroy(fire.gameObject);
                    }
                    foreach (FireTrigger trigger in fireTriggers)
                    {
                        Destroy(trigger.gameObject);
                    }
                    fires.Clear();
                    fireTriggers.Clear();
                }
            }
        }

        private void InvokeCallbacks()
        {
            foreach (FlameTriggerCallbacks callback in GetComponents<FlameTriggerCallbacks>())
            {
                callback.Invoke(nameof(callback.TriggerEvents), callback.delaySeconds);
            }
        }

        private void SetupFireConstants(VisualEffect fireEffect)
        {
            //Customization
            fireEffect.SetFloat("FireParticleMultiplier", FlameEngine.instance.globalFireVFXMultiplier);
            fireEffect.SetFloat("FireVFXMultiplier", flameVFXMultiplier * flameVisibilityMultiplier);
            fireEffect.SetFloat("FlameLength", flameLength / 4);
            fireEffect.SetFloat("FlameSpeed", flameEnvironmentalSpeed);
            fireEffect.SetFloat("FlameLiveliness", flameLiveliness);
            fireEffect.SetFloat("WindMultiplier", windForceMultiplier);
            fireEffect.SetFloat("FlameLivelinessSpeed", flameLivelinessSpeed);
            fireEffect.SetFloat("FlameParticleSize", flameParticleSize);
            fireEffect.SetVector2("SizeChangeRange", flameAreaNoiseMinMaxMultiplier);
            fireEffect.SetVector2("BurstDelayRange", flameBurstDelayMinMax);


            fireEffect.SetFloat("CullingDist", FlameEngine.instance.flameLodCullDistance);
            fireEffect.SetFloat("LODMaxDist", FlameEngine.instance.flameLodCullDistance * FlameEngine.instance.flameLodStartToFadePercentage);


            //Color
            fireEffect.SetVector4("FlameColor", new Vector4(fireColor.r * colorIntensityMultiplier, fireColor.g * colorIntensityMultiplier, fireColor.b * colorIntensityMultiplier, fireColor.a));
            fireEffect.SetFloat("FlameColorBlend", fireColorBlend);

            //Smoke
            fireEffect.SetVector4("SmokeColor", new Vector4(smokeColor.r * smokeColorIntensity, smokeColor.g * smokeColorIntensity, smokeColor.b * smokeColorIntensity, smokeColor.a));
            fireEffect.SetFloat("SmokeAlpha", smokeAlpha);
            fireEffect.SetFloat("SmokeVFXMultiplier", smokeVFXMultiplier);
            fireEffect.SetFloat("SmokeParticleSize", smokeParticleSize);

            // Embers
            fireEffect.SetFloat("EmbersVFXMultiplier", embersVFXMultiplier);
            fireEffect.SetFloat("EmbersBurstVFXMultiplier", embersBurstVFXMultiplier);
            fireEffect.SetFloat("EmbersParticleSize", embersParticleSize);
            fireEffect.SetVector2("EmbersBurstDelayMinMax", embersBurstDelayMinMax);
        }

        private void SetupSFX(Vector3 position, Transform parent)
        {
            if (useFireSFX)
            {
                GameObject audioGO = Instantiate(customFireSFX, parent);
                //Sound
                curAudio = audioGO.GetComponentInChildren<AudioSource>();
                if (curAudio)
                {
                    curAudio.transform.position = position;
                    curAudio.time = Random.Range(0f, 10f);
                    curAudio.pitch = Random.Range(0.7f, 1.5f);
                    curAudio.volume = 0;
                    fireSFX.Add(curAudio);
                }
            }
        }

        private void SetBoxesOnFire()
        {
            for (int boxIndex = 0; boxIndex < flammableColliders.Count; boxIndex++)
            {
                BoxCollider box = flammableColliders[boxIndex];

                GameObject fire = Instantiate(FlameEngine.instance.GetFireVFX(overrideVFXVariant, overrideFireVFX), new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0), FlameEngine.instance.fireParent);
                VisualEffect fireEffect = fire.GetComponent<VisualEffect>();
                FlameEngine.instance.AddFlame(fire);
                SetupFireConstants(fireEffect);

                //Place
                GetFireEmissionBox(box, out Vector3 emissionCenter, out Vector3 emissionSize);
                fireEffect.SetVector3("Spread_center", transform.TransformPoint(_fireOriginLocal));
                fireEffect.SetVector3("Box_size", emissionSize);
                fireEffect.SetVector3("Box_center", emissionCenter);
                fireEffect.SetVector3("Rotation", Clamp0360Vector(box.transform.rotation.eulerAngles));

                ApplyNetworkSeed(fireEffect, boxIndex, true);

                fires.Add(fire.GetComponent<VisualEffect>());

                SetupSFX(box.transform.TransformPoint(box.center), fire.transform);

                if (!useMeshFire)
                {
                    GameObject trigger = Instantiate(FlameEngine.instance.fireTriggerPrefab, box.transform.TransformPoint(box.center), box.transform.rotation, FlameEngine.instance.triggerParent);
                    FireTrigger triggerComp = trigger.GetComponent<FireTrigger>();


                    triggerComp.size = Vector3.Scale(box.size, box.transform.lossyScale) + flameCatchAreaAddition;

                    triggerComp.flameObj = this;
                    triggerComp.boundBoxCollider = box;
                    triggerComp.mask = spreadLayerMask;

                    fireTriggers.Add(triggerComp);

                    triggerComp.StartTrigger();
                }

            }
        }

        private void SetMeshOnFire()
        {
            MeshFilter[] filters = transform.GetComponentsInChildren<MeshFilter>();

            System.Random networkRandom = _networkVfxSeedConfigured
                ? new System.Random(_networkVfxSeed)
                : null;

            MeshFilter meshFilter = filters[RandomRange(networkRandom, 0, filters.Length)];
            Mesh meshObj = meshFilter.mesh;

            Vector3[] meshPoints = meshObj.vertices;
            for (int o = 0; o < meshFireCount; o++)
            {
                int triStart = RandomRange(networkRandom, 0, meshPoints.Length / 3) * 3; // get first index of each triangle

                float a = RandomValue(networkRandom);
                float b = RandomValue(networkRandom);

                if (a + b >= 1)
                { // reflect back if > 1
                    a = 1 - a;
                    b = 1 - b;
                }

                Vector3 newPointOnMesh1 = meshPoints[triStart] + (a * (meshPoints[triStart + 1] - meshPoints[triStart])) + (b * (meshPoints[triStart + 2] - meshPoints[triStart])); // apply formula to get new random point inside triangle

                newPointOnMesh1 = meshFilter.transform.TransformPoint(newPointOnMesh1); // convert back to worldspace

                GameObject fire = Instantiate(FlameEngine.instance.GetFireVFX(overrideVFXVariant, overrideFireVFX), new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0), FlameEngine.instance.fireParent);
                VisualEffect fireEffect = fire.GetComponent<VisualEffect>();

                FlameEngine.instance.AddFlame(fire);
                //Customization
                SetupFireConstants(fireEffect);

                //Place
                fireEffect.SetVector3("Spread_center", Vector3.zero);
                fireEffect.SetVector3("Box_size", new Vector3(0.2f, 0.2f, 0.2f));
                fireEffect.SetVector3("Box_center", newPointOnMesh1);
                fireEffect.SetVector3("Rotation", new Vector3());
                fireEffect.SetFloat("Spread_radius", 10000);

                ApplyNetworkSeed(fireEffect, o, true);

                fires.Add(fire.GetComponent<VisualEffect>());

                meshFilter = filters[RandomRange(networkRandom, 0, filters.Length)];
                meshObj = meshFilter.mesh;

                meshPoints = meshObj.vertices;
            }
        }

        private void GetFireEmissionBox(BoxCollider box, out Vector3 worldCenter, out Vector3 worldSize)
        {
            const float maximumSurfaceDepth = 0.04f;
            const float objectSurfaceClearance = 0.005f;
            const float wallSurfaceClearance = 0.12f;
            const float objectEdgeInset = 0.06f;

            Vector3 localSize = box.size;
            Vector3 halfSize = localSize * 0.5f;
            Vector3 fromCenter = box.transform.InverseTransformPoint(GetFireOrigin()) - box.center;

            Vector3 absoluteScale = new Vector3(
                Mathf.Abs(box.transform.lossyScale.x),
                Mathf.Abs(box.transform.lossyScale.y),
                Mathf.Abs(box.transform.lossyScale.z));

            bool isWall = IsWallCollider(box, localSize);
            if (!isWall && TryGetVisualEmissionBox(box, out worldCenter, out worldSize))
                return;

            int surfaceAxis = isWall
                ? (localSize.x * absoluteScale.x <= localSize.z * absoluteScale.z ? 0 : 2)
                : 1;
            float axisScale = Mathf.Max(absoluteScale[surfaceAxis], 0.0001f);
            float surfaceDepth = Mathf.Min(localSize[surfaceAxis], maximumSurfaceDepth / axisScale);
            float side = isWall && fromCenter[surfaceAxis] < 0f ? -1f : 1f;
            float clearance = isWall ? wallSurfaceClearance : objectSurfaceClearance;

            Vector3 localCenter = box.center;
            localCenter[surfaceAxis] += side *
                                        (halfSize[surfaceAxis] +
                                         (clearance / axisScale) +
                                         surfaceDepth * 0.5f);
            localSize[surfaceAxis] = surfaceDepth;

            if (!isWall)
            {
                // Keep top-surface flames just inside the object's footprint so
                // particles do not appear to float beyond appliance/furniture edges.
                localSize.x = Mathf.Max(0.01f, localSize.x - (objectEdgeInset * 2f / Mathf.Max(absoluteScale.x, 0.0001f)));
                localSize.z = Mathf.Max(0.01f, localSize.z - (objectEdgeInset * 2f / Mathf.Max(absoluteScale.z, 0.0001f)));
            }

            worldCenter = box.transform.TransformPoint(localCenter);
            worldSize = Vector3.Scale(localSize, absoluteScale);
        }

        private bool TryGetVisualEmissionBox(BoxCollider box, out Vector3 worldCenter, out Vector3 worldSize)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer != null && renderer.enabled &&
                                   !(renderer is ParticleSystemRenderer) &&
                                   renderer.bounds.Intersects(box.bounds))
                .ToArray();

            if (renderers.Length == 0)
            {
                worldCenter = default;
                worldSize = default;
                return false;
            }

            bool hasPoint = false;
            Vector3 localMin = default;
            Vector3 localMax = default;

            foreach (Renderer renderer in renderers)
            {
                Bounds rendererBounds = renderer.localBounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;

                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 rendererLocalPoint = new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            Vector3 boxLocalPoint = box.transform.InverseTransformPoint(
                                renderer.transform.TransformPoint(rendererLocalPoint));

                            if (!hasPoint)
                            {
                                localMin = boxLocalPoint;
                                localMax = boxLocalPoint;
                                hasPoint = true;
                            }
                            else
                            {
                                localMin = Vector3.Min(localMin, boxLocalPoint);
                                localMax = Vector3.Max(localMax, boxLocalPoint);
                            }
                        }
                    }
                }
            }

            Vector3 localCenter = (localMin + localMax) * 0.5f;
            Vector3 localSize = localMax - localMin;
            Vector3 absoluteScale = new Vector3(
                Mathf.Abs(box.transform.lossyScale.x),
                Mathf.Abs(box.transform.lossyScale.y),
                Mathf.Abs(box.transform.lossyScale.z));

            worldCenter = box.transform.TransformPoint(localCenter);
            worldSize = Vector3.Scale(localSize, absoluteScale);
            return worldSize.sqrMagnitude > 0.000001f;
        }

        private static bool IsWallCollider(BoxCollider box, Vector3 localSize)
        {
            Transform current = box.transform;
            while (current != null)
            {
                if (current.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                current = current.parent;
            }

            Vector3 scale = box.transform.lossyScale;
            float worldX = Mathf.Abs(localSize.x * scale.x);
            float worldY = Mathf.Abs(localSize.y * scale.y);
            float worldZ = Mathf.Abs(localSize.z * scale.z);
            return worldY > 1f && Mathf.Min(worldX, worldZ) <= 0.35f;
        }

        private static int RandomRange(System.Random networkRandom, int minInclusive, int maxExclusive)
        {
            return networkRandom != null
                ? networkRandom.Next(minInclusive, maxExclusive)
                : Random.Range(minInclusive, maxExclusive);
        }

        private static float RandomValue(System.Random networkRandom)
        {
            return networkRandom != null
                ? (float)networkRandom.NextDouble()
                : Random.Range(0f, 1f);
        }

        private Vector3 Clamp0360Vector(Vector3 vector)
        {
            return new Vector3(Clamp0360(vector.x), Clamp0360(vector.y), Clamp0360(vector.z));
        }

        private float Clamp0360(float eulerAngles)
        {
            float result = eulerAngles - Mathf.CeilToInt(eulerAngles / 360f) * 360f;
            if (result < 0)
            {
                result += 360f;
            }
            return result;
        }

        /// <summary>
        /// Has the object already burnt out?
        /// </summary>
        /// <returns></returns>
        public bool hasBurnedOut()
        {
            return onFireTimer > (burnOutStart_s + burnOutLength_s);
        }

        /// <summary>
        /// Was the object extinguished
        /// </summary>
        /// <returns></returns>
        public bool IsExtinguished()
        {
            return extinguished;
        }

        /// <summary>
        /// Returns the fire origin
        /// </summary>
        /// <returns></returns>
        public Vector3 GetFireOrigin()
        {
            return transform.TransformPoint(_fireOriginLocal);
        }

        /// <summary>
        /// Applies the shared ignition point after network metadata arrives. The
        /// point affects shader spread, emitter activation, lights, and TIC heat.
        /// </summary>
        public void ApplyNetworkFireOriginLocal(Vector3 localOrigin)
        {
            if ((_fireOriginLocal - localOrigin).sqrMagnitude <= 0.00000001f)
                return;

            _fireOriginLocal = localOrigin;

            if (!onFire)
                return;

            UpdateShaders();
            UpdateVFX();
            UpdateLights();
        }

        /// <summary>
        /// Return the radius of the current extinguish effect
        /// </summary>
        /// <returns>radius in m</returns>
        public float GetPutOutRadius()
        {
            return putOutRadius;
        }

        /// <summary>
        /// Return the center of the current extinguish effect
        /// </summary>
        /// <returns>World position</returns>
        public Vector3 GetPutOutCenter()
        {
            return transform.TransformPoint(putOutAreaCenter);
        }

        /// <summary>
        /// Returns current ignition progress
        /// </summary>
        /// <returns>ignition progress in seconds</returns>
        public float GetCurrentIgnitionProgress()
        {
            return currentIgnitionProgress_s;
        }

        /// <summary>
        /// Gets the approximated longest size of an object
        /// </summary>
        /// <returns></returns>
        public float GetObjectApproxSize()
        {
            return approxSize;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
            {
                Gizmos.color = Color.red;
                if (flammableColliders.Count == 0)
                {
                    foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>())
                    {
                        Gizmos.matrix = box.transform.localToWorldMatrix;
                        Gizmos.DrawWireCube(box.center, box.size + Vector3.Scale(flameCatchAreaAddition, new Vector3(1 / box.transform.lossyScale.x, 1 / box.transform.lossyScale.y, 1 / box.transform.lossyScale.z)));
                    }
                }
                else
                {
                    foreach (BoxCollider box in flammableColliders)
                    {
                        Gizmos.matrix = box.transform.localToWorldMatrix;
                        Gizmos.DrawWireCube(box.center, box.size + flameCatchAreaAddition);
                    }
                }
            }

        }

        private void OnDestroy()
        {
            foreach (VisualEffect fire in fires)
            {
                if (fire)
                {
                    FlameEngine.instance.RemoveFlame(fire.gameObject);
                    Destroy(fire.gameObject);
                }

            }
            foreach (FireTrigger trigger in fireTriggers)
            {
                if (trigger)
                    Destroy(trigger.gameObject);
            }
            fires.Clear();
            fireTriggers.Clear();
        }
    }

}
