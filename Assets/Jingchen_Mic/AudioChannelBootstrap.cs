using UnityEngine;
using UnityEngine.SceneManagement;
using Normal.Realtime;

namespace Jingchen.Mic
{
    // Install before avatar voice starts sending samples, without editing any scene.
    [DefaultExecutionOrder(-10000)]
    internal sealed class AudioChannelBootstrap : MonoBehaviour
    {
        private static AudioChannelBootstrap instance;
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null) return;
            var root = new GameObject("Audio Channels");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<AudioChannelBootstrap>();
        }

        private void Awake() { SceneManager.sceneLoaded += SceneLoaded; }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { Scan(); }

        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;
            Scan();
        }

        private static void Scan()
        {
            foreach (var manager in FindObjectsByType<RealtimeAvatarManager>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var realtime = manager.GetComponent<Realtime>();
                if (realtime == null) continue;
                var controller = realtime.GetComponent<AudioChannelController>();
                if (controller == null)
                    controller = realtime.gameObject.AddComponent<AudioChannelController>();
                controller.Register(manager);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (instance == this) instance = null;
        }
    }
}
