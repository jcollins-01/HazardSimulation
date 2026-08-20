using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SnapBackToHip : MonoBehaviour
{
    public Transform socket;

    private XRGrabInteractable grab;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.selectExited.AddListener(OnReleased);
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        transform.position = socket.position;
        transform.rotation = socket.rotation;
    }
}