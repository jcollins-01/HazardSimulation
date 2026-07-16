using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Normal.Realtime;
using System;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class NetworkedGrabInteractable : MonoBehaviour
{
    [SerializeField] private XRGrabInteractable _grabInteractable;
    [SerializeField] private RealtimeView _realtimeView;
    [SerializeField] private RealtimeTransform _realtimeTransform;

    void OnEnable()
    {

        _grabInteractable.selectEntered.AddListener(RequestOwnership);
        _grabInteractable.selectExited.AddListener(ClearOwnership);
    }

    private void ClearOwnership(SelectExitEventArgs arg0)
    {
        throw new NotImplementedException();
    }

    private void OnDisable()
    {
        _grabInteractable.selectEntered.RemoveListener(RequestOwnership);
        _grabInteractable.selectExited.RemoveListener(ClearOwnership);
    }

    private void RequestOwnership(SelectEnterEventArgs args)
    {
        _realtimeView.RequestOwnership();
        _realtimeTransform.RequestOwnership();
    }

    private void ClearOwnership(SelectExitEvent args)
    {
        if (_realtimeView.isOwnedLocally)
        {
            _realtimeView.ClearOwnership();
        }

        if(_realtimeTransform.isOwnedLocally)
        {
            _realtimeTransform.ClearOwnership();
        }
    }
}
