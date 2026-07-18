using Normal.Realtime;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Bridges XR Interaction Toolkit grabbing and Normcore ownership for a shared,
/// grabbable tool (hose, fire extinguisher, test props).
///
/// Ownership policy:
///  - selectEntered  -> request ownership of the RealtimeView and every realtime
///    component on this object (RealtimeTransform, SpraySync, ...). Normcore grants
///    it to this client; the grabber becomes the single writer of pose + spray state.
///  - selectExited   -> ownership is KEPT, not cleared. Rationale:
///      1) While unowned, any client may write the transform model. Because the
///         Rigidbody is simulated on every client with slightly different results,
///         clearing ownership on drop would let remote clients' physics fight over
///         the resting pose. A stable owner keeps one source of truth for where the
///         dropped tool lies.
///      2) The spray state must be writable the moment the tool is released so it
///         reliably turns off for everyone (clearing ownership first could reject
///         that final write).
///    Ownership transfers to the next player who grabs the tool (takeover is allowed
///    on these views), and Normcore clears ownership automatically if the owner
///    disconnects. SpraySync handles the disconnect-while-spraying cleanup.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class NetworkedGrabInteractable : MonoBehaviour
{
    [SerializeField] private XRGrabInteractable _grabInteractable;
    [SerializeField] private RealtimeView _realtimeView;
    [SerializeField] private RealtimeTransform _realtimeTransform;

    private bool _subscribed;

    private void Awake()
    {
        // Auto-fill missing references so the component cannot silently no-op.
        if (_grabInteractable == null) _grabInteractable = GetComponent<XRGrabInteractable>();
        if (_realtimeView == null) _realtimeView = GetComponent<RealtimeView>();
        if (_realtimeTransform == null) _realtimeTransform = GetComponent<RealtimeTransform>();
    }

    private void OnEnable()
    {
        if (_subscribed || _grabInteractable == null)
            return;

        _grabInteractable.selectEntered.AddListener(OnSelectEntered);
        _grabInteractable.selectExited.AddListener(OnSelectExited);
        _subscribed = true;
    }

    private void OnDisable()
    {
        if (!_subscribed || _grabInteractable == null)
            return;

        _grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        _grabInteractable.selectExited.RemoveListener(OnSelectExited);
        _subscribed = false;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        // Request ownership of the view model and every realtime component model on
        // this object (RealtimeTransform, SpraySync, ...). GetComponents is safe even
        // when some of the serialized references above are null.
        if (_realtimeView != null && !_realtimeView.isOwnedLocallySelf)
            _realtimeView.RequestOwnership();

        foreach (IRealtimeComponent component in GetComponents<IRealtimeComponent>())
        {
            if (component != null && !component.isOwnedLocallySelf)
                component.RequestOwnership();
        }
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        // Ownership is intentionally retained (see class summary). We only make sure
        // the spray state is off so every client sees the particles stop on drop.
        if (TryGetComponent<SpraySync>(out SpraySync spraySync))
            spraySync.SetSprayInputHeld(false);
    }
}
