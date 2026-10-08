using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Bird's-eye camera for <see cref="MonitorMode"/>. Looks straight down at the VR
/// users and follows them between areas; the near clip plane is pushed down to just
/// above their heads so room ceilings are cut away. Remote voices are played as 2D
/// audio so every user is heard clearly regardless of where the camera is.
///
/// Controls: Tab / 0-9 choose who to follow (0 = everyone), WASD / arrows pan,
/// Q / E rotate, mouse wheel zoom, [ / ] lower / raise the ceiling cut, R reset view,
/// F1 toggle the overlay.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class MonitorCamera : MonoBehaviour
{
    private const int FollowEveryone = -1;

    [Header("View")]
    [SerializeField] private float defaultHeight = 14f;
    [SerializeField] private float minHeight = 3f;
    [SerializeField] private float maxHeight = 80f;
    [SerializeField] private float fieldOfView = 60f;
    [Tooltip("Geometry higher than this above the followed users' floor is clipped away (ceilings).")]
    [SerializeField] private float ceilingCut = 2.4f;
    [SerializeField] private float followSharpness = 6f;

    [Header("Controls")]
    [SerializeField] private float panSpeed = 1.2f;
    [SerializeField] private float rotateSpeed = 90f;
    [SerializeField] private float zoomStep = 0.12f;

    [Header("Audio")]
    [Tooltip("Play remote voices without 3D falloff so they are audible from the bird's-eye view.")]
    [SerializeField] private bool flattenVoiceAudio = true;
    [SerializeField] private float speakingThreshold = 0.05f;

    private readonly List<RealtimeAvatar> avatars = new();
    private readonly List<int> clientIds = new();

    private RealtimeAvatarManager avatarManager;
    private Realtime realtime;
    private Camera viewCamera;

    private int followIndex = FollowEveryone;
    private Vector3 focus;
    private float floorY;
    private Vector3 panOffset;
    private float yaw;
    private float height;
    private bool showOverlay = true;
    private float nextAudioRefreshTime;

    private GUIStyle panelStyle;
    private GUIStyle labelStyle;

    public void Initialize(RealtimeAvatarManager manager, Vector3 startFocus)
    {
        avatarManager = manager;
        realtime = manager.GetComponent<Realtime>();
        focus = startFocus;
        floorY = startFocus.y;
    }

    private void Awake()
    {
        gameObject.tag = "MainCamera";
        viewCamera = GetComponent<Camera>();
        viewCamera.fieldOfView = fieldOfView;
        gameObject.AddComponent<AudioListener>();
        height = defaultHeight;
    }

    private void LateUpdate()
    {
        RefreshAvatars();
        HandleInput();
        UpdateFocus();
        ApplyCamera();

        if (flattenVoiceAudio && Time.unscaledTime >= nextAudioRefreshTime)
        {
            nextAudioRefreshTime = Time.unscaledTime + 0.5f;
            FlattenVoiceAudio();
        }
    }

    private void RefreshAvatars()
    {
        avatars.Clear();
        clientIds.Clear();
        if (avatarManager == null || avatarManager.avatars == null)
            return;

        foreach (KeyValuePair<int, RealtimeAvatar> pair in avatarManager.avatars)
        {
            if (pair.Value != null)
                clientIds.Add(pair.Key);
        }
        clientIds.Sort();
        foreach (int clientId in clientIds)
            avatars.Add(avatarManager.avatars[clientId]);

        if (followIndex >= avatars.Count)
            followIndex = FollowEveryone;
    }

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
                SetFollow(followIndex + 1 >= avatars.Count ? FollowEveryone : followIndex + 1);
            if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
                SetFollow(FollowEveryone);
            for (int i = 0; i < 9; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                {
                    if (i < avatars.Count)
                        SetFollow(i);
                }
            }

            Vector2 move = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            float speed = panSpeed * height * (keyboard.shiftKey.isPressed ? 3f : 1f) * Time.unscaledDeltaTime;
            panOffset += Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y) * speed;

            if (keyboard.qKey.isPressed) yaw -= rotateSpeed * Time.unscaledDeltaTime;
            if (keyboard.eKey.isPressed) yaw += rotateSpeed * Time.unscaledDeltaTime;

            if (keyboard.leftBracketKey.wasPressedThisFrame) ceilingCut = Mathf.Max(0.5f, ceilingCut - 0.25f);
            if (keyboard.rightBracketKey.wasPressedThisFrame) ceilingCut += 0.25f;

            if (keyboard.rKey.wasPressedThisFrame)
            {
                panOffset = Vector3.zero;
                yaw = 0f;
                height = defaultHeight;
            }
            if (keyboard.f1Key.wasPressedThisFrame)
                showOverlay = !showOverlay;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                height = Mathf.Clamp(height * (1f - Mathf.Sign(scroll) * zoomStep), minHeight, maxHeight);
        }
    }

    private void SetFollow(int index)
    {
        followIndex = index;
        panOffset = Vector3.zero;
    }

    private void UpdateFocus()
    {
        if (avatars.Count == 0)
            return;

        Vector3 target;
        float targetFloor;
        if (followIndex == FollowEveryone)
        {
            target = Vector3.zero;
            targetFloor = float.MinValue;
            foreach (RealtimeAvatar avatar in avatars)
            {
                target += GetViewPoint(avatar);
                targetFloor = Mathf.Max(targetFloor, avatar.transform.position.y);
            }
            target /= avatars.Count;
        }
        else
        {
            RealtimeAvatar avatar = avatars[followIndex];
            target = GetViewPoint(avatar);
            targetFloor = avatar.transform.position.y;
        }

        float t = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        // Snap when users teleport between areas instead of sliding through walls.
        bool teleported = Mathf.Abs(targetFloor - floorY) > 3f || (target - focus).sqrMagnitude > 100f;
        focus = teleported ? target : Vector3.Lerp(focus, target, t);
        floorY = teleported ? targetFloor : Mathf.Lerp(floorY, targetFloor, t);
    }

    private static Vector3 GetViewPoint(RealtimeAvatar avatar)
    {
        // The root is the play-space origin; the head is where the user actually stands.
        return avatar.head != null ? avatar.head.position : avatar.transform.position;
    }

    private void ApplyCamera()
    {
        float viewHeight = Mathf.Max(height, RequiredHeightToFitEveryone());
        Vector3 ground = new(focus.x + panOffset.x, floorY, focus.z + panOffset.z);
        transform.SetPositionAndRotation(ground + Vector3.up * viewHeight, Quaternion.Euler(90f, yaw, 0f));

        // Looking straight down, the near plane is horizontal: everything above floor + ceilingCut is clipped.
        viewCamera.nearClipPlane = Mathf.Max(0.05f, viewHeight - ceilingCut);
        viewCamera.farClipPlane = viewHeight + 50f;
    }

    private float RequiredHeightToFitEveryone()
    {
        if (followIndex != FollowEveryone || avatars.Count < 2)
            return 0f;

        float spread = 0f;
        foreach (RealtimeAvatar avatar in avatars)
        {
            Vector3 offset = GetViewPoint(avatar) - focus;
            spread = Mathf.Max(spread, new Vector2(offset.x, offset.z).magnitude);
        }
        float halfFov = Mathf.Deg2Rad * viewCamera.fieldOfView * 0.5f;
        return Mathf.Min(maxHeight, (spread + 2f) / Mathf.Tan(halfFov));
    }

    private void FlattenVoiceAudio()
    {
        foreach (RealtimeAvatar avatar in avatars)
        {
            foreach (AudioSource source in avatar.GetComponentsInChildren<AudioSource>(true))
            {
                source.spatialBlend = 0f;
                source.spatialize = false;
            }
        }
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        panelStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };
        labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold, richText = true };

        bool connected = realtime != null && realtime.connected;
        System.Text.StringBuilder text = new();
        text.AppendLine("<b>MONITOR MODE</b>  (desktop observer)");
        // Every scenario joins the same room, so the experimenter must open the VR users' scene.
        text.AppendLine($"Scene: <b>{gameObject.scene.name}</b>");
        text.AppendLine(connected ? $"Connected  ·  VR users: {avatars.Count}" : "<color=#ffb347>Connecting to room…</color>");
        text.AppendLine(followIndex == FollowEveryone ? "Following: <b>everyone</b>" : $"Following: <b>{GetLabel(followIndex)}</b>");
        for (int i = 0; i < avatars.Count; i++)
        {
            string speaking = IsSpeaking(avatars[i]) ? "  <color=#7CFC00>● speaking</color>" : string.Empty;
            text.AppendLine($"  [{i + 1}] {GetLabel(i)}{speaking}");
        }
        text.AppendLine();
        text.Append("<size=12>Tab/0-9 follow · WASD pan · Q/E rotate · Wheel zoom · [ ] ceiling cut · R reset · F1 hide</size>");

        GUIContent content = new(text.ToString());
        float width = Mathf.Min(470f, Screen.width - 24f);
        GUI.Box(new Rect(12f, 12f, width, panelStyle.CalcHeight(content, width)), content, panelStyle);

        for (int i = 0; i < avatars.Count; i++)
        {
            Vector3 screen = viewCamera.WorldToScreenPoint(GetViewPoint(avatars[i]));
            if (screen.z <= 0f)
                continue;
            string color = IsSpeaking(avatars[i]) ? "#7CFC00" : "#FFFFFF";
            GUI.Label(new Rect(screen.x - 60f, Screen.height - screen.y - 34f, 120f, 22f), $"<color={color}>{GetLabel(i)}</color>", labelStyle);
        }
    }

    private string GetLabel(int index) => $"Player {index + 1} (id {clientIds[index]})";

    private bool IsSpeaking(RealtimeAvatar avatar)
    {
        RealtimeAvatarVoice voice = avatar.head != null ? avatar.head.GetComponent<RealtimeAvatarVoice>() : null;
        return voice != null && voice.voiceVolume > speakingThreshold;
    }
}
