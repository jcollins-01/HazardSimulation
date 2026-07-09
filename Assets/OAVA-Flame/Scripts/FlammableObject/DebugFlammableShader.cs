#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ignis
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(FlammableObject))]
    public class DebugFlammableShader : MonoBehaviour
    {
        [HideInInspector]
        public FlammableObject flam;

        [HideInInspector]
        public List<Renderer> rends;

        [HideInInspector]
        public Material[] debugMats;

        [HideInInspector]
        public GameObject flamCopy;

        private float onFireTimer = 0;

        [Header("Enable Gizmos to force scene update")]
        [Tooltip("Gradually increases spread status")]
        public bool animateFireSpreadStatus = false;

        [Range(0, 1)]
        [Tooltip("Drag this value to animate the spread")]
        public float fireSpreadStatus = 0;

        private float fireSpread = 0;

        [Tooltip("This has only effect with Ignis shader")]
        public Vector3 _fireOriginLocal = Vector3.zero;

        private float approxSize = 0;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                flam = GetComponent<FlammableObject>();
            }
        }

        private void SetupDebugMaterials()
        {
            if (flamCopy != null)
            {
                DestroyImmediate(flamCopy);
            }

            flamCopy = Instantiate(flam.gameObject, flam.transform.parent);
            flamCopy.name += "_debug";

            FlameEngine.instance.creatingDebug = true;
            //Do not destroy other debug objects
            DebugFlammableVFXBox debugVFX = flamCopy.GetComponent<DebugFlammableVFXBox>();
            if (debugVFX)
            {
                debugVFX.fires.Clear();
                debugVFX.fires = null;
                debugVFX.tempColliders.Clear();
                debugVFX.tempColliders = null;
                DestroyImmediate(flamCopy.GetComponent<DebugFlammableVFXBox>());
            }
            DestroyImmediate(flamCopy.GetComponent<DebugFlammableShader>());
            DestroyImmediate(flamCopy.GetComponent<FlammableObject>());

            foreach (Renderer rend in flam.GetComponentsInChildren<Renderer>())
            {
                rend.enabled = false;
            }

            if (flam.customFireOriginOnStart)
            {
                _fireOriginLocal = flam.customFireOriginOnStart.position;
            }

            CalculateApproxSize();
            rends = flamCopy.gameObject.GetComponentsInChildren<Renderer>().ToList();
            foreach (Renderer child in rends)
            {
                debugMats = new Material[child.sharedMaterials.Length];

                for (int i = 0; i < child.sharedMaterials.Length; i++)
                {
                    debugMats[i] = new Material(child.sharedMaterials[i]);
                    debugMats[i].name = debugMats[i] + "_Debug";
                }

                child.sharedMaterials = debugMats;
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
            if(!Application.isPlaying)
            {
                if (animateFireSpreadStatus)
                {
                    fireSpreadStatus += 0.001f;
                }
                
                fireSpread = fireSpreadStatus * (flam.burnOutStart_s + flam.burnOutLength_s);
                onFireTimer = fireSpread;
                if(fireSpread <= 0) 
                {
                    if(flamCopy != null)
                    {
                        Clean();
                    }
                } 
                else
                {
                    if (flamCopy == null)
                    {
                        SetupDebugMaterials();
                    }
                }

                UpdateShaders();
            }
                
        }

        private void CalculateApproxSize()
        {
            List<BoxCollider> boxCols = flam.GetComponentsInChildren<BoxCollider>().ToList();

            if(boxCols.Count > 0)
            {
                foreach(BoxCollider box in boxCols)
                {
                    approxSize += box.size.magnitude;
                }
            } 
            else
            {
                if (rends.Count <= 0)
                {
                    approxSize = 1;
                }
                else
                {
                    Bounds combinedRendBounds = rends[0].bounds;

                    foreach (Renderer rend in rends)
                    {
                        if (rend != rends[0]) combinedRendBounds.Encapsulate(rend.bounds);
                    }

                    approxSize = combinedRendBounds.size.magnitude;
                }
            }
        }

        private void UpdateShaders()
        {
            if (!flamCopy) return;

            if (rends.Count > 0)
            {
                foreach (Renderer rend in rends)
                {
                    for (int i = 0; i < rend.sharedMaterials.Length; i++)
                    {
                        if (flam.flammableMaterialIndexes.Count <= 0 || flam.flammableMaterialIndexes.Contains(i))
                        {
                            
                            if (rend.sharedMaterials[i].shader == FlameEngine.instance.flameableShader)
                            {
                                SetupIgnisShader(rend, i);
                                UpdateIgnisShader(rend, i);
                                
                            }
                            else
                            {
                                
                                KeywordsEnableCompatibleShaders(rend, i);
                                UpdateSupportedThirdPartyShaders(rend, i);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateIgnisShader(Renderer rend, int i)
        {
            rend.sharedMaterials[i].SetFloat("Fire_spread", fireSpread);
            rend.sharedMaterials[i].SetVector("Fire_origin", transform.TransformPoint(_fireOriginLocal));

            if (onFireTimer >= flam.burnOutStart_s)
            {
                rend.sharedMaterials[i].SetFloat("Fire_bright",
                    Mathf.MoveTowards(rend.sharedMaterials[i].GetFloat("Fire_bright"), 0, (1 / flam.burnOutLength_s) * Time.deltaTime));
            }
            else
            {

                rend.sharedMaterials[i].SetFloat("Fire_bright",
                Mathf.MoveTowards(rend.sharedMaterials[i].GetFloat("Fire_bright"), 1, (1 / flam.achieveMaxBrightness_s) * Time.deltaTime));

                rend.sharedMaterials[i].SetFloat("Charred_wood_blend",
                Mathf.Clamp(onFireTimer / flam.burnOutStart_s, 0, 1));
            }
            DynamicGI.SetEmissive(rend, flam.shaderEmissionColor * flam.shaderEmissionMultiplier * rend.sharedMaterials[i].GetFloat("Fire_bright"));
        }

        private void UpdateSupportedThirdPartyShaders(Renderer rend, int a)
        {
            UpdateCompatibleShaders(rend, a);
        }

        private void UpdateCompatibleShaders(Renderer rend, int a)
        {
            List<OAVAShaderCompatibilitySO> compShaders = FlameEngine.instance.GetCompatibleShaders();
            foreach (OAVAShaderCompatibilitySO shaderComp in compShaders)
            {
                if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderCheckProperty) ||
                    (shaderComp.ShaderCheckProperty.Equals(string.Empty) && rend.sharedMaterials[a].shader.name.ToLower().Equals(shaderComp.ShaderName.ToLower())))
                {
                    if (onFireTimer > flam.burnOutStart_s)
                    {
                        float timeSinceBurnOutStart = onFireTimer - flam.burnOutStart_s;
                        float percentageToburnOutEnd = timeSinceBurnOutStart / (flam.burnOutLength_s);
                        if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderMainColorPropertyName))
                        {
                            rend.sharedMaterials[a].SetColor(shaderComp.ShaderMainColorPropertyName, Color.Lerp(rend.sharedMaterials[a].GetColor(shaderComp.ShaderMainColorPropertyName), flam.shaderBurntColor, percentageToburnOutEnd * flam.shaderToBurntInterpolateSpeed));
                        }
                        else if (!shaderComp.ShaderMainColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + rend.sharedMaterials[a].shader.name + " does not contain color property: " + shaderComp.ShaderMainColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderEmissionColorPropertyName))
                        {
                            rend.sharedMaterials[a].SetColor(shaderComp.ShaderEmissionColorPropertyName, Color.Lerp(rend.sharedMaterials[a].GetColor(shaderComp.ShaderEmissionColorPropertyName), flam.shaderBurntColor, percentageToburnOutEnd * flam.shaderToBurntInterpolateSpeed));
                        }
                        else if (!shaderComp.ShaderEmissionColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + rend.sharedMaterials[a].shader.name + " does not contain color property: " + shaderComp.ShaderEmissionColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        foreach (OAVAShaderCompatibilitySO.ShaderProperty prop in shaderComp.onBurnoutChangeProperties)
                        {
                            if (rend.sharedMaterials[a].HasProperty(prop.name))
                            {
                                rend.sharedMaterials[a].SetFloat(prop.name, Mathf.MoveTowards(rend.sharedMaterials[a].GetFloat(prop.name), prop.targetValue, Time.deltaTime * prop.speedMultiplier));
                            }
                        }
                    }
                    else
                    {
                        if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderMainColorPropertyName))
                        {
                            rend.sharedMaterials[a].SetColor(shaderComp.ShaderMainColorPropertyName,
                            new Color(flam.shaderEmissionColor.r * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1),
                                flam.shaderEmissionColor.g * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1),
                                flam.shaderEmissionColor.b * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1))
                                * ((1 - flam.shaderColorNoise) + (Mathf.PerlinNoise(onFireTimer * flam.shaderColorNoiseSpeed, 0)) * (flam.shaderColorNoise * 2)));
                        }
                        else if (!shaderComp.ShaderMainColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + rend.sharedMaterials[a].shader.name + " does not contain color property: " + shaderComp.ShaderMainColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderEmissionColorPropertyName))
                        {
                            rend.sharedMaterials[a].SetColor(shaderComp.ShaderEmissionColorPropertyName,
                            new Color(flam.shaderEmissionColor.r * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1),
                                flam.shaderEmissionColor.g * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1),
                                flam.shaderEmissionColor.b * flam.shaderEmissionMultiplier * Mathf.Clamp((fireSpread) / (approxSize), 0, 1))
                                * ((1 - flam.shaderColorNoise) + (Mathf.PerlinNoise(onFireTimer * flam.shaderColorNoiseSpeed, 0)) * (flam.shaderColorNoise * 2)));
                            DynamicGI.SetEmissive(rend, rend.sharedMaterials[a].GetColor(shaderComp.ShaderEmissionColorPropertyName));
                        }
                        else if (!shaderComp.ShaderEmissionColorPropertyName.Equals(string.Empty))
                        {
                            Debug.LogWarning("Shader " + rend.sharedMaterials[a].shader.name + " does not contain color property: " + shaderComp.ShaderEmissionColorPropertyName + " which has been added to ShaderCompabilities");
                        }

                        foreach (OAVAShaderCompatibilitySO.ShaderProperty prop in shaderComp.onFireChangeProperties)
                        {
                            if (rend.sharedMaterials[a].HasProperty(prop.name))
                            {
                                rend.sharedMaterials[a].SetFloat(prop.name, Mathf.MoveTowards(rend.sharedMaterials[a].GetFloat(prop.name), prop.targetValue, Time.deltaTime * prop.speedMultiplier));
                            }
                        }
                    }
                    break;
                }
            }
        }

        private void SetupIgnisShader(Renderer rend, int i)
        {
            rend.sharedMaterials[i].SetVector("Fire_origin", transform.TransformPoint(_fireOriginLocal));
            rend.sharedMaterials[i].SetFloat("Voronoi_move", flam.shaderColorNoiseSpeed * 2);
            rend.sharedMaterials[i].SetColor("Fire_color", flam.shaderEmissionColor);
            rend.sharedMaterials[i].SetFloat("Fire_max_brightness", flam.shaderEmissionMultiplier);
            rend.sharedMaterials[i].SetFloat("Fire_min_brightness", flam.shaderEmissionMultiplier / 50);
        }

        private void KeywordsEnableCompatibleShaders(Renderer rend, int a)
        {
            List<OAVAShaderCompatibilitySO> compShaders = FlameEngine.instance.GetCompatibleShaders();
            foreach (OAVAShaderCompatibilitySO shaderComp in compShaders)
            {
                if (rend.sharedMaterials[a].HasProperty(shaderComp.ShaderCheckProperty) ||
                    (shaderComp.ShaderCheckProperty.Equals(string.Empty) && rend.sharedMaterials[a].shader.name.ToLower().Equals(shaderComp.ShaderName.ToLower())))
                {
                    foreach (string keyword in shaderComp.onFireStartEnableKeywords)
                    {
                        rend.sharedMaterials[a].EnableKeyword(keyword);

                    }
                    foreach (MaterialGlobalIlluminationFlags flag in shaderComp.onFireStartEnableIlluminationFlag)
                    {
                        rend.sharedMaterials[a].globalIlluminationFlags = flag;
                    }
                    break;
                }
            }
        }



        private void OnDisable()
        {
            Clean();
        }

        private void Clean()
        {
            if(debugMats != null)
            {
                foreach(Material mat in debugMats)
                {
                    DestroyImmediate(mat);
                }
                debugMats = null;
            }
            if (flamCopy)
            {
                DestroyImmediate(flamCopy);
            }
            flamCopy = null;
            foreach (Renderer rend in flam.GetComponentsInChildren<Renderer>())
            {
                rend.enabled = true;
            }

            if(rends != null)
            {
                rends.Clear();
                rends = null;
            }
            
            approxSize = 0;
            onFireTimer = 0;
            fireSpread = 0;
        }

        private void OnDestroy()
        {
            Clean();
        }

        void OnDrawGizmos()
        {
            // Your gizmo drawing thing goes here if required...
            // Ensure continuous Update calls.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
        }
    }
}
#endif
