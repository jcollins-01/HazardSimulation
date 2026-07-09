using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace Ignis
{
    public class FlameEngine : MonoBehaviour
    {
        [System.Serializable]
        public struct FlameVariantPrefabs
        {
            [Tooltip("Prefab for the box fire")]
            public GameObject fireBoxPrefab;

            [Tooltip("Prefab for the lightweight variant of fire")]
            public GameObject fireBoxLWVariantPrefab;

            [Tooltip("Prefab for the lightweight textured variant of fire")]
            public GameObject fireBoxLWTexturedVariantPrefab;

            [Tooltip("Prefab for the lightweight textured 2 variant of fire")]
            public GameObject fireBoxWildVariantPrefab;

            [Tooltip("Prefab for the Gentle variant of fire")]
            public GameObject fireBoxGentleVariantPrefab;

            [Tooltip("Prefab for the Gentle Stylized variant of fire")]
            public GameObject fireBoxGentleStylizedVariantPrefab;

            [Tooltip("Prefab for the Old-school variant of fire")]
            public GameObject fireBoxOldSchoolVariantPrefab;

            [Tooltip("Prefab for the Old-school variant of fire")]
            public GameObject onlySmokeVariantPrefab;

            [Tooltip("Prefab for the custom variant of fire")]
            public GameObject fireBoxCustomVariantPrefab;
        }



        public static FlameEngine _instance;

        public static FlameEngine instance
        {
            get
            {
                if (_instance == null)
                    _instance = (FlameEngine)FindObjectOfType(typeof(FlameEngine));
                return _instance;
            }
        }

        public enum FireVFXVariant
        {
            LegacyOriginal,
            LegacyLightweight,
            Lively,
            Wild,
            Gentle,
            Simple,
            OldSchool,
            OnlySmoke,
            Custom
        }


        [HideInInspector]
        [Tooltip("Where to parent the flames")]
        public Transform fireParent;

        [HideInInspector]
        [Tooltip("Where to parent the triggers")]
        public Transform triggerParent;

        [HideInInspector]
        [Tooltip("Where to parent the runtime trees")]
        public Transform runTimeTreeParent;

        [HideInInspector]
        [Tooltip("Where to parent the VSPro masks")]
        public Transform vsProMaskParent;

        [Tooltip("All different flame variants")]
        public FlameVariantPrefabs flameVariantPrefabs;

        [Tooltip("CANNOT MODIFY AT RUNTIME. VFX variant of the flame VFX. Only use High quality if you want few very high quality flames. If there is a lot of flames high quality might spawn too much particles")]
        public FireVFXVariant fireVFXVariant = FireVFXVariant.Wild;

        [HideInInspector]
        [Tooltip("Prefab for the triggers")]
        public GameObject fireTriggerPrefab;

        [HideInInspector]
        [Tooltip("Default Fire SFX")]
        public GameObject defaultFireSFX;

        [HideInInspector]
        public WindRetrieve flameWindRetriever;

        [HideInInspector]
        public Shader flameableShader;

        [Header("Compatibility")]
        [Tooltip("List of all compatible shaders. Shaders not in project will be removed at Runtime Start()")]
        public List<OAVAShaderCompatibilitySO> compatibleShaders = new List<OAVAShaderCompatibilitySO>();

        [Tooltip("---Remember to backup your terrain data before using! Ignis modifies terrain data and data can be lost in case of blue screen!--- Is Ignis compatible with Unity Terrain? If you do not want terrain trees to burn enabling this will affect negatively on performance")]
        public bool UnityTerrainCompatible = false;

        [Tooltip("Is Ignis VSPro Compatible? If you don't want to burn Vegetation Studio Vegetation leave this off, otherwise it will negatively impact your performance")]
        public bool VegetationStudioProCompatible = false;

        [Header("Debug")]
        [Tooltip("Set all objects on fire on start.")]
        public bool fireOnStart = false;

        [Header("Performance")]
        [Range(0, 2)]
        [Tooltip("Performance/quality multiplier")]
        public float globalFireVFXMultiplier = 1;

        [Range(0.5f, 60)]
        [Tooltip("How often do we check for collisions with flame in second? Smaller = more efficient, bigger = more realistic. Value 5 = 5 times per second.")]
        public float FlameTriggerCollisionCheckFrequency = 5;

        [Tooltip("Option to enable the on touch collision check (If you have e.g. snow on the TVE rock which you want to melt)")]
        public bool enableOnTouchChecks = false;

        [Tooltip("Allows modifying constant flame attributes on runtime. This will impact performance. Disable if you do not need to scale the object/modify flames on runtime to gain small performance boost.")]
        public bool modifyFlamesOnRuntime = true;

        [Tooltip("In Meters/Units. Flames will be fully culled when main camera is this far away from the flames.")]
        [Range(1, 1000)]
        public float flameLodCullDistance = 100;


        [Tooltip("Percentage. Flame particle count starts to linearly decrease at this percentage far away to cull distance. " +
            "E.g. If this is 0.2 and flameLodCullDistance is 10, the flame will have full particle count until 0.2*10 = 2 meters/units and then linearly decrease. " +
            "The particle count would then be half when main camera is 6 meters from the flame and zero when main camera is 10 meters from the flame. Value 1 means that flames will not fade and will be instantly culled at culldistance.")]
        [Range(0, 1)]
        public float flameLodStartToFadePercentage = 0.2f;

        [HideInInspector]
        public bool creatingDebug = false;

        [HideInInspector]
        public bool pause = false;

        [Tooltip("Can be used to optimize performance and not light up too many flames at the same time. Note that whole objects with multiple colliders still flame up all at once so the real number can be little bit over the limit.")]
        [Range(5, 1000)]
        public int maxFlamesInWorld = 500;

        [HideInInspector]
        public List<GameObject> currentFlames = new List<GameObject>();

        private List<FlameCollisionCallbacks> flameCollisionCallbacks;

#if VEGETATION_STUDIO_PRO
        [HideInInspector]
        public AwesomeTechnologies.VegetationSystem.VegetationSystemPro vsPro;
#endif

        private void Awake()
        {
            if (_instance != null) Destroy(instance);

            _instance = this;
        }

        public void Start()
        {
#if VEGETATION_STUDIO_PRO
            vsPro = FindObjectOfType<AwesomeTechnologies.VegetationSystem.VegetationSystemPro>();
#endif

            for (int i = 0; i < compatibleShaders.Count; i++)
            {
                OAVAShaderCompatibilitySO item = compatibleShaders[i];

                if (item == null)
                {
                    compatibleShaders.RemoveAt(i);
                    i--;
                }
                else if (!item.ShaderName.Equals(string.Empty) && !Shader.Find(item.ShaderName))
                {
                    compatibleShaders.RemoveAt(i);
                    i--;
                }
            }

        }

        public void AddFlame(GameObject flame)
        {
            currentFlames.Add(flame);
        }

        public void RemoveFlame(GameObject flame)
        {
            currentFlames.Remove(flame);
        }

        public int FlameCount()
        {
            return currentFlames.Count;
        }

        public void PauseFlames()
        {
            pause = true;

            foreach (VisualEffect eff in fireParent.GetComponentsInChildren<VisualEffect>())
            {
                eff.pause = true;
            }

            foreach (AudioSource audioSource in fireParent.GetComponentsInChildren<AudioSource>())
            {
                audioSource.Pause();
            }

            triggerParent.gameObject.SetActive(false);
        }

        public void ResumeFlames()
        {
            pause = false;

            foreach (VisualEffect eff in fireParent.GetComponentsInChildren<VisualEffect>())
            {
                eff.pause = false;
            }

            foreach (AudioSource audioSource in fireParent.GetComponentsInChildren<AudioSource>())
            {
                audioSource.Play();
            }

            triggerParent.gameObject.SetActive(true);
        }

        /// <summary>
        /// Gets VFX prefab of selected VFX variant in the FlameEngine or vfx variant provided in the parameter if useOverride is true.
        /// </summary>
        /// <returns>Selected VFX prefab</returns>
        public GameObject GetFireVFX(FireVFXVariant overrideVariant, bool useOverride)
        {
            if (useOverride)
            {
                return GetFireVFX(overrideVariant);
            }
            else
            {
                return GetFireVFX(fireVFXVariant);
            }

        }

        /// <summary>
        /// Gets VFX prefab of selected VFX variant in the FlameEngine.
        /// </summary>
        /// <returns>Selected VFX prefab</returns>
        public GameObject GetFireVFX()
        {
            return GetFireVFX(fireVFXVariant);
        }

        /// <summary>
        /// Gets VFX prefab of vfx variant in parameter.
        /// </summary>
        /// <returns>parameter VFX prefab</returns>
        public GameObject GetFireVFX(FireVFXVariant variant)
        {
            if (variant == FireVFXVariant.LegacyOriginal)
            {
                return flameVariantPrefabs.fireBoxPrefab;
            }
            else if (variant == FireVFXVariant.LegacyLightweight)
            {
                return flameVariantPrefabs.fireBoxLWVariantPrefab;
            }
            else if (variant == FireVFXVariant.Lively)
            {
                return flameVariantPrefabs.fireBoxLWTexturedVariantPrefab;
            }
            else if (variant == FireVFXVariant.Wild)
            {
                return flameVariantPrefabs.fireBoxWildVariantPrefab;
            }
            else if (variant == FireVFXVariant.Gentle)
            {
                return flameVariantPrefabs.fireBoxGentleVariantPrefab;
            }
            else if (variant == FireVFXVariant.Simple)
            {
                return flameVariantPrefabs.fireBoxGentleStylizedVariantPrefab;
            }
            else if (variant == FireVFXVariant.OldSchool)
            {
                return flameVariantPrefabs.fireBoxOldSchoolVariantPrefab;
            }
            else if (variant == FireVFXVariant.OnlySmoke)
            {
                return flameVariantPrefabs.onlySmokeVariantPrefab;
            }
            else
            {
                return flameVariantPrefabs.fireBoxCustomVariantPrefab;
            }
        }

        /// <summary>
        /// Gets VFX prefab of selected VFX variant.
        /// </summary>
        /// <returns>Selected VFX prefab</returns>


        public List<OAVAShaderCompatibilitySO> GetCompatibleShaders()
        {
            return compatibleShaders;
        }

        public List<FlameCollisionCallbacks> GetCollisionCallbacks()
        {
            if (flameCollisionCallbacks == null)
            {
                flameCollisionCallbacks = GetComponents<FlameCollisionCallbacks>().ToList();
            }
            return flameCollisionCallbacks;
        }

        public GameObject MaskInstanceAndSpawnAPrefabIfNecessary(GameObject other)
        {
#if VEGETATION_STUDIO_PRO
            AwesomeTechnologies.Vegetation.VegetationItemInstanceInfo vegetationItemInstanceInfo = other.gameObject.GetComponent<AwesomeTechnologies.Vegetation.VegetationItemInstanceInfo>();

            // In case of "From prefab" collider
            if (!vegetationItemInstanceInfo && other.transform.parent)
                vegetationItemInstanceInfo = other.transform.parent.GetComponent<AwesomeTechnologies.Vegetation.VegetationItemInstanceInfo>();

            if (vegetationItemInstanceInfo)
            {
                FlammableObject flamCheck = vegetationItemInstanceInfo.gameObject.GetComponentInChildren<FlammableObject>();
                if (!flamCheck)
                {
                    if (!runTimeTreeParent.GetComponentsInChildren<VegetationStudioProTreeUnMasker>().Any(o => o.VegetationInstanceItemId == vegetationItemInstanceInfo.VegetationItemInstanceID))
                    {
                        AwesomeTechnologies.Vegetation.RuntimeObjectInfo runInfo = vegetationItemInstanceInfo.gameObject.GetComponent<AwesomeTechnologies.Vegetation.RuntimeObjectInfo>();

                        AwesomeTechnologies.VegetationSystem.VegetationItemInfoPro info = runInfo.VegetationItemInfo;
                        if (info != null)
                        {
                            GameObject tree = Instantiate(info.VegetationPrefab, vegetationItemInstanceInfo.Position, vegetationItemInstanceInfo.Rotation, FlameEngine.instance.runTimeTreeParent);
                            tree.transform.localScale = vegetationItemInstanceInfo.Scale;
                            FlammableObject flamTreeObj = tree.GetComponentInChildren<FlammableObject>();

                            if (flamTreeObj)
                            {
                                GameObject vegetationItemMaskObject = new GameObject { name = "VegetationItemMask - " + other.gameObject.name };
                                vegetationItemMaskObject.transform.parent = vsProMaskParent;
                                vegetationItemMaskObject.transform.position = vegetationItemInstanceInfo.Position;

                                vegetationItemMaskObject.AddComponent<AwesomeTechnologies.Vegetation.Masks.VegetationItemMask>().SetVegetationItemInstanceInfo(vegetationItemInstanceInfo);

                                VegetationStudioProTreeUnMasker unMasker = tree.AddComponent<VegetationStudioProTreeUnMasker>();
                                unMasker.mask = vegetationItemMaskObject;
                                unMasker.VegetationInstanceItemId = vegetationItemInstanceInfo.VegetationItemInstanceID;
                                unMasker.flammable = flamTreeObj;
                            }
                            else
                            {
                                Destroy(tree);
                            }

                            return vegetationItemInstanceInfo.gameObject;
                        }
                    }
                }
            }
#endif
            return null;
        }

    }
}
