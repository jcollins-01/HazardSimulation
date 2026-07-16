using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class Spray : MonoBehaviour
{
    [Header("Water Settings")]
    [SerializeField] private ParticleSystem waterParticles;
    [SerializeField] private Transform nozzleTransform; // Position & direction where water comes out
    [SerializeField] private float maxSprayDistance = 10f;

    [Header("Ignis Extinguish Settings")]
    [SerializeField] private float startRadius = 0.5f;
    [SerializeField] private float radiusIncrementSpeed = 0.5f;

    private XRGrabInteractable grabInteractable;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        // Subscribe to XRI trigger events
        grabInteractable.activated.AddListener(OnTriggerPulled);
        grabInteractable.deactivated.AddListener(OnTriggerReleased);
        grabInteractable.selectExited.AddListener(OnHoseDropped);

        // Ensure particles are stopped at start
        if (waterParticles != null && waterParticles.isPlaying)
        {
            waterParticles.Stop();
        }
    }

    void OnDestroy()
    {
        // Unsubscribe to avoid memory leaks
        if (grabInteractable != null)
        {
            grabInteractable.activated.RemoveListener(OnTriggerPulled);
            grabInteractable.deactivated.RemoveListener(OnTriggerReleased);
            grabInteractable.selectExited.RemoveListener(OnHoseDropped);
        }
    }

    private void OnTriggerPulled(ActivateEventArgs args)
    {
        //Debug.Log("Trigger pulled! XRI Event fired successfully.");
        if (waterParticles != null) waterParticles.Play();
    }

    private void OnTriggerReleased(DeactivateEventArgs args)
    {
        //Debug.Log("Trigger released.");
        StopSpraying();
    }

    private void OnHoseDropped(SelectExitEventArgs args)
    {
        StopSpraying();
    }

    private void StopSpraying()
    {
        if (waterParticles != null) waterParticles.Stop();
    }
}