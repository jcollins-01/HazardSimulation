using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Ignis;

/// <summary>
/// Reads the XR trigger on the client that currently holds this hose / extinguisher
/// and forwards it to <see cref="SpraySync"/>, which replicates the spray state.
///
/// This script deliberately contains NO fire-extinguish logic anymore:
///  - Particle collisions / Ignis ParticleExtinguish run on every client locally, so
///    letting them decide fire state made clients diverge.
///  - Permanent fire state is now decided only by each fire's authority and replicated
///    via <see cref="NetworkedFireState"/>.
///
/// If there is no SpraySync (e.g. an unnetworked test scene), the old local
/// Play/Stop behavior is preserved as a fallback.
/// </summary>
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

    [Header("Fire Compatibility")]
    [Tooltip("When enabled, this tool only extinguishes fires using the selected combined behavior/type profile.")]
    [SerializeField] private bool restrictByFireProfile;
    [SerializeField] private FireProfileController.FireProfile extinguishingProfile = FireProfileController.FireProfile.ClassA;

    private XRGrabInteractable grabInteractable;
    private SpraySync spraySync;

    // Public accessors so SpraySync and NetworkedFireState can read the same
    // inspector values instead of duplicating them.
    public ParticleSystem WaterParticles => waterParticles;
    public Transform NozzleTransform => nozzleTransform;
    public float MaxSprayDistance => maxSprayDistance;
    public float StartRadius => startRadius;
    public float RadiusIncrementSpeed => radiusIncrementSpeed;
    public bool RestrictsByFireProfile => restrictByFireProfile;
    public FireProfileController.FireProfile ExtinguishingProfile => extinguishingProfile;

    public bool CanExtinguish(FireProfileController fireProfile)
    {
        return fireProfile == null ||
            !restrictByFireProfile ||
            fireProfile.CanBeExtinguishedBy(extinguishingProfile);
    }

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        spraySync = GetComponent<SpraySync>();

        // Subscribe to XRI trigger events. These only fire on the client that is
        // physically holding the tool, so only the holder ever writes spray state.
        grabInteractable.activated.AddListener(OnTriggerPulled);
        grabInteractable.deactivated.AddListener(OnTriggerReleased);
        grabInteractable.selectExited.AddListener(OnToolDropped);

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
            grabInteractable.selectExited.RemoveListener(OnToolDropped);
        }
    }

    private void OnTriggerPulled(ActivateEventArgs args)
    {
        SetSpraying(true);
    }

    private void OnTriggerReleased(DeactivateEventArgs args)
    {
        SetSpraying(false);
    }

    private void OnToolDropped(SelectExitEventArgs args)
    {
        // Dropping always stops the spray, even if the trigger was still held.
        SetSpraying(false);
    }

    private void SetSpraying(bool spraying)
    {
        if (spraySync != null)
        {
            // Networked path: the latch is written into the replicated model by
            // SpraySync (only if we own the tool); all clients render from the model.
            spraySync.SetSprayInputHeld(spraying);
        }
        else
        {
            // Offline fallback: original local behavior.
            if (waterParticles == null) return;
            if (spraying && !waterParticles.isPlaying) waterParticles.Play();
            else if (!spraying && waterParticles.isPlaying) waterParticles.Stop();
        }
    }
}
