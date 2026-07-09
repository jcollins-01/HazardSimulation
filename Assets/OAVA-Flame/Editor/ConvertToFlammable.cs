using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ignis
{
    public class ConvertToFlammable : EditorWindow
    {
        public GameObject flameEnginePrefab;
        public List<GameObject> flammableObjs = new List<GameObject>();
        public Dictionary<Material, bool> materialCheckList = new Dictionary<Material, bool>();
        public float minDistBetweenBones = 0.2f;
        public float minDistBetweenAny = 0.1f;
        public bool isVegetation = false;
        public bool isSkinnedMesh = false;
        public bool automaticBones = true;
        public bool isTree = false;
        HashSet<Shader> supportedShaders;
        Vector2 scrollPos = Vector2.zero;
        private void OnEnable()
        {
            supportedShaders = new HashSet<Shader>()
        {
            Shader.Find("Universal Render Pipeline/Lit"),
            Shader.Find("Universal Render Pipeline/Unlit"),
            Shader.Find("HDRP/Lit"),
            Shader.Find("HDRP/Unlit")
        };
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Object", false, 10)]
        static void Convert(MenuCommand menuCommand)
        {
            ConvertToFlammable window = (ConvertToFlammable)EditorWindow.GetWindow(typeof(ConvertToFlammable));
            GameObject go = menuCommand.context as GameObject;
            
            window.isVegetation = false;
            window.isSkinnedMesh = false;
            window.isTree = false;
            window.flammableObjs.Clear();
            window.materialCheckList.Clear();
            window.flammableObjs.AddRange(Selection.gameObjects.ToList());
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 300, 300);
            window.Show();
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Object", true, 10)]
        static bool ConvertValidate(MenuCommand menuCommand)
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Skinned Mesh", false, 10)]
        static void ConvertBones(MenuCommand menuCommand)
        {
            ConvertToFlammable window = (ConvertToFlammable)EditorWindow.GetWindow(typeof(ConvertToFlammable));

            foreach(GameObject go in Selection.gameObjects)
            {
                if (go.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 0)
                {
                    Debug.LogError("No skinned mesh renderer in one of the objects you are trying to convert");
                    return;
                }
            }
           
            window.isSkinnedMesh = true;
            window.isVegetation = false;
            window.isTree = false;
            window.flammableObjs.Clear();
            window.materialCheckList.Clear();
            window.flammableObjs.AddRange(Selection.gameObjects.ToList());
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 300, 420);
            window.Show();
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Skinned Mesh", true, 10)]
        static bool ConvertBonesValidate(MenuCommand menuCommand)
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Vegetation", false, 10)]
        static void ConvertVegetation(MenuCommand menuCommand)
        {
            ConvertToFlammable window = (ConvertToFlammable)EditorWindow.GetWindow(typeof(ConvertToFlammable));
            window.isVegetation = true;
            window.isSkinnedMesh = false;
            window.isTree = false;
            window.flammableObjs.Clear();
            window.materialCheckList.Clear();
            window.flammableObjs.AddRange(Selection.gameObjects.ToList());
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 300, 300);
            window.Show();
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Vegetation", true, 10)]
        static bool ConvertVegetationValidate(MenuCommand menuCommand)
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Tree", false, 10)]
        static void ConvertTree(MenuCommand menuCommand)
        {
            ConvertToFlammable window = (ConvertToFlammable)EditorWindow.GetWindow(typeof(ConvertToFlammable));
            GameObject go = menuCommand.context as GameObject;

            window.isVegetation = false;
            window.isSkinnedMesh = false;
            window.isTree = true;
            window.flammableObjs.Clear();
            window.materialCheckList.Clear();
            window.flammableObjs.AddRange(Selection.gameObjects.ToList());
            window.position = new Rect(Screen.width / 2, Screen.height / 2, 300, 300);
            window.Show();
        }

        [MenuItem("GameObject/OAVA-Convert/Flammable Tree", true, 10)]
        static bool ConvertTreeValidate(MenuCommand menuCommand)
        {
            return Selection.activeGameObject != null;
        }

        void ConvertGOToFlammable()
        {
            foreach(GameObject flam in flammableObjs)
            {
                Debug.Log("Convert gameobject to flammable: " + flam.name.ToString());
            }
            
            HashSet<Shader> supportedURPLitShaders = new HashSet<Shader>()
        {
            Shader.Find("Universal Render Pipeline/Lit")
        };
            HashSet<Shader> supportedURPUnLitShaders = new HashSet<Shader>()
        {
            Shader.Find("Universal Render Pipeline/Unlit")
        };

            HashSet<Shader> supportedHDRPLitShaders = new HashSet<Shader>()
        {
            Shader.Find("HDRP/Lit")
        };

            HashSet<Shader> supportedHDRPUnLitShaders = new HashSet<Shader>()
        {
            Shader.Find("HDRP/Unlit")
        };
            bool ignisShader = false;
            foreach(GameObject go in flammableObjs)
            {
                foreach (Renderer rend in go.GetComponentsInChildren<Renderer>())
                {
                    for (int i = 0; i < rend.sharedMaterials.Length; i++)
                    {
                        if (materialCheckList.ContainsKey(rend.sharedMaterials[i]) && materialCheckList[rend.sharedMaterials[i]])
                        {
                            Material cur = rend.sharedMaterials[i];
                            if (supportedURPUnLitShaders.Contains(cur.shader))
                            {
                                Undo.RecordObject(rend.sharedMaterials[i], "Shader change");
                                Color col = cur.GetColor("_BaseColor");
                                Texture baseText = cur.GetTexture("_BaseMap");
                                Vector2 tiling = cur.GetTextureScale("_BaseMap");
                                Vector2 offset = cur.GetTextureOffset("_BaseMap");

                                rend.sharedMaterials[i].shader = Shader.Find("OAVA/Flammable");

                                rend.sharedMaterials[i].SetVector("_Tiling", tiling);
                                rend.sharedMaterials[i].SetVector("_Offset", offset);
                                rend.sharedMaterials[i].SetColor("_BaseColor", col);
                                rend.sharedMaterials[i].SetTexture("_BaseMap", baseText);
                                ignisShader = true;
                            }
                            else if (supportedURPLitShaders.Contains(cur.shader))
                            {
                                Undo.RecordObject(rend.sharedMaterials[i], "Shader change");
                                Color col = cur.GetColor("_BaseColor");
                                Texture baseText = cur.GetTexture("_BaseMap");
                                float smoothness = cur.GetFloat("_Smoothness");
                                float metallic = cur.GetFloat("_Metallic");
                                float normalStrength = cur.GetFloat("_BumpScale");
                                Texture normalText = cur.GetTexture("_BumpMap");
                                Vector2 tiling = cur.GetTextureScale("_BaseMap");
                                Vector2 offset = cur.GetTextureOffset("_BaseMap");

                                rend.sharedMaterials[i].shader = Shader.Find("OAVA/Flammable");

                                rend.sharedMaterials[i].SetColor("_BaseColor", col);
                                rend.sharedMaterials[i].SetTexture("_BaseMap", baseText);
                                rend.sharedMaterials[i].SetVector("_Tiling", tiling);
                                rend.sharedMaterials[i].SetVector("_Offset", offset);
                                rend.sharedMaterials[i].SetTexture("_NormalMap", normalText);
                                rend.sharedMaterials[i].SetFloat("_Smoothness", smoothness);
                                rend.sharedMaterials[i].SetFloat("_Metallic", metallic);

                                if (normalText == null)
                                {
                                    rend.sharedMaterials[i].SetFloat("_NormalStrength", 0);
                                }
                                else
                                {
                                    rend.sharedMaterials[i].SetFloat("_NormalStrength", normalStrength);
                                }
                                ignisShader = true;
                            }
                            else if (supportedHDRPUnLitShaders.Contains(cur.shader))
                            {
                                Undo.RecordObject(rend.sharedMaterials[i], "Shader change");
                                Color col = cur.GetColor("_BaseColor");
                                Texture baseText = cur.GetTexture("_BaseMap");
                                Vector2 tiling = cur.GetTextureScale("_BaseMap");
                                Vector2 offset = cur.GetTextureOffset("_BaseMap");

                                rend.sharedMaterials[i].shader = Shader.Find("OAVA/Flammable");

                                rend.sharedMaterials[i].SetVector("_Tiling", tiling);
                                rend.sharedMaterials[i].SetVector("_Offset", offset);
                                rend.sharedMaterials[i].SetColor("_BaseColor", col);
                                rend.sharedMaterials[i].SetTexture("_BaseMap", baseText);
                                ignisShader = true;
                            }
                            else if (supportedHDRPLitShaders.Contains(cur.shader))
                            {
                                Undo.RecordObject(rend.sharedMaterials[i], "Shader change");
                                Color col = cur.GetColor("_BaseColor");
                                Texture baseText = cur.GetTexture("_BaseColorMap");
                                float smoothness = cur.GetFloat("_Smoothness");
                                float metallic = cur.GetFloat("_Metallic");
                                float normalStrength = cur.GetFloat("_NormalScale");
                                Texture normalText = cur.GetTexture("_NormalMap");
                                Vector2 tiling = cur.GetTextureScale("_BaseColorMap");
                                Vector2 offset = cur.GetTextureOffset("_BaseColorMap");

                                Debug.Log(normalText);

                                rend.sharedMaterials[i].shader = Shader.Find("OAVA/Flammable");

                                rend.sharedMaterials[i].SetColor("_BaseColor", col);
                                rend.sharedMaterials[i].SetTexture("_BaseMap", baseText);
                                rend.sharedMaterials[i].SetVector("_Tiling", tiling);
                                rend.sharedMaterials[i].SetVector("_Offset", offset);
                                rend.sharedMaterials[i].SetTexture("_NormalMap", normalText);
                                rend.sharedMaterials[i].SetFloat("_Smoothness", smoothness);
                                rend.sharedMaterials[i].SetFloat("_Metallic", metallic);

                                if (normalText == null)
                                {
                                    rend.sharedMaterials[i].SetFloat("_NormalStrength", 0);
                                }
                                else
                                {
                                    rend.sharedMaterials[i].SetFloat("_NormalStrength", normalStrength);
                                }
                                ignisShader = true;
                            }

                        }
                    }
                }
            }
            

            Object flameEngine = Object.FindObjectOfType<FlameEngine>();
            if (!flameEngine)
            {
                if (!flameEnginePrefab)
                {
                    flameEnginePrefab = AssetDatabase.LoadAssetAtPath("Assets/OAVA-Flame/Prefabs/FlameEngine.prefab", typeof(GameObject)) as GameObject;
                }
                if (!flameEnginePrefab)
                {
                    Debug.LogError("Cannot find flameEngine prefab in path: Assets/OAVA-Flame/Prefabs/FlameEngine.prefab");
                }

                Undo.RegisterCreatedObjectUndo(PrefabUtility.InstantiatePrefab(flameEnginePrefab), "Created go");
            }
            foreach(GameObject flammable in flammableObjs)
            {
                if (!flammable.GetComponent<FlammableObject>())
                {
                    FlammableObject obj = Undo.AddComponent<FlammableObject>(flammable);
                    if (isVegetation)
                    {
                        obj.calculateFlammationAreaFromMesh = FlammableObject.CalculationArea.Vegetation;
                        obj.flameAreaNoiseMinMaxMultiplier = new Vector2(0.4f, 0.9f);
                        obj.flameLength = 3f;
                    }

                    if (isTree)
                    {
                        obj.useMeshFire = true;
                        obj.meshFireCount = 20;
                        obj.calculateFlammationAreaFromMesh = FlammableObject.CalculationArea.Vegetation;
                        obj.flameAreaNoiseMinMaxMultiplier = new Vector2(0.1f, 3f);
                        obj.flameEnvironmentalSpeed = 6f;
                        obj.flameLiveliness = 2;
                        obj.flameLength = 5f;
                        obj.embersBurstDelayMinMax = new Vector2(0.5f, 2);
                        obj.embersVFXMultiplier = 2;
                        obj.embersBurstVFXMultiplier = 5;
                        obj.embersParticleSize = 6.5f;
                        obj.smokeVFXMultiplier = 1;
                        obj.smokeColorIntensity = 12;
                        obj.smokeAlpha = 0.5f;
                        obj.smokeParticleSize = 8f;

                        obj.smokeColor = new Color(159f / 255f, 102f / 255f, 41f / 255f);
                        obj.flameParticleSize = 8f;
                        obj.flameVFXMultiplier = 0.2f;
                    }
                    if (!ignisShader)
                    {
                        obj.shaderEmissionMultiplier = 1;
                    }

                    if (isSkinnedMesh)
                    {
                        obj.flameAreaNoiseMinMaxMultiplier = new Vector2(0.9f, 1.5f);
                        obj.flameEnvironmentalSpeed = 0.5f;
                        obj.flameLength = 1.5f;
                        obj.flameVFXMultiplier = 0.5f;
                        obj.flameBurstDelayMinMax = new Vector2(0f, 0.1f);
                    }
                }
                if (isSkinnedMesh)
                {
                    if (automaticBones)
                    {
                        List<Transform> bones = new List<Transform>();

                        // List of all bones
                        foreach (SkinnedMeshRenderer skinRend in flammable.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            bones.AddRange(skinRend.rootBone.GetComponentsInChildren<Transform>().ToList());
                            bones = bones.Distinct().ToList();
                        }

                        if (bones.Count == 0)
                        {
                            Debug.LogError("No bones found in gameobject. Cannot convert to flammable skinned mesh... Converting to regular flammable object.");
                            return;
                        }

                        // Find Parent
                        Transform parentBone = bones[0];

                        foreach (Transform bone in bones)
                        {
                            if (!bones.Contains(bone.parent))
                            {
                                parentBone = bone;
                            }
                        }
                        List<Transform> suitableBones = new List<Transform>();
                        suitableBones.Add(parentBone);

                        FindSuitableBones(parentBone, minDistBetweenBones, suitableBones);

                        foreach (Transform bone in suitableBones)
                        {
                            BoxCollider box = Undo.AddComponent<BoxCollider>(bone.gameObject);
                            box.size = new Vector3(0.1f, 0.1f, 0.1f);
                            box.isTrigger = true;
                        }
                    }

                }
            }
            

            

        }

        void FindSuitableBones(Transform parent, float currentDist, List<Transform> output)
        {
            foreach(Transform child in parent.transform)
            {
                if(child.gameObject != parent.gameObject)
                {
                    float dist = Vector3.Distance(child.position, parent.position);

                    float passingDist = currentDist;
                    if (dist > currentDist)
                    {
                        bool tooClose = false;
                        foreach(Transform curFlamePos in output)
                        {
                            if(Vector3.Distance(child.position, curFlamePos.position) < minDistBetweenAny)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (!tooClose)
                        {
                            output.Add(child);
                            passingDist = minDistBetweenBones;
                        }
                        else
                        {
                            passingDist -= dist;
                        }
                    } 
                    else
                    {
                        passingDist -= dist;
                    }

                    FindSuitableBones(child, passingDist, output);
                }
            }

        }


        void OnGUI()
        {
            if (flammableObjs.Count <= 0)
                this.Close();

            if (flammableObjs.Count > 0)
            {
                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.LabelField("This functionality will attach the script to selected gameobjects and convert materials in gameobject and children; if there are any. Do you want to continue?", EditorStyles.wordWrappedLabel);
                    GUILayout.Space(20);

                    if (isTree)
                    {
                        EditorGUILayout.LabelField("Convert->Tree automatically enables use mesh fires on FlammableObject->System. If you want to manually place fires, disable use mesh fires.", EditorStyles.wordWrappedLabel);
                    }

                    scrollPos =
                        EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(300), GUILayout.Height(130));
                    bool foundMats = false;


                    foreach(GameObject go in flammableObjs)
                    {
                        Renderer[] rends = go.GetComponentsInChildren<Renderer>();

                        foreach (Renderer rend in rends)
                        {
                     
                            for (int i = 0; i < rend.sharedMaterials.Length; i++)
                            {
                                if (supportedShaders.Contains(rend.sharedMaterials[i].shader))
                                {
                                    foundMats = true;
                                    if (!materialCheckList.ContainsKey(rend.sharedMaterials[i])) materialCheckList.Add(rend.sharedMaterials[i], true);

                                    
                                    bool check = EditorGUILayout.ToggleLeft(rend.gameObject.name + ": " + rend.sharedMaterials[i].name, materialCheckList[rend.sharedMaterials[i]]);

                                    if (check)
                                    {
                                        materialCheckList[rend.sharedMaterials[i]] = true;
                                    }
                                    else
                                    {
                                        materialCheckList[rend.sharedMaterials[i]] = false;
                                    }
                                } 
                            }
                        }
                    }
                    
                    EditorGUILayout.EndScrollView();

                    if (foundMats)
                    {
                        EditorGUILayout.LabelField("Selected materials will be converted. ", EditorStyles.wordWrappedLabel);
                        GUILayout.Space(10);
                    }

                    if (isSkinnedMesh)
                    {
                        GUILayout.Space(10);
                        automaticBones = EditorGUILayout.Toggle("Auto-placement?", automaticBones);
                        if (automaticBones)
                        {
                            GUILayout.Space(2);
                            EditorGUILayout.LabelField("Min distance for new compared to last flammable parent bone:", EditorStyles.wordWrappedLabel);
                            minDistBetweenBones = EditorGUILayout.FloatField(minDistBetweenBones);
                            GUILayout.Space(2);
                            EditorGUILayout.LabelField("Min distance between any two colliders:", EditorStyles.wordWrappedLabel);
                            minDistBetweenAny = EditorGUILayout.FloatField(minDistBetweenAny);
                        }

                        GUILayout.Space(10);
                    }

                    if (GUILayout.Button("Convert!"))
                    {
                        ConvertGOToFlammable();
                        this.Close();
                    }
                }
                EditorGUILayout.EndVertical();
            }
            
        }
    }
}
