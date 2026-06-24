using System.Collections;
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

    private XRGrabInteractable grabInteractable;
    private bool isSpraying = false;

    // Tracking variables for extinguishing hazards
    private GameObject currentHazard;
    private Coroutine extinguishCoroutine;

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
        if (isSpraying)
        {
            CheckHazardCollision();
        }
    }

    private void OnTriggerPulled(ActivateEventArgs args)
    {
        Debug.Log("Trigger pulled! XRI Event fired successfully.");
        isSpraying = true;
        if (waterParticles != null) waterParticles.Play();
    }

    private void OnTriggerReleased(DeactivateEventArgs args)
    {
        Debug.Log("Trigger released.");
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
        ResetExtinguishTracking();
    }

    private void CheckHazardCollision()
    {
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
                    ResetExtinguishTracking();
                    currentHazard = hazard;
                    extinguishCoroutine = StartCoroutine(ExtinguishRoutine(currentHazard));
                }
                return;
            }
        }

        // If the ray hits nothing or misses the hazard, stop progress
        ResetExtinguishTracking();
    }

    private void ResetExtinguishTracking()
    {
        if (extinguishCoroutine != null)
        {
            StopCoroutine(extinguishCoroutine);
            extinguishCoroutine = null;
        }
        currentHazard = null;
    }

    private IEnumerator ExtinguishRoutine(GameObject hazard)
    {
        // Roll a random duration between 5 and 20 seconds
        float requiredTime = Random.Range(5f, 20f);
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
            hazardController.resetSingleObject(hazard);
        }
        else
        {
            Debug.LogError("UniversalHazardController script was not found in the scene!");
        }

        ResetExtinguishTracking();
    }
}