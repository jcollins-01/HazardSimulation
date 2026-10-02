using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

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

        MatchHandCastersToController(origin, "Left Hand", "Left Controller");
        MatchHandCastersToController(origin, "Right Hand", "Right Controller");

        if (mode == LocomotionModeConfigurator.Mode.Teleport)
        {
            AddHandTeleport(origin, "Left Hand", Handedness.Left);
            AddHandTeleport(origin, "Right Hand", Handedness.Right);
        }

        AddHandSpray(origin, "Left Hand", "Right Hand", Handedness.Left);
        AddHandSpray(origin, "Right Hand", "Left Hand", Handedness.Right);

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

    /// <summary>
    /// The hand rig's Near-Far casters only hit the Default/UI layers, while the controllers were set up to
    /// also hit Equipment (the fire extinguisher's layer), so hands could not grab it. Give each hand's
    /// casters every physics layer its controller's casters use.
    /// </summary>
    private static void MatchHandCastersToController(XROrigin origin, string handName, string controllerName)
    {
        var all = origin.GetComponentsInChildren<Transform>(true);
        Transform hand = all.FirstOrDefault(t => t.name == handName);
        Transform controller = all.FirstOrDefault(t => t.name == controllerName);
        if (hand == null || controller == null)
        {
            Debug.LogError($"[Locomotion] Could not find '{handName}' or '{controllerName}' to match grab layers.");
            return;
        }

        int sphereLayers = controller.GetComponentsInChildren<SphereInteractionCaster>(true).Aggregate(0, (m, c) => m | c.physicsLayerMask.value);
        int curveLayers = controller.GetComponentsInChildren<CurveInteractionCaster>(true).Aggregate(0, (m, c) => m | c.raycastMask.value);

        foreach (var caster in hand.GetComponentsInChildren<SphereInteractionCaster>(true))
            caster.physicsLayerMask = caster.physicsLayerMask.value | sphereLayers;
        foreach (var caster in hand.GetComponentsInChildren<CurveInteractionCaster>(true))
            caster.raycastMask = caster.raycastMask.value | curveLayers;
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

    private static void AddHandSpray(XROrigin origin, string holdingHandName, string freeHandName, Handedness holdingHandedness)
    {
        var all = origin.GetComponentsInChildren<Transform>(true);
        Transform holdingHand = all.FirstOrDefault(t => t.name == holdingHandName);
        Transform freeHand = all.FirstOrDefault(t => t.name == freeHandName);
        var holdingInteractor = holdingHand != null ? holdingHand.GetComponentInChildren<NearFarInteractor>(true) : null;
        if (holdingInteractor == null || freeHand == null)
        {
            Debug.LogError($"[Locomotion] Could not find '{holdingHandName}' Near-Far interactor or '{freeHandName}'; hand spray not added.");
            return;
        }

        // The free hand's teleport ray is left alone so the player can still teleport while holding the extinguisher.
        var freeInteractors = freeHand.GetComponentsInChildren<XRBaseInteractor>(true)
            .Where(i => i.GetComponentInParent<HandTeleportGesture>(true) == null)
            .ToArray();

        var root = new GameObject("Hand Spray");
        root.transform.SetParent(holdingHand, false);
        var spray = root.AddComponent<HandSprayGesture>();

        var so = new SerializedObject(spray);
        so.FindProperty("holdingHand").intValue = (int)holdingHandedness;
        so.FindProperty("holdingInteractor").objectReferenceValue = holdingInteractor;
        var free = so.FindProperty("freeHandInteractors");
        free.arraySize = freeInteractors.Length;
        for (int i = 0; i < freeInteractors.Length; i++)
            free.GetArrayElementAtIndex(i).objectReferenceValue = freeInteractors[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void LogSummary(XROrigin origin, LocomotionModeConfigurator.Mode mode)
    {
        var lines = origin.GetComponentsInChildren<Behaviour>(true)
            .Where(b => b is UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider
                        || b is XRRayInteractor
                        || b is HandTeleportGesture
                        || b is HandSprayGesture
                        || b is SphereInteractionCaster
                        || b is CurveInteractionCaster)
            .Select(b => $"  {(b.enabled ? "ON " : "off")}  {b.GetType().Name}{LayerInfo(b)}  ({GetPath(b.transform, origin.transform)})");
        Debug.Log($"[Locomotion] {mode}:\n{string.Join("\n", lines)}");
    }

    private static string LayerInfo(Behaviour b) => b switch
    {
        SphereInteractionCaster s => $" mask={s.physicsLayerMask.value}",
        CurveInteractionCaster c => $" mask={c.raycastMask.value}",
        _ => "",
    };

    private static string GetPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t != null && t != root; t = t.parent)
            parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    private static void AddToBuildSettings(List<string> paths)
    {
        // Rebuilt scenes get new GUIDs, so refresh existing entries in place (keeping their order and enabled state).
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (var path in paths)
        {
            int index = scenes.FindIndex(s => s.path == path);
            if (index >= 0)
                scenes[index] = new EditorBuildSettingsScene(path, scenes[index].enabled);
            else
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
