using System.Collections.Generic;
using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Replicates the "is spraying" state of a hose / fire extinguisher to all clients.
///
/// Data flow:
///  - The current owner of the tool (whoever grabbed it, see NetworkedGrabInteractable)
///    is the only client that reads XR trigger input. Spray.cs forwards that input here
///    via SetSprayInputHeld(), and this component writes it into the model.
///  - Every client (owner included) reacts to the replicated model property and
///    plays/stops its LOCAL particle system. The particles themselves are never
///    networked; only this one bool is.
///
/// The nozzle pose does not need its own replication: the nozzle is a child of the
/// tool root, whose transform is already synchronized by the existing RealtimeTransform.
/// </summary>
[RequireComponent(typeof(RealtimeView))]
public class SpraySync : RealtimeComponent<SpraySyncModel>
{
    [Header("Spray Visuals")]
    [Tooltip("Particle system played/stopped from the replicated spray state. Auto-filled from Spray if left empty.")]
    [SerializeField] private ParticleSystem sprayParticles;

    // All SpraySync instances in the scene. NetworkedFireState uses this on the
    // fire-authority client to find every tool that is currently spraying.
    public static readonly List<SpraySync> All = new List<SpraySync>();

    private Spray _spray;

    // Input latch written by Spray.cs on the owning client only.
    private bool _inputHeld;

    /// <summary>Replicated spray state (false before the model is ready).</summary>
    public bool IsSpraying => model != null && model.isSpraying;

    /// <summary>
    /// True when this client may write the spray state: either we own the tool, or
    /// there is no room connection at all (offline / single-player testing), in which
    /// case the local model is writable and behaves exactly like the old local bool.
    /// </summary>
    private bool CanWriteState =>
        model != null && (realtime == null || !realtime.connected || isOwnedLocallySelf);

    /// <summary>Nozzle pose is synchronized implicitly via the root RealtimeTransform.</summary>
    public Transform NozzleTransform => _spray != null ? _spray.NozzleTransform : null;
    public float MaxSprayDistance => _spray != null ? _spray.MaxSprayDistance : 10f;
    public float SprayStartRadius => _spray != null ? _spray.StartRadius : 0.5f;
    public float SprayRadiusIncrementSpeed => _spray != null ? _spray.RadiusIncrementSpeed : 0.5f;

    private void Awake()
    {
        _spray = GetComponent<Spray>();
        if (sprayParticles == null && _spray != null)
            sprayParticles = _spray.WaterParticles;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        // If this object is disabled locally, stop the local visual immediately.
        ApplyParticleState(false);
    }

    protected override void OnRealtimeModelReplaced(SpraySyncModel previousModel, SpraySyncModel currentModel)
    {
        if (previousModel != null)
        {
            previousModel.isSprayingDidChange -= OnIsSprayingDidChange;
            previousModel.ownerIDSelfDidChange -= OnOwnerIDDidChange;
        }

        if (currentModel != null)
        {
            if (currentModel.isFreshModel)
                currentModel.isSpraying = false;

            // Apply the current state right away so a client joining mid-spray
            // immediately sees the tool spraying.
            ApplyParticleState(currentModel.isSpraying);

            currentModel.isSprayingDidChange += OnIsSprayingDidChange;
            currentModel.ownerIDSelfDidChange += OnOwnerIDDidChange;
        }
    }

    private void OnIsSprayingDidChange(SpraySyncModel changedModel, bool isSpraying)
    {
        ApplyParticleState(isSpraying);
    }

    private void OnOwnerIDDidChange(RealtimeModel changedModel, int newOwnerID)
    {
        // If the owner disconnected (or released ownership) while the state was
        // still "spraying", the model is now unowned and any client may repair it.
        // Writing the same value from multiple clients is idempotent and harmless.
        if (newOwnerID == -1 && model != null && model.isSpraying)
            model.isSpraying = false;
    }

    private void Update()
    {
        if (model == null)
            return;

        // Only the tool owner writes spray state. Reconciliation instead of blind
        // per-frame writes: we write only when the latch and the model disagree,
        // which also covers the brief window while an ownership request is in flight.
        if (CanWriteState && model.isSpraying != _inputHeld)
            model.isSpraying = _inputHeld;
    }

    /// <summary>
    /// Called by Spray.cs on the owning client when the XR trigger is pulled/released
    /// or the tool is dropped. Never call this from remote clients.
    /// </summary>
    public void SetSprayInputHeld(bool held)
    {
        _inputHeld = held;

        // Fast path: if we already own the model, push the change immediately
        // instead of waiting one frame for Update().
        if (CanWriteState && model.isSpraying != held)
            model.isSpraying = held;
    }

    private void ApplyParticleState(bool isSpraying)
    {
        if (sprayParticles == null)
            return;

        // Only touch the particle system on actual transitions.
        if (isSpraying && !sprayParticles.isPlaying)
            sprayParticles.Play();
        else if (!isSpraying && sprayParticles.isPlaying)
            sprayParticles.Stop();
    }
}
