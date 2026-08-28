using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jingchen.Mic
{
    [DefaultExecutionOrder(-10000)]
    internal sealed class LapelMicBootstrap : MonoBehaviour
    {
        private const float ManagerScanInterval = 0.5f;

        private static LapelMicBootstrap instance;

        private readonly HashSet<RealtimeAvatarManager> observedManagers = new HashSet<RealtimeAvatarManager>();
        private readonly List<RealtimeAvatarManager> staleManagers = new List<RealtimeAvatarManager>();
        private float nextManagerScanTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
                return;

            GameObject bootstrapObject = new GameObject("Jingchen Lapel Mic Bootstrap");
            DontDestroyOnLoad(bootstrapObject);
            instance = bootstrapObject.AddComponent<LapelMicBootstrap>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindAvatarManagers();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextManagerScanTime)
                return;

            nextManagerScanTime = Time.unscaledTime + ManagerScanInterval;
            BindAvatarManagers();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            foreach (RealtimeAvatarManager manager in observedManagers)
            {
                if (manager != null)
                    manager.avatarCreated -= OnAvatarCreated;
            }

            observedManagers.Clear();

            if (instance == this)
                instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextManagerScanTime = 0f;
            BindAvatarManagers();
        }

        private void BindAvatarManagers()
        {
            RemoveDestroyedManagers();

            RealtimeAvatarManager[] managers = FindObjectsByType<RealtimeAvatarManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (RealtimeAvatarManager manager in managers)
            {
                if (manager == null || !observedManagers.Add(manager))
                    continue;

                manager.avatarCreated += OnAvatarCreated;

                if (manager.localAvatar != null)
                    InstallOnAvatar(manager.localAvatar, true);

                if (manager.avatars == null)
                    continue;

                foreach (RealtimeAvatar avatar in manager.avatars.Values)
                {
                    if (avatar != null)
                        InstallOnAvatar(avatar, avatar.isLocalAvatar);
                }
            }
        }

        private void RemoveDestroyedManagers()
        {
            staleManagers.Clear();

            foreach (RealtimeAvatarManager manager in observedManagers)
            {
                if (manager == null)
                    staleManagers.Add(manager);
            }

            foreach (RealtimeAvatarManager manager in staleManagers)
                observedManagers.Remove(manager);
        }

        private static void OnAvatarCreated(
            RealtimeAvatarManager manager,
            RealtimeAvatar avatar,
            bool isLocalAvatar)
        {
            InstallOnAvatar(avatar, isLocalAvatar);
        }

        private static void InstallOnAvatar(RealtimeAvatar avatar, bool isLocalAvatar)
        {
            if (avatar == null)
                return;

            RealtimeAvatarVoice voice = avatar.GetComponentInChildren<RealtimeAvatarVoice>(true);

            LapelMicVisual visual = avatar.GetComponent<LapelMicVisual>();
            if (visual == null)
                visual = avatar.gameObject.AddComponent<LapelMicVisual>();

            visual.Initialize(avatar, voice, isLocalAvatar);

            if (!isLocalAvatar)
                return;

            if (voice == null)
            {
                Debug.LogWarning("LapelMicBootstrap: The local avatar has no RealtimeAvatarVoice component.");
                return;
            }

            LapelMicController controller = avatar.GetComponent<LapelMicController>();
            if (controller == null)
                controller = avatar.gameObject.AddComponent<LapelMicController>();

            controller.Initialize(avatar, voice, visual);
        }
    }
}
