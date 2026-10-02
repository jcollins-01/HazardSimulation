using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Builds the locomotion study scenes from the hand tracking demo scene so the environment,
/// fire, TIC, Normcore avatar and hand rig stay identical and only locomotion differs.
/// Re-running it overwrites the generated scenes, so make scene-wide changes in the source
/// scene and rebuild rather than editing the generated copies.
///
/// Tools > Locomotion > Build Locomotion Scenes
/// </summary>
public static class LocomotionSceneBuilder
{
    private const string SourceScene = "Assets/SCENARIOS/hand tracking demo.unity";
    private const string OutputFolder = "Assets/SCENARIOS/Locomotion";
    private const string TeleportInteractorPrefab =
        "Assets/Samples/XR Interaction Toolkit/3.0.10/Starter Assets/Prefabs/Interactors/Teleport Interactor.prefab";

    private static readonly (string name, LocomotionModeConfigurator.Mode mode)[] Scenes =
    {
        ("Locomotion - Natural Walking", LocomotionModeConfigurator.Mode.Natural),
        ("Locomotion - Teleport", LocomotionModeConfigurator.Mode.Teleport),
    };

    [MenuItem("Tools/Locomotion/Build Locomotion Scenes")]
    public static void BuildAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
        {
            Debug.LogError($"[Locomotion] Source scene not found: {SourceScene}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(OutputFolder).Replace('\\', '/'), Path.GetFileName(OutputFolder));

        var built = new List<string>();
        foreach (var (name, mode) in Scenes)
        {
            string path = $"{OutputFolder}/{name}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(SourceScene, path))
            {
                Debug.LogError($"[Locomotion] Could not copy {SourceScene} to {path}");
                continue;
            }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!ConfigureScene(mode))
                continue;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            built.Add(path);
        }

        AddToBuildSettings(built);
        Debug.Log($"[Locomotion] Built {built.Count} scene(s):\n{string.Join("\n", built)}");
    }

    private static bool ConfigureScene(LocomotionModeConfigurator.Mode mode)
    {
        var origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin == null)
        {
            Debug.LogError("[Locomotion] No XR Origin found in scene.");
            return false;
        }

        if (mode == LocomotionModeConfigurator.Mode.Teleport)
        {
            AddHandTeleport(origin, "Left Hand", Handedness.Left);
            AddHandTeleport(origin, "Right Hand", Handedness.Right);
        }

        if (!origin.TryGetComponent(out LocomotionModeConfigurator configurator))
            configurator = origin.gameObject.AddComponent<LocomotionModeConfigurator>();

        var so = new SerializedObject(configurator);
        so.FindProperty("mode").intValue = (int)mode;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Apply in the editor too so the saved scene shows what will be active in play mode.
        // Enabled flags changed on prefab instance parts must be recorded or they revert on reload.
        configurator.Apply(mode);
        foreach (var component in origin.GetComponentsInChildren<Component>(true))
        {
            if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        LogSummary(origin, mode);
        return true;
    }

    private static void AddHandTeleport(XROrigin origin, string handName, Handedness handedness)
    {
        Transform hand = origin.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == handName);
        if (hand == null)
        {
            Debug.LogError($"[Locomotion] '{handName}' not found under the XR Origin; hand teleport not added.");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeleportInteractorPrefab);
        if (prefab == null)
        {
            Debug.LogError($"[Locomotion] Teleport interactor prefab not found: {TeleportInteractorPrefab}");
            return;
        }

        var root = new GameObject("Hand Teleport");
        root.transform.SetParent(hand, false);
        var gesture = root.AddComponent<HandTeleportGesture>();

        var interactorObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        interactorObject.name = "Teleport Interactor";
        var ray = interactorObject.GetComponent<XRRayInteractor>();

        var others = hand.GetComponentsInChildren<XRBaseInteractor>(true).Where(i => i != ray).ToArray();

        var so = new SerializedObject(gesture);
        so.FindProperty("handedness").intValue = (int)handedness;
        so.FindProperty("teleportInteractor").objectReferenceValue = ray;
        so.FindProperty("trackingSpace").objectReferenceValue = origin.CameraFloorOffsetObject.transform;
        var suppress = so.FindProperty("suppressWhileAiming");
        suppress.arraySize = others.Length;
        for (int i = 0; i < others.Length; i++)
            suppress.GetArrayElementAtIndex(i).objectReferenceValue = others[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void LogSummary(XROrigin origin, LocomotionModeConfigurator.Mode mode)
    {
        var lines = origin.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b is UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider
                        || b is XRRayInteractor
                        || b is HandTeleportGesture)
            .Select(b => $"  {(b.enabled ? "ON " : "off")}  {b.GetType().Name}  ({GetPath(b.transform, origin.transform)})");
        Debug.Log($"[Locomotion] {mode}:\n{string.Join("\n", lines)}");
    }

    private static string GetPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent)
            parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    private static void AddToBuildSettings(List<string> paths)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (var path in paths)
        {
            if (scenes.All(s => s.path != path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
