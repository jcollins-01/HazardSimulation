using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Attach this to the VR Player prefab (alongside RealtimeAvatar).
/// When this avatar belongs to the local player, it hides the specified renderers
/// so the player doesn't see their own body/head from the inside in VR.
/// Remote players still see the full avatar normally.
///
/// Two modes:
///   _autoHideBodyRenderers = true  → automatically hides ALL SkinnedMeshRenderers
///                                    in children (use this with the Firefighter model).
///   _autoHideBodyRenderers = false → falls back to the manually assigned
///                                    _headRenderers / _handRenderers arrays.
/// </summary>
[RequireComponent(typeof(RealtimeAvatar))]
public class LocalAvatarHider : MonoBehaviour
{
    [Header("Auto-hide (recommended for full-body avatars)")]
    [Tooltip("Automatically finds and hides ALL SkinnedMeshRenderers in children " +
             "for the local player. Keeps plain MeshRenderers (hand models) visible.")]
    [SerializeField] private bool _autoHideBodyRenderers = false;

    [Header("Manual override renderers")]
    [Tooltip("Head mesh renderer(s) — hidden for local player only")]
    [SerializeField] private Renderer[] _headRenderers = new Renderer[0];

    [Tooltip("Hand mesh renderer(s) — optionally hidden for local player")]
    [SerializeField] private Renderer[] _handRenderers = new Renderer[0];

    [Tooltip("If true, also hides hand renderers for local player")]
    [SerializeField] private bool _hideHandsLocally = true;

    private RealtimeAvatar _avatar;

    private void Awake()
    {
        _avatar = GetComponent<RealtimeAvatar>();
    }

    private void Start()
    {
        // isLocalAvatar is assigned during Start, so we wait one frame.
        Invoke(nameof(ApplyVisibility), 0.1f);
    }

    private void ApplyVisibility()
    {
        bool isLocal = _avatar != null && _avatar.isLocalAvatar;

        if (_autoHideBodyRenderers)
        {
            // Hide every SkinnedMeshRenderer under this avatar (the full Firefighter body).
            // Regular MeshRenderers (e.g. hand models) are left untouched.
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr != null) smr.enabled = !isLocal;
            }
        }
        else
        {
            // Legacy mode: use the manually assigned renderer arrays.
            foreach (Renderer r in _headRenderers)
                if (r != null) r.enabled = !isLocal;

            if (_hideHandsLocally)
            {
                foreach (Renderer r in _handRenderers)
                    if (r != null) r.enabled = !isLocal;
            }
        }
    }
}
