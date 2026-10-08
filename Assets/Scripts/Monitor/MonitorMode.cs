using System;
using Normal.Realtime;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

/// <summary>
/// Desktop observer mode for experimenters without a headset. When enabled, every
/// networked scene joins the Normcore room as an entity-less client: no local avatar
/// is spawned, the scene XR rig is switched off, the client never claims fire
/// authority (see <see cref="NetworkedFireState"/>), and a bird's-eye
/// <see cref="MonitorCamera"/> is created to watch and listen to the VR users.
///
/// Enable it per machine via the Editor menu "Tools/Monitor Mode", or in a build by
/// launching with the "-monitor" command-line argument. Nothing is stored in the
/// scene, so VR clients are unaffected.
/// </summary>
public static class MonitorMode
{
    public const string CommandLineFlag = "-monitor";
    public const string EditorPrefKey = "HazardSimulation.MonitorMode";

    private const float MonitorPlumeLodDistance = 60f;
    private const float MonitorPlumeCullDistance = 120f;

    public static bool IsActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        IsActive = ReadEnabledFlag();
        if (!IsActive)
            return;

        Debug.Log("[MonitorMode] Desktop observer mode is ON.");
        StopXR();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static bool ReadEnabledFlag()
    {
#if UNITY_EDITOR
        if (UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false))
            return true;
#endif
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void StopXR()
    {
        XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null || !manager.isInitializationComplete)
            return;

        manager.StopSubsystems();
        manager.DeinitializeLoader();
    }

    // sceneLoaded runs after Awake/OnEnable but before Start, and the avatar is only
    // spawned once Realtime finishes connecting, so clearing the prefab here is early enough.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RealtimeAvatarManager avatarManager = FindInScene<RealtimeAvatarManager>(scene);
        if (avatarManager == null)
            return;

        avatarManager.localAvatarPrefab = null;

        Vector3 startFocus = Vector3.zero;
        foreach (XROrigin rig in UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (rig.gameObject.scene != scene)
                continue;
            startFocus = rig.transform.position;
            rig.gameObject.SetActive(false);
        }

        // Only the monitor camera should hear the room.
        foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (listener.gameObject.scene == scene)
                listener.enabled = false;
        }

        ExtendWindowSmokePlumeRange(scene);

        GameObject cameraObject = new("Monitor Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.AddComponent<MonitorCamera>().Initialize(avatarManager, startFocus);
    }

    // Window plumes are tuned for a headset a few metres away and fade out beyond 25 m,
    // which hides them from the high monitor camera. The graph only picks up these
    // values in OnEnable, so toggle the component; Update has not run yet, so the
    // plume is still dormant and restarting it changes nothing else.
    private static void ExtendWindowSmokePlumeRange(Scene scene)
    {
        foreach (WindowSmokePlumeController plume in UnityEngine.Object.FindObjectsByType<WindowSmokePlumeController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (plume.gameObject.scene != scene)
                continue;
            plume.lodMaxDistance = Mathf.Max(plume.lodMaxDistance, MonitorPlumeLodDistance);
            plume.cullingDistance = Mathf.Max(plume.cullingDistance, MonitorPlumeCullDistance);
            plume.enabled = false;
            plume.enabled = true;
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T component in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (component.gameObject.scene == scene)
                return component;
        }
        return null;
    }
}
