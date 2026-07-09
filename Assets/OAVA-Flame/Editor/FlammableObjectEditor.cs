using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Ignis
{
    [CustomEditor(typeof(FlammableObject))]
    [CanEditMultipleObjects]
    public class FlammableObjectEditor : Editor
    {
        public Texture logo;
        private SerializedProperty maxSpread;
        private SerializedProperty fireCrawlSpeed;
        private SerializedProperty fireColor;
        private SerializedProperty colorIntensityMultiplier;
        private SerializedProperty fireColorBlend;
        private SerializedProperty flameLength;
        private SerializedProperty shaderEmissionColor;
        private SerializedProperty shaderEmissionMultiplier;
        private SerializedProperty flameCatchAreaAddition;
        private SerializedProperty setThisOnFireOnStart;
        private SerializedProperty fireSpread;
        private SerializedProperty burnOutStart_s;
        private SerializedProperty burnOutLength_s;
        private SerializedProperty deleteAfterBurnout;
        private SerializedProperty useMeshFire;
        private SerializedProperty calculateFlammationAreaFromMesh;
        private SerializedProperty affectedByWind;
        private SerializedProperty ignitionTime;
        private SerializedProperty flameEnvironmentalSpeed;
        private SerializedProperty flameLiveliness;
        private SerializedProperty flameAreaNoiseMinMaxMultiplier;
        private SerializedProperty flammableColliders;
        private SerializedProperty flammableMaterialIndexes;
        private SerializedProperty customFireSFX;
        private SerializedProperty useFireSFX;
        private SerializedProperty customFireOriginOnStart;
        private SerializedProperty flameBurstDelayMinMax;
        private SerializedProperty achieveMaxBrightness_s;
        private SerializedProperty flameVFXMultiplier;
        private SerializedProperty shaderColorNoise;
        private SerializedProperty shaderColorNoiseSpeed;
        private SerializedProperty shaderBurntColor;
        private SerializedProperty backSpreadCoolDown_s;
        private SerializedProperty flameLights;
        private SerializedProperty smokeColor;
        private SerializedProperty smokeAlpha;
        private SerializedProperty smokeVFXMultiplier;
        private SerializedProperty smokeColorIntensity;
        private SerializedProperty meshFireCount;
        private SerializedProperty isReignitable;
        private SerializedProperty onFireTimer;
        private SerializedProperty flameLivelinessSpeed;
        private SerializedProperty windForceMultiplier;
        private SerializedProperty fullExtinguishToughness;
        private SerializedProperty shaderToBurntInterpolateSpeed;
        private SerializedProperty enableMaterialAnimation;
        private SerializedProperty overrideFireVFX;
        private SerializedProperty overrideVFXVariant;
        private SerializedProperty flameParticleSize;
        private SerializedProperty embersVFXMultiplier;
        private SerializedProperty embersBurstDelayMinMax;
        private SerializedProperty embersBurstVFXMultiplier;
        private SerializedProperty meshFireMeshFilters;
        private SerializedProperty embersParticleSize;
        private SerializedProperty smokeParticleSize;
        private SerializedProperty useExtinguishSteam;
        private SerializedProperty spreadLayerMask;

        private SerializedProperty useMaterialAnimationOnAllRenderers;

        private SerializedProperty animateMaterialsRenderers;
        private SerializedProperty enableFlameVFX;
        private SerializedProperty isEffectOnForever;

        private int realOpenTab = 0;

        private FlammableObject flam;

        private void OnEnable()
        {
            maxSpread = serializedObject.FindProperty("maxSpread");
            fireCrawlSpeed = serializedObject.FindProperty("fireCrawlSpeed");
            fireColor = serializedObject.FindProperty("fireColor");
            colorIntensityMultiplier = serializedObject.FindProperty("colorIntensityMultiplier");
            fireColorBlend = serializedObject.FindProperty("fireColorBlend");
            flameLength = serializedObject.FindProperty("flameLength");
            shaderEmissionColor = serializedObject.FindProperty("shaderEmissionColor");
            shaderEmissionMultiplier = serializedObject.FindProperty("shaderEmissionMultiplier");
            flameCatchAreaAddition = serializedObject.FindProperty("flameCatchAreaAddition");
            setThisOnFireOnStart = serializedObject.FindProperty("setThisOnFireOnStart");
            fireSpread = serializedObject.FindProperty("fireSpread");
            burnOutStart_s = serializedObject.FindProperty("burnOutStart_s");
            burnOutLength_s = serializedObject.FindProperty("burnOutLength_s");
            deleteAfterBurnout = serializedObject.FindProperty("deleteAfterBurnout");
            useMeshFire = serializedObject.FindProperty("useMeshFire");
            calculateFlammationAreaFromMesh = serializedObject.FindProperty("calculateFlammationAreaFromMesh");
            affectedByWind = serializedObject.FindProperty("affectedByWind");
            ignitionTime = serializedObject.FindProperty("ignitionTime");
            flameEnvironmentalSpeed = serializedObject.FindProperty("flameEnvironmentalSpeed");
            flameLiveliness = serializedObject.FindProperty("flameLiveliness");
            flameAreaNoiseMinMaxMultiplier = serializedObject.FindProperty("flameAreaNoiseMinMaxMultiplier");
            flammableColliders = serializedObject.FindProperty("flammableColliders");
            flammableMaterialIndexes = serializedObject.FindProperty("flammableMaterialIndexes");
            customFireSFX = serializedObject.FindProperty("customFireSFX");
            useFireSFX = serializedObject.FindProperty("useFireSFX");
            customFireOriginOnStart = serializedObject.FindProperty("customFireOriginOnStart");
            flameBurstDelayMinMax = serializedObject.FindProperty("flameBurstDelayMinMax");
            achieveMaxBrightness_s = serializedObject.FindProperty("achieveMaxBrightness_s");
            flameVFXMultiplier = serializedObject.FindProperty("flameVFXMultiplier");
            shaderColorNoise = serializedObject.FindProperty("shaderColorNoise");
            shaderColorNoiseSpeed = serializedObject.FindProperty("shaderColorNoiseSpeed");
            shaderBurntColor = serializedObject.FindProperty("shaderBurntColor");
            flameLights = serializedObject.FindProperty("flameLights");
            smokeColor = serializedObject.FindProperty("smokeColor");
            smokeAlpha = serializedObject.FindProperty("smokeAlpha");
            smokeVFXMultiplier = serializedObject.FindProperty("smokeVFXMultiplier");
            smokeColorIntensity = serializedObject.FindProperty("smokeColorIntensity");
            meshFireCount = serializedObject.FindProperty("meshFireCount");
            isReignitable = serializedObject.FindProperty("isReignitable");
            onFireTimer = serializedObject.FindProperty("onFireTimer");
            flameLivelinessSpeed = serializedObject.FindProperty("flameLivelinessSpeed");
            windForceMultiplier = serializedObject.FindProperty("windForceMultiplier");
            fullExtinguishToughness = serializedObject.FindProperty("fullExtinguishToughness");
            backSpreadCoolDown_s = serializedObject.FindProperty("backSpreadCoolDown_s");
            shaderToBurntInterpolateSpeed = serializedObject.FindProperty("shaderToBurntInterpolateSpeed");
            enableMaterialAnimation = serializedObject.FindProperty("enableMaterialAnimation");
            overrideFireVFX = serializedObject.FindProperty("overrideFireVFX");
            overrideVFXVariant = serializedObject.FindProperty("overrideVFXVariant");
            flameParticleSize = serializedObject.FindProperty("flameParticleSize");
            embersVFXMultiplier = serializedObject.FindProperty("embersVFXMultiplier");
            embersBurstDelayMinMax = serializedObject.FindProperty("embersBurstDelayMinMax");
            embersBurstVFXMultiplier = serializedObject.FindProperty("embersBurstVFXMultiplier");
            meshFireMeshFilters = serializedObject.FindProperty("meshFireMeshFilters");
            embersParticleSize = serializedObject.FindProperty("embersParticleSize");
            smokeParticleSize = serializedObject.FindProperty("smokeParticleSize");
            useExtinguishSteam = serializedObject.FindProperty("useExtinguishSteam");
            spreadLayerMask = serializedObject.FindProperty("spreadLayerMask");
            useMaterialAnimationOnAllRenderers = serializedObject.FindProperty("useMaterialAnimationOnAllRenderers");
            animateMaterialsRenderers = serializedObject.FindProperty("animateMaterialsRenderers");

            isEffectOnForever = serializedObject.FindProperty("isEffectOnForever");
            enableFlameVFX = serializedObject.FindProperty("enableFlameVFX");

            flam = (FlammableObject)target;
        }

        public override void OnInspectorGUI()
        {
            GUILayout.Box(logo);
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            flam.openTabUpper = GUILayout.Toolbar(flam.openTabUpper, new string[] { "System", "Flame VFX", "Other VFX" });
            switch (flam.openTabUpper)
            {
                case 0:
                    realOpenTab = 0;
                    flam.openTabLower = 4;
                    break;
                case 1:
                    realOpenTab = 1;
                    flam.openTabLower = 4;
                    break;
                case 2:
                    realOpenTab = 2;
                    flam.openTabLower = 4;
                    break;


            }

            flam.openTabLower = GUILayout.Toolbar(flam.openTabLower, new string[] { "Material", "Advanced", "SFX" });
            switch (flam.openTabLower)
            {
                case 0:
                    realOpenTab = 3;
                    flam.openTabUpper = 4;
                    break;
                case 1:
                    realOpenTab = 4;
                    flam.openTabUpper = 4;
                    break;
                case 2:
                    realOpenTab = 5;
                    flam.openTabUpper = 4;
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
            }

            EditorGUI.BeginChangeCheck();

            switch (realOpenTab)
            {
                case 0:
                    EditorGUILayout.LabelField("Fire Behaviour", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(setThisOnFireOnStart);
                    if (flam.setThisOnFireOnStart)
                    {
                        EditorGUILayout.PropertyField(customFireOriginOnStart);
                    }

                    EditorGUILayout.PropertyField(fireCrawlSpeed);
                    EditorGUILayout.PropertyField(flameCatchAreaAddition);
                    EditorGUILayout.Space();

                    EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(ignitionTime);
                    EditorGUILayout.PropertyField(isEffectOnForever);

                    if (!flam.isEffectOnForever)
                    {
                        EditorGUILayout.PropertyField(burnOutStart_s);
                        EditorGUILayout.PropertyField(burnOutLength_s);
                    }

                    EditorGUILayout.Space();

                    EditorGUILayout.LabelField("Additional", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(isReignitable);
                    EditorGUILayout.PropertyField(affectedByWind);

                    if (flam.affectedByWind)
                    {
                        EditorGUILayout.PropertyField(windForceMultiplier);
                    }

                    EditorGUILayout.PropertyField(useMeshFire);
                    if (flam.useMeshFire)
                    {
                        EditorGUILayout.PropertyField(meshFireCount);
                        EditorGUILayout.PropertyField(meshFireMeshFilters);
                    }


                    break;
                case 1:
                    EditorGUILayout.PropertyField(overrideFireVFX);
                    if (flam.overrideFireVFX)
                    {
                        EditorGUILayout.PropertyField(overrideVFXVariant);
                    }
                    if ((!flam.overrideFireVFX && FlameEngine.instance.fireVFXVariant != FlameEngine.FireVFXVariant.OnlySmoke) || (flam.overrideFireVFX && flam.overrideVFXVariant != FlameEngine.FireVFXVariant.OnlySmoke))
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.PropertyField(enableFlameVFX);
                        EditorGUILayout.Space();
                        if (flam.enableFlameVFX)
                        {
                            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
                            EditorGUILayout.PropertyField(fireColor);
                            EditorGUILayout.PropertyField(colorIntensityMultiplier);
                            EditorGUILayout.PropertyField(fireColorBlend);
                            EditorGUILayout.Space();

                            EditorGUILayout.LabelField("Other", EditorStyles.boldLabel);
                            EditorGUILayout.PropertyField(flameLength);
                            EditorGUILayout.PropertyField(flameVFXMultiplier);

                            if ((!flam.overrideFireVFX && FlameEngine.instance.fireVFXVariant != FlameEngine.FireVFXVariant.OldSchool) || (flam.overrideFireVFX && flam.overrideVFXVariant != FlameEngine.FireVFXVariant.OldSchool))
                            {
                                EditorGUILayout.PropertyField(flameEnvironmentalSpeed);
                                EditorGUILayout.PropertyField(flameLiveliness);
                                EditorGUILayout.PropertyField(flameLivelinessSpeed);
                                EditorGUILayout.PropertyField(flameParticleSize);
                            }

                            EditorGUILayout.Space();
                            EditorGUILayout.PropertyField(flameAreaNoiseMinMaxMultiplier);
                            if ((!flam.overrideFireVFX && FlameEngine.instance.fireVFXVariant != FlameEngine.FireVFXVariant.OldSchool) || (flam.overrideFireVFX && flam.overrideVFXVariant != FlameEngine.FireVFXVariant.OldSchool))
                            {
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(flameBurstDelayMinMax);
                            }
                            EditorGUILayout.Space();
                        }

                    }
                    else
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.PropertyField(flameLength);

                    }

                    DebugFlammableVFXBox debug = flam.GetComponent<DebugFlammableVFXBox>();
                    if (!debug)
                    {
                        if (GUILayout.Button("Preview Flames VFX"))
                        {
                            flam.gameObject.AddComponent<DebugFlammableVFXBox>();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop Previewing Flames VFX"))
                        {
                            DestroyImmediate(debug);
                        }
                    }
                    break;

                case 2:
                    EditorGUILayout.LabelField("Smoke", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(smokeColor);
                    EditorGUILayout.PropertyField(smokeColorIntensity);
                    EditorGUILayout.PropertyField(smokeAlpha);
                    EditorGUILayout.PropertyField(smokeVFXMultiplier);
                    EditorGUILayout.PropertyField(smokeParticleSize);
                    EditorGUILayout.Space();
                    if ((!flam.overrideFireVFX && FlameEngine.instance.fireVFXVariant != FlameEngine.FireVFXVariant.OnlySmoke) || (flam.overrideFireVFX && flam.overrideVFXVariant != FlameEngine.FireVFXVariant.OnlySmoke))
                    {
                        EditorGUILayout.LabelField("Embers", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(embersVFXMultiplier);
                        EditorGUILayout.PropertyField(embersBurstVFXMultiplier);
                        EditorGUILayout.PropertyField(embersBurstDelayMinMax);
                        EditorGUILayout.PropertyField(embersParticleSize);
                        EditorGUILayout.Space();
                    }

                    EditorGUILayout.LabelField("Steam", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(useExtinguishSteam);
                    EditorGUILayout.Space();

                    DebugFlammableVFXBox debug2 = flam.GetComponent<DebugFlammableVFXBox>();
                    if (!debug2)
                    {
                        if (GUILayout.Button("Preview Flames VFX"))
                        {
                            flam.gameObject.AddComponent<DebugFlammableVFXBox>();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Stop Previewing Flames VFX"))
                        {
                            DestroyImmediate(debug2);
                        }
                    }
                    break;
                case 3:
                    EditorGUILayout.PropertyField(enableMaterialAnimation);
                    if (flam.enableMaterialAnimation)
                    {
                        EditorGUILayout.PropertyField(shaderEmissionColor);
                        EditorGUILayout.PropertyField(shaderEmissionMultiplier);
                        EditorGUILayout.PropertyField(shaderBurntColor);
                        EditorGUILayout.Space();
                        EditorGUILayout.PropertyField(shaderToBurntInterpolateSpeed);
                        EditorGUILayout.PropertyField(achieveMaxBrightness_s);
                        EditorGUILayout.PropertyField(shaderColorNoise);
                        EditorGUILayout.PropertyField(shaderColorNoiseSpeed);
                        EditorGUILayout.Space();
                        EditorGUILayout.PropertyField(useMaterialAnimationOnAllRenderers);

                        if (!flam.useMaterialAnimationOnAllRenderers)
                        {
                            EditorGUILayout.PropertyField(animateMaterialsRenderers);
                        }
                        EditorGUILayout.Space();

                        DebugFlammableShader debugShader = flam.GetComponent<DebugFlammableShader>();
                        if (!debugShader)
                        {
                            if (GUILayout.Button("Preview Shader Animation"))
                            {
                                flam.gameObject.AddComponent<DebugFlammableShader>();
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Stop previewing Shader Animation"))
                            {
                                DestroyImmediate(debugShader);
                            }
                        }
                    }
                    else
                    {
                        DebugFlammableShader shader = flam.gameObject.GetComponent<DebugFlammableShader>();
                        if (shader)
                            DestroyImmediate(shader);
                    }

                    break;
                case 4:
                    EditorGUILayout.LabelField("Burn settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(maxSpread);
                    EditorGUILayout.PropertyField(deleteAfterBurnout);
                    EditorGUILayout.PropertyField(calculateFlammationAreaFromMesh);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Current progress", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(fireSpread);
                    EditorGUILayout.PropertyField(onFireTimer);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Extinguish parameters", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(fullExtinguishToughness);
                    EditorGUILayout.PropertyField(backSpreadCoolDown_s);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Advanced setup", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(spreadLayerMask);
                    EditorGUILayout.PropertyField(flameLights);
                    EditorGUILayout.PropertyField(flammableColliders);
                    EditorGUILayout.PropertyField(flammableMaterialIndexes);

                    FlameEventInvoker eventInvoker = flam.GetComponent<FlameEventInvoker>();

                    if (!eventInvoker)
                    {
                        if (GUILayout.Button("Enable Flame Events"))
                        {
                            flam.gameObject.AddComponent<FlameEventInvoker>();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Disable Flame Events"))
                        {
                            DestroyImmediate(eventInvoker);
                        }
                    }

                    break;
                case 5:
                    EditorGUILayout.PropertyField(useFireSFX);
                    if (flam.useFireSFX)
                        EditorGUILayout.PropertyField(customFireSFX);

                    break;

            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
