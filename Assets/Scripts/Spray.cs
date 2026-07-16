using Ignis;
using System.Collections;
using System.Collections.Generic;
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
    private bool isSpraying = false;

    // Tracking variables for extinguishing hazards
    private GameObject currentHazard;
    private Coroutine extinguishCoroutine;

    // A HashSet tracks which hazards are currently being monitored 
    // to prevent starting duplicate coroutines if hundreds of particles hit the same fire
    private HashSet<FlammableObject> monitoredHazards = new HashSet<FlammableObject>();

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
        isSpraying = true;
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
        isSpraying = false;
        if (waterParticles != null) waterParticles.Stop();
    }

    // Called automatically by Unity when a particle hits a collider 
    // (Requires "Send Collision Messages" to be checked in the Particle System)
    private void OnParticleCollision(GameObject other)
    {
        // Check if the particle hit an active hazard
        if (other.CompareTag("Active Hazard"))// && other.TryGetComponent<FlammableObject>(out FlammableObject ignisHazard))
        {
            FlammableObject ignisHazard = other.GetComponentInParent<FlammableObject>();
            Debug.Log("Detected a collision from the spray particles on a hazard's collider");
            // If we aren't already monitoring this specific fire, start watching it
            if (!monitoredHazards.Contains(ignisHazard))
            {
                monitoredHazards.Add(ignisHazard);
                StartCoroutine(MonitorHazardState(ignisHazard));
            }
        }
    }

    private IEnumerator MonitorHazardState(FlammableObject hazard)
    {
        Debug.Log($"Started spraying {hazard.name}. Hasn't extinguished yet.");
        // It will sit here quietly until the player successfully puts the fire out, or the fire went out on its own
        yield return new WaitUntil(() => hazard.IsExtinguished() || hazard.hasBurnedOut());
        HandleHazardExtinguished(hazard);
    }

    private void HandleHazardExtinguished(FlammableObject hazard)
    {
        // Find the controller and reset the object
        UniversalHazardController hazardController = FindAnyObjectByType<UniversalHazardController>();

        if (hazardController != null)
        {
            Debug.Log($"{hazard.name} extinguished successfully!");
            // Clear this BEFORE running the cleanup process to stop ResetExtinguishTracking from accidentally calling Ignite()
            currentHazard = null;
            hazardController.resetSingleObject(hazard.gameObject);
        }
        else
        {
            Debug.LogError("UniversalHazardController script was not found in the scene!");
        }

        ResetExtinguishTracking();
        // Remove the hazard from our tracking list so it can be interacted with again if re-ignited
        monitoredHazards.Remove(hazard);
    }

    private void ResetExtinguishTracking()
    {
        if (extinguishCoroutine != null)
        {
            StopCoroutine(extinguishCoroutine);
            extinguishCoroutine = null;
        }

        // If we stop spraying or miss, the fire flares back up!
        if (currentHazard != null)
        {
            if (currentHazard.TryGetComponent<HazardTemperature>(out HazardTemperature temp))
            {
                temp.Ignite();
            }
        }

        currentHazard = null;
    }
}