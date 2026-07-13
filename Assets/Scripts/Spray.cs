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

    void Update()
    {
        /*if (isSpraying)
        {
            CheckHazardCollision();
        }*/
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
        //ResetExtinguishTracking();
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

    /*private void CheckHazardCollision()
    {
        // Cast a ray forward from the nozzle tip
        if (Physics.Raycast(nozzleTransform.position, nozzleTransform.forward, out RaycastHit hit, maxSprayDistance))
        {
            // Check for the Ignis FlammableObject script on a burning object
            if (hit.collider.CompareTag("Active Hazard") && hit.collider.TryGetComponent<FlammableObject>(out FlammableObject ignisHazard))
            {
                Debug.Log("Hitting an Ignis fire object");
                currentHazard = ignisHazard.gameObject;

                // Calculate the extinguish radius expansion based on frame time
                float radiusIncrement = radiusIncrementSpeed * Time.deltaTime;

                // Ignis natively calculates the extinguish radius and cool-down automatically.
                // If you stop spraying before it is fully extinguished, Ignis will automatically
                // reignite and spread the fire based on its internal properties.
                ignisHazard.IncrementalExtinguish(hit.point, startRadius, radiusIncrement);

                // Start a lightweight monitor to watch this specific hazard's boolean state
                extinguishCoroutine = StartCoroutine(MonitorHazardState(ignisHazard));
            }
        }


        // OLD VERSION BEFORE IGNIS
        RaycastHit hit;
        // Cast a ray forward from the nozzle tip
        if (Physics.Raycast(nozzleTransform.position, nozzleTransform.forward, out hit, maxSprayDistance))
        {
            if (hit.collider.CompareTag("Active Hazard"))
            {
                GameObject hazard = hit.collider.gameObject;

                // If we hit a brand new hazard, start a new timer routine
                if (currentHazard != hazard)
                {
                    ResetExtinguishTracking(); // This will safely re-ignite the old hazard if we switched targets
                    currentHazard = hazard;

                    // Roll the duration upfront so both systems can use it
                    float requiredTime = Random.Range(5f, 20f);

                    if (currentHazard.TryGetComponent<HazardTemperature>(out HazardTemperature temp))
                    {
                        // Calculate exactly how fast it needs to cool down to hit 0 right when the timer ends
                        temp.coolDownSpeed = temp.temperature / requiredTime;
                        temp.Extinguish();
                    }

                    // Pass the rolled time into the coroutine
                    extinguishCoroutine = StartCoroutine(ExtinguishRoutine(currentHazard, requiredTime));
                }
                return;
            }
        }

        // If the ray hits nothing or misses the hazard, stop progress
        ResetExtinguishTracking();
    }*/

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

    /*private IEnumerator ExtinguishRoutine(GameObject hazard, float requiredTime)
    {
        float elapsedTime = 0f;
        Debug.Log($"Started spraying {hazard.name}. Needs {requiredTime:F1} seconds to extinguish.");

        while (elapsedTime < requiredTime)
        {
            elapsedTime += Time.deltaTime;
            yield return null; // Wait for the next frame
        }

        // Timer finished successfully! Find the controller and reset the object
        UniversalHazardController hazardController = FindAnyObjectByType<UniversalHazardController>();

        if (hazardController != null)
        {
            Debug.Log($"{hazard.name} extinguished successfully!");
            // Clear this BEFORE running the cleanup process to stop ResetExtinguishTracking from accidentally calling Ignite()
            currentHazard = null;
            hazardController.resetSingleObject(hazard);
        }
        else
        {
            Debug.LogError("UniversalHazardController script was not found in the scene!");
        }

        ResetExtinguishTracking();
    }*/
}