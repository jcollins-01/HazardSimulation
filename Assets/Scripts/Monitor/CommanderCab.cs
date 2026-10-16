using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The commander's seat in the fire truck. The rig (cab interior, head pivot and camera) is
/// authored in the Commander scene around a reference copy of the truck. When a scenario
/// loads, <see cref="MonitorMode"/> hands the scene to <see cref="EnterScene"/>, which moves
/// the rig to the same spot in that scenario's truck.
///
/// The commander faces the windshield; holding the right mouse button looks around (within
/// <see cref="maxYaw"/> / <see cref="maxPitch"/>) and the view eases back when released.
/// Every VR user's voice is played as 2D audio, so the whole conversation is audible from
/// the truck. F1 toggles the overlay.
/// </summary>
public sealed class CommanderCab : MonoBehaviour
{
    [Tooltip("Truck in the Commander scene that this cab is authored against.")]
    [SerializeField] private Transform referenceTruck;
    [Tooltip("Eye pivot; rotated for looking around.")]
    [SerializeField] private Transform head;
    [SerializeField] private Camera viewCamera;

    [Header("Look around (hold right mouse button)")]
    [SerializeField] private float maxYaw = 100f;
    [SerializeField] private float maxPitch = 30f;
    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] private float lookSensitivity = 0.15f;
    [Tooltip("How quickly the view returns to the windshield after releasing the button.")]
    [SerializeField] private float returnSharpness = 4f;

    [Header("Audio")]
    [SerializeField] private float speakingThreshold = 0.05f;

    private readonly List<RealtimeAvatar> avatars = new();
    private readonly List<int> clientIds = new();

    private Matrix4x4 cabInTruck;
    private Mesh truckMesh;
    private RealtimeAvatarManager avatarManager;
    private Realtime realtime;
    private string scenarioName;
    private bool seatedInTruck;
    private float yaw;
    private float pitch;
    private bool showOverlay = true;
    private float nextAudioRefreshTime;
    private GUIStyle panelStyle;

    private void Awake()
    {
        viewCamera.gameObject.tag = "MainCamera";
        if (referenceTruck != null)
        {
            cabInTruck = TruckFrame(referenceTruck).inverse * transform.localToWorldMatrix;
            truckMesh = referenceTruck.GetComponentInChildren<MeshFilter>().sharedMesh;
        }
    }

    /// <summary>Seats the commander in <paramref name="scene"/>'s fire truck.</summary>
    public void EnterScene(Scene scene, RealtimeAvatarManager manager, Pose fallback)
    {
        avatarManager = manager;
        realtime = manager.GetComponent<Realtime>();
        scenarioName = scene.name;

        Transform truck = FindTruck(scene);
        seatedInTruck = truck != null;
        if (seatedInTruck)
        {
            Matrix4x4 pose = TruckFrame(truck) * cabInTruck;
            transform.SetPositionAndRotation(pose.GetPosition(), pose.rotation);
            return;
        }

        // No truck in this scene: stand at the VR spawn point instead, facing the same way.
        Debug.LogWarning($"[Commander] No fire truck found in {scene.name}; placing the commander at the VR spawn point.");
        Quaternion facing = Quaternion.Euler(0f, fallback.rotation.eulerAngles.y, 0f);
        Vector3 eye = fallback.position + Vector3.up * 1.6f;
        transform.SetPositionAndRotation(eye - facing * transform.InverseTransformPoint(head.position), facing);
    }

    // The truck model lies on its side inside the prefab (its local Y axis runs from tail to
    // nose), so build an upright frame from its position and the direction its nose points.
    private static Matrix4x4 TruckFrame(Transform truck)
    {
        Vector3 nose = Vector3.ProjectOnPlane(truck.up, Vector3.up).normalized;
        return Matrix4x4.TRS(truck.position, Quaternion.LookRotation(nose, Vector3.up), Vector3.one);
    }

    private Transform FindTruck(Scene scene)
    {
        if (truckMesh == null)
            return null;
        foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (filter.gameObject.scene == scene && filter.sharedMesh == truckMesh)
                return filter.transform;
        }
        return null;
    }

    private void LateUpdate()
    {
        ObservedAvatars.Collect(avatarManager, avatars, clientIds);
        HandleLook();

        if (Time.unscaledTime >= nextAudioRefreshTime)
        {
            nextAudioRefreshTime = Time.unscaledTime + 0.5f;
            ObservedAvatars.FlattenVoices(avatars);
        }
    }

    private void HandleLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw = Mathf.Clamp(yaw + delta.x, -maxYaw, maxYaw);
            pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        }
        else
        {
            float t = 1f - Mathf.Exp(-returnSharpness * Time.unscaledDeltaTime);
            yaw = Mathf.Lerp(yaw, 0f, t);
            pitch = Mathf.Lerp(pitch, 0f, t);
        }
        head.localRotation = Quaternion.Euler(pitch, yaw, 0f);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            showOverlay = !showOverlay;
    }

    private void OnGUI()
    {
        // Before a scenario is chosen the launcher menu owns the screen.
        if (!showOverlay || avatarManager == null)
            return;

        panelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };

        bool connected = realtime != null && realtime.connected;
        System.Text.StringBuilder text = new();
        text.AppendLine("<b>COMMANDER VIEW</b>");
        text.AppendLine($"Scenario: <b>{scenarioName}</b>{(seatedInTruck ? string.Empty : "  <color=#ffb347>(no fire truck)</color>")}");
        text.AppendLine(connected ? $"Connected  ·  VR users: {avatars.Count}" : "<color=#ffb347>Connecting to room…</color>");
        for (int i = 0; i < avatars.Count; i++)
        {
            string speaking = ObservedAvatars.IsSpeaking(avatars[i], speakingThreshold) ? "  <color=#7CFC00>● speaking</color>" : string.Empty;
            text.AppendLine($"  Player {i + 1} (id {clientIds[i]}){speaking}");
        }
        text.AppendLine();
        text.Append("<size=12>Hold right mouse to look around · F1 hide</size>");

        GUIContent content = new(text.ToString());
        float width = Mathf.Min(360f, Screen.width - 24f);
        GUI.Box(new Rect(12f, 12f, width, panelStyle.CalcHeight(content, width)), content, panelStyle);
    }
}
