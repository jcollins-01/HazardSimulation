using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Attach this to the VR Player prefab (alongside RealtimeAvatar).
/// When this avatar belongs to the local player, it hides the head and hand
/// renderers so the player doesn't see their own floating head in VR.
/// Remote players still see the full avatar normally.
/// </summary>
[RequireComponent(typeof(RealtimeAvatar))]
public class LocalAvatarHider : MonoBehaviour
{
    [Header("Avatar Parts to Hide for Local Player")]
    [Tooltip("Head mesh renderer — hidden for local player only")]
    [SerializeField] private Renderer[] _headRenderers;

    [Tooltip("Hand mesh renderers — hidden for local player only")]
    [SerializeField] private Renderer[] _handRenderers;

    [Tooltip("If true, also disables hand renderers for local player (recommended)")]
    [SerializeField] private bool _hideHandsLocally = true;

    private RealtimeAvatar _avatar;

    private void Awake()
    {
        _avatar = GetComponent<RealtimeAvatar>();
    }

    private void Start()
    {
        // RealtimeAvatar.isLocalAvatar is set during Start, so we read it here.
        // Use a slight delay to ensure Normcore has finished initialising the avatar.
        Invoke(nameof(ApplyVisibility), 0.1f);
    }

    private void ApplyVisibility()
    {
        bool isLocal = _avatar != null && _avatar.isLocalAvatar;

        // Hide head from local player (they should never see their own head in VR)
        foreach (Renderer r in _headRenderers)
        {
            if (r != null) r.enabled = !isLocal;
        }

        // Optionally hide hand meshes too (local player uses XR controller visuals instead)
        if (_hideHandsLocally)
        {
            foreach (Renderer r in _handRenderers)
            {
                if (r != null) r.enabled = !isLocal;
            }
        }
    }
}
