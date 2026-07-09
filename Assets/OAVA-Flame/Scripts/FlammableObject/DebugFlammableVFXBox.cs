using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace Ignis
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(FlammableObject))]
    public class DebugFlammableVFXBox : MonoBehaviour
    {
        private FlammableObject flam;

        [HideInInspector]
        public List<BoxCollider> flammableColliders;

        [HideInInspector]
        public List<BoxCollider> tempColliders;

        [HideInInspector]
        public List<VisualEffect> fires;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                if (fires == null)
                {
                    fires = new List<VisualEffect>();
                }

                flam = GetComponent<FlammableObject>();

                if (fires.Count > 0) return;

                if (flam.calculateFlammationAreaFromMesh == FlammableObject.CalculationArea.None)
                {
                    if (flam.flammableColliders.Count <= 0)
                        flammableColliders = GetComponentsInChildren<BoxCollider>().ToList();
                    else
                        flammableColliders = new List<BoxCollider>(flam.flammableColliders);
                }
                else
                {
                    GenerateColliderFromMesh(flam.gameObject);
                    flammableColliders = tempColliders;
                }

                if (fires.Count <= 0)
                {
                    if (flam.useMeshFire)
                    {
                        SetMeshOnFire();
                    }
                    else
                    {
                        SetBoxesOnFire();
                    }

                }
            }
        }


        private void Awake()
        {
            if (Application.isPlaying)
            {
                this.enabled = false;
            }

        }

        // Update is called once per frame
        void Update()
        {
            if (fires != null && !Application.isPlaying)
            {
                for (int i = 0; i < fires.Count; i++)
                {
                    VisualEffect fire = fires[i];

                    if (!fire)
                    {
                        fires.RemoveAt(i);
                        continue;
                    }

                    SetupFireConstants(fire);


                    if (!flam.useMeshFire)
                    {
                        BoxCollider box = flammableColliders[i];
                        if (box)
                        {
                            if (fire.HasVector3("Box_center"))
                            {
                                fire.SetVector3("Box_center", box.transform.TransformPoint(box.center));
                                fire.SetVector3("Rotation", box.transform.rotation.eulerAngles);
                                fire.SetVector3("Box_size", Vector3.Scale(box.size, box.transform.lossyScale));
                            }
                        }
                        else
                        {
                            flammableColliders.RemoveAt(i);
                        }


                    }

#if UNITY_EDITOR
                    if (Application.isEditor)
                    {
                        fire.SetVector3("AdditionalCameraPosition", UnityEditor.SceneView.lastActiveSceneView.camera.transform.position);
                    }
#endif
                    if (FlameEngine.instance)
                    {
                        if (FlameEngine.instance.flameWindRetriever.OnUse() && flam.affectedByWind && fire.HasVector3("WindForce"))
                        {
                            fire.SetVector3("WindForce", FlameEngine.instance.flameWindRetriever.GetCurrentWindVelocity());
                        }
                    }


                }
            }

        }

        private void SetMeshOnFire()
        {
            MeshFilter[] filters;
            if (flam.meshFireMeshFilters.Count > 0)
            {
                filters = flam.meshFireMeshFilters.ToArray();
            }
            else
            {
                filters = transform.GetComponentsInChildren<MeshFilter>();
            }


            MeshFilter meshFilter = filters[Random.Range(0, filters.Length)];
            Mesh meshObj = meshFilter.sharedMesh;

            Vector3[] meshPoints = meshObj.vertices;
            for (int o = 0; o < flam.meshFireCount; o++)
            {
                int triStart = Random.Range(0, meshPoints.Length / 3) * 3; // get first index of each triangle

                float a = Random.Range(0f, 1f);
                float b = Random.Range(0f, 1f);

                if (a + b >= 1)
                { // reflect back if > 1
                    a = 1 - a;
                    b = 1 - b;
                }

                Vector3 newPointOnMesh1 = meshPoints[triStart] + (a * (meshPoints[triStart + 1] - meshPoints[triStart])) + (b * (meshPoints[triStart + 2] - meshPoints[triStart])); // apply formula to get new random point inside triangle

                newPointOnMesh1 = meshFilter.transform.TransformPoint(newPointOnMesh1); // convert back to worldspace

                GameObject fire = Instantiate(FlameEngine.instance.GetFireVFX(flam.overrideVFXVariant, flam.overrideFireVFX), new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0), FlameEngine.instance.fireParent);
                VisualEffect fireEffect = fire.GetComponent<VisualEffect>();

                //Customization
                SetupFireConstants(fireEffect);

                //Place
                fireEffect.SetVector3("Spread_center", Vector3.zero);
                fireEffect.SetVector3("Box_size", new Vector3(0.2f, 0.2f, 0.2f));
                fireEffect.SetVector3("Box_center", newPointOnMesh1);
                fireEffect.SetVector3("Rotation", new Vector3());
                fireEffect.SetFloat("Spread_radius", 10000);

                fires.Add(fire.GetComponent<VisualEffect>());

                meshFilter = filters[Random.Range(0, filters.Length)];
                meshObj = meshFilter.sharedMesh;

                meshPoints = meshObj.vertices;
            }
        }

        private void SetBoxesOnFire()
        {
            foreach (BoxCollider box in flammableColliders)
            {
                GameObject fire = Instantiate(FlameEngine.instance.GetFireVFX(flam.overrideVFXVariant, flam.overrideFireVFX), new Vector3(0, 0, 0), Quaternion.Euler(0, 0, 0), FlameEngine.instance.fireParent);
                VisualEffect fireEffect = fire.GetComponent<VisualEffect>();

                SetupFireConstants(fireEffect);

                //Place
                fireEffect.SetVector3("Spread_center", Vector3.zero);
                fireEffect.SetVector3("Box_size", Vector3.Scale(box.size, box.transform.lossyScale));
                fireEffect.SetVector3("Box_center", box.transform.TransformPoint(box.center));
                fireEffect.SetVector3("Rotation", box.transform.rotation.eulerAngles);
                fireEffect.SetFloat("Spread_radius", 10000);

                fires.Add(fire.GetComponent<VisualEffect>());
            }
        }

        private void SetupFireConstants(VisualEffect fireEffect)
        {
            //Customization
            fireEffect.SetFloat("FireParticleMultiplier", FlameEngine.instance.globalFireVFXMultiplier);
            fireEffect.SetFloat("FireVFXMultiplier", flam.flameVFXMultiplier);
            fireEffect.SetFloat("FlameLength", flam.flameLength / 4);
            fireEffect.SetFloat("FlameSpeed", flam.flameEnvironmentalSpeed);
            fireEffect.SetFloat("FlameLiveliness", flam.flameLiveliness);
            fireEffect.SetFloat("FlameLivelinessSpeed", flam.flameLivelinessSpeed);
            fireEffect.SetFloat("FlameParticleSize", flam.flameParticleSize);
            fireEffect.SetVector2("SizeChangeRange", flam.flameAreaNoiseMinMaxMultiplier);
            fireEffect.SetVector2("BurstDelayRange", flam.flameBurstDelayMinMax);
            fireEffect.SetFloat("WindMultiplier", flam.windForceMultiplier);

            fireEffect.SetFloat("CullingDist", FlameEngine.instance.flameLodCullDistance);
            fireEffect.SetFloat("LODMaxDist", FlameEngine.instance.flameLodCullDistance * FlameEngine.instance.flameLodStartToFadePercentage);

            //Color
            fireEffect.SetVector4("FlameColor", new Vector4(flam.fireColor.r * flam.colorIntensityMultiplier, flam.fireColor.g * flam.colorIntensityMultiplier, flam.fireColor.b * flam.colorIntensityMultiplier, flam.fireColor.a));
            fireEffect.SetFloat("FlameColorBlend", flam.fireColorBlend);

            //Smoke
            fireEffect.SetVector4("SmokeColor", new Vector4(flam.smokeColor.r * flam.smokeColorIntensity, flam.smokeColor.g * flam.smokeColorIntensity, flam.smokeColor.b * flam.smokeColorIntensity, flam.smokeColor.a));
            fireEffect.SetFloat("SmokeAlpha", flam.smokeAlpha);
            fireEffect.SetFloat("SmokeVFXMultiplier", flam.smokeVFXMultiplier);
            fireEffect.SetFloat("SmokeParticleSize", flam.smokeParticleSize);

            // Embers
            fireEffect.SetFloat("EmbersVFXMultiplier", flam.embersVFXMultiplier);
            fireEffect.SetFloat("EmbersBurstVFXMultiplier", flam.embersBurstVFXMultiplier);
            fireEffect.SetFloat("EmbersParticleSize", flam.embersParticleSize);
            fireEffect.SetVector2("EmbersBurstDelayMinMax", flam.embersBurstDelayMinMax);
        }

        private void GenerateColliderFromMesh(GameObject flamObj)
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
                name = "Debug Flame Spawn"
            };
            collider.transform.position = combinedRendBounds.center;
            collider.transform.localScale = new Vector3(1, 1, 1);

            BoxCollider colliderComp = collider.AddComponent<BoxCollider>();
            colliderComp.isTrigger = true;

            colliderComp.size = combinedRendBounds.size;
            if (flam.calculateFlammationAreaFromMesh == FlammableObject.CalculationArea.Vegetation)
            {
                collider.transform.position = new Vector3(collider.transform.position.x, combinedRendBounds.min.y, collider.transform.position.z);
                colliderComp.size = new Vector3(colliderComp.size.x * 0.8f, 0.1f, colliderComp.size.z * 0.8f);
            }

            collider.transform.parent = this.transform;

            collider.transform.localRotation = Quaternion.Euler(Vector3.zero);

            flamObj.transform.rotation = origRot;

            tempColliders = new List<BoxCollider>
            {
                colliderComp
            };
        }

        private void OnDisable()
        {
            Clean();
        }

        private void Clean()
        {
            if (fires != null)
            {
                foreach (VisualEffect fire in fires)
                {
                    if (fire)
                        DestroyImmediate(fire.gameObject);
                }
                fires.Clear();
                fires = null;
            }

            if (tempColliders != null)
            {
                foreach (BoxCollider tempCol in tempColliders)
                {
                    if (tempCol)
                        DestroyImmediate(tempCol.gameObject);
                }
                tempColliders.Clear();
                tempColliders = null;
            }

            if (flammableColliders != null)
            {
                flammableColliders.Clear();
                flammableColliders = null;
            }
        }

        private void OnDestroy()
        {
            Clean();
        }
    }
}
