using Ignis;
using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Gives one fire / hazard object (an Ignis <see cref="FlammableObject"/>) a shared,
/// authoritative network state.
///
/// Authority model:
///  - The owner of this component's model is the single authority for this fire.
///    Ownership is claimed automatically: scene views start unowned, the first client
///    that notices requests ownership, Normcore grants it to exactly one client.
///    If the authority disconnects, Normcore clears ownership and a remaining client
///    claims it again (failover). The currently spraying player does NOT own the fire;
///    fire ownership is stable and independent of tool ownership.
///  - Only the authority runs the "real" Ignis simulation: ignition, spread,
///    back-spread/reignition, burnout, and extinguishing (via spray raycast checks
///    against synchronized tool poses + gated Ignis ParticleExtinguish/RaycastExtinguish).
///    It then mirrors the resulting high-level state into the model.
///  - Every non-authority client runs its local FlammableObject in "puppet" mode:
///    local ignition is neutered (ignitionTime = MaxValue) so Ignis spread triggers can
///    never make permanent local decisions, and the replicated model drives the local
///    Ignis visuals (ignite / extinguish hole growth / fade-out / burnout).
///
/// Ignis adapter note: FlammableObject exposes no "set exact extinguish progress" API
/// (putOutRadius/extinguished are private), so remotes cannot be snapped to an exact
/// internal state. Instead they call the public IncrementalExtinguish() in small steps
/// until their local put-out radius reaches the replicated progress, and final states
/// are enforced via the public onFireTimer field (the same mechanism Ignis itself uses
/// when a fire is extinguished). The authority's replicated flags are always the truth.
/// </summary>
[RequireComponent(typeof(FlammableObject))]
public class NetworkedFireState : RealtimeComponent<FireStateModel>
{
    [Header("Spray Hit Detection (authority only)")]
    [Tooltip("Radius of the sphere cast used by the fire authority to detect spraying tools hitting this fire.")]
    [SerializeField] private float sprayHitRadius = 0.1f;

    [Tooltip("Layers considered when checking whether a spraying tool hits this fire.")]
    [SerializeField] private LayerMask sprayHitMask = ~0;

    [Header("Puppet Visuals")]
    [Tooltip("How fast a puppet's local extinguish hole catches up to the replicated progress (fraction of the remaining gap per second).")]
    [SerializeField] private float progressCatchUpSpeed = 6f;

    [Tooltip("Seconds a puppet tolerates a locally-ignited fire that the authority says is not burning before forcing it out (covers ignition timing jitter).")]
    [SerializeField] private float phantomFireGraceSeconds = 1.5f;

    private FlammableObject _flammableObject;

    // Puppet-mode bookkeeping
    private float _originalIgnitionTime;
    private bool _ignitionNeutered;
    private float _phantomFireSince = -1f;

    // Ownership-claim throttling. A Normcore ownership request is asynchronous,
    // so do not issue one every Update while waiting for the server response.
    private float _nextClaimAttemptTime;
    private bool _ownershipRequestPending;
    private const float OwnershipRequestRetrySeconds = 1f;

    // Cached scene references for the shared extinguish cleanup
    private static UniversalHazardController _hazardController;

    /// <summary>
    /// True if this client is the authority for this fire (or there is no room
    /// connection at all, i.e. offline/single-player play).
    /// </summary>
    public bool IsAuthority
    {
        get
        {
            if (realtime == null || !realtime.connected)
                return true; // offline: preserve legacy single-player behavior
            return isOwnedLocallySelf;
        }
    }

    /// <summary>
    /// Gate used by Ignis interaction scripts (ParticleExtinguish / RaycastExtinguish):
    /// only the fire authority may apply permanent extinguish effects to this fire.
    /// Fires without a NetworkedFireState keep their legacy local behavior.
    /// </summary>
    public static bool LocalClientMayAffectFire(FlammableObject fire)
    {
        if (fire == null)
            return false;

        NetworkedFireState state = fire.GetComponent<NetworkedFireState>();
        if (state == null)
            return true; // not networked: legacy local behavior

        return state.IsAuthority;
    }

    private void Awake()
    {
        _flammableObject = GetComponent<FlammableObject>();
    }

    protected override void OnRealtimeModelReplaced(FireStateModel previousModel, FireStateModel currentModel)
    {
        _ownershipRequestPending = false;
        _nextClaimAttemptTime = 0f;

        if (previousModel != null)
        {
            previousModel.isBurningDidChange -= OnIsBurningDidChange;
            previousModel.isExtinguishedDidChange -= OnIsExtinguishedDidChange;
            previousModel.isBurnedOutDidChange -= OnIsBurnedOutDidChange;
            previousModel.extinguishProgressDidChange -= OnExtinguishProgressDidChange;
            previousModel.putOutCenterLocalDidChange -= OnPutOutCenterDidChange;
            previousModel.heatVisualDidChange -= OnHeatVisualDidChange;
            previousModel.ownerIDSelfDidChange -= OnOwnerIDDidChange;
        }

        if (currentModel != null)
        {
            if (currentModel.isFreshModel)
            {
                // Seed the fresh model from the local Ignis state (usually all false).
                currentModel.isBurning = _flammableObject.onFire;
                currentModel.isExtinguished = false;
                currentModel.isBurnedOut = false;
                currentModel.extinguishProgress = 0f;
                currentModel.putOutCenterLocal = Vector3.zero;
                currentModel.heatVisual = false;
            }
            else
            {
                // Existing state from the datastore (e.g. we just joined): snap our
                // local visuals to it on the next Update pass.
                if (currentModel.isExtinguished || currentModel.isBurnedOut)
                    RunSharedExtinguishCleanup();
            }

            currentModel.isBurningDidChange += OnIsBurningDidChange;
            currentModel.isExtinguishedDidChange += OnIsExtinguishedDidChange;
            currentModel.isBurnedOutDidChange += OnIsBurnedOutDidChange;
            currentModel.extinguishProgressDidChange += OnExtinguishProgressDidChange;
            currentModel.putOutCenterLocalDidChange += OnPutOutCenterDidChange;
            currentModel.heatVisualDidChange += OnHeatVisualDidChange;
            currentModel.ownerIDSelfDidChange += OnOwnerIDDidChange;
        }
    }

    private void Update()
    {
        if (model == null || _flammableObject == null)
            return;

        TryClaimAuthority();

        if (IsAuthority)
            AuthorityUpdate();
        else
            PuppetUpdate();
    }

    // ------------------------------------------------------------------
    // Authority
    // ------------------------------------------------------------------

    /// <summary>
    /// Claims stable ownership of this fire for this client if nobody owns it.
    /// Normcore grants the request to exactly one claimant; if the owner leaves,
    /// ownership is cleared and a remaining client re-claims it here.
    /// </summary>
    private void TryClaimAuthority()
    {
        // Ownership only exists for an initialized component model in a live room.
        // Offline mode deliberately keeps the legacy local-authority behavior.
        if (model == null || realtime == null || !realtime.connected)
            return;

        if (!isUnownedSelf)
        {
            _ownershipRequestPending = false;
            return;
        }

        // Wait for the in-flight request to resolve. If no ownership update arrives
        // (for example after a transient disconnect), permit a single retry later.
        if (_ownershipRequestPending && Time.time < _nextClaimAttemptTime)
            return;

        _ownershipRequestPending = true;
        _nextClaimAttemptTime = Time.time + OwnershipRequestRetrySeconds;

        RequestOwnership();

        // The moving hazard agent also needs its transform synced from the same
        // authority so every client sees identical movement/spread touches.
        if (realtimeView != null && realtimeView.isUnownedSelf)
            realtimeView.RequestOwnership();

        RealtimeTransform rt = GetComponent<RealtimeTransform>();
        if (rt != null && rt.isUnownedSelf)
            rt.RequestOwnership();
    }

    private void AuthorityUpdate()
    {
        RestoreAuthorityIgnition();

        // If we just became authority of a fire that should be burning but our local
        // Ignis is not (authority failover), resume the simulation from the model.
        if (model.isBurning && !_flammableObject.onFire && !_flammableObject.hasBurnedOut())
            _flammableObject.SetOnFireFromCenter();

        ApplySprayHits();
        ObserveIgnisAndWriteModel();
    }

    /// <summary>
    /// Fire-authority-side spray hit detection: checks every currently spraying tool
    /// (poses synchronized via each tool's RealtimeTransform) against this fire and
    /// feeds Ignis' incremental extinguish. Multiple players spraying the same fire
    /// simply add up here, on a single client, so there are no state conflicts.
    /// </summary>
    private void ApplySprayHits()
    {
        if (!_flammableObject.onFire)
            return;

        for (int i = 0; i < SpraySync.All.Count; i++)
        {
            SpraySync sprayer = SpraySync.All[i];
            if (sprayer == null || !sprayer.IsSpraying)
                continue;

            Transform nozzle = sprayer.NozzleTransform;
            if (nozzle == null)
                continue;

            if (!Physics.SphereCast(nozzle.position, sprayHitRadius, nozzle.forward, out RaycastHit hit, sprayer.MaxSprayDistance, sprayHitMask, QueryTriggerInteraction.Collide))
                continue;

            if (hit.collider.GetComponentInParent<FlammableObject>() != _flammableObject)
                continue;

            _flammableObject.IncrementalExtinguish(hit.point, sprayer.SprayStartRadius, sprayer.SprayRadiusIncrementSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Reads the authority's local Ignis simulation and publishes the compact
    /// high-level state. Ignis does change detection on its side, and we only write
    /// on meaningful deltas, so network traffic stays tiny.
    /// </summary>
    private void ObserveIgnisAndWriteModel()
    {
        bool burning = _flammableObject.onFire;
        bool extinguished = _flammableObject.IsExtinguished();
        bool burnedOut = _flammableObject.hasBurnedOut();

        if (model.isBurning != burning)
            model.isBurning = burning;
        if (model.isExtinguished != extinguished)
            model.isExtinguished = extinguished;
        if (model.isBurnedOut != burnedOut)
            model.isBurnedOut = burnedOut;

        float threshold = GetExtinguishThreshold();
        float progress = threshold > 0f ? Mathf.Clamp01(_flammableObject.GetPutOutRadius() / threshold) : 0f;
        if (Mathf.Abs(progress - model.extinguishProgress) >= 0.002f)
            model.extinguishProgress = progress;

        Vector3 centerLocal = transform.InverseTransformPoint(_flammableObject.GetPutOutCenter());
        if ((centerLocal - model.putOutCenterLocal).sqrMagnitude > 0.0001f)
            model.putOutCenterLocal = centerLocal;
    }

    /// <summary>
    /// Radius of put-out area at which Ignis considers this object fully extinguished.
    /// Mirrors the check inside FlammableObject.IncrementalExtinguish.
    /// </summary>
    private float GetExtinguishThreshold()
    {
        return _flammableObject.GetObjectApproxSize() * _flammableObject.fullExtinguishToughness * 0.5f;
    }

    // ------------------------------------------------------------------
    // Puppet (non-authority) visual mirroring
    // ------------------------------------------------------------------

    private void PuppetUpdate()
    {
        NeuterLocalIgnition();

        if (model.isBurning)
        {
            ApplyBurningVisual();
            ApplyExtinguishProgressVisual();
            _phantomFireSince = -1f;
        }
        else if (_flammableObject.onFire)
        {
            if (model.isExtinguished || model.isBurnedOut)
            {
                ApplyExtinguishedVisual(model.isBurnedOut);
                _phantomFireSince = -1f;
            }
            else
            {
                // Local Ignis ignited on its own (e.g. setThisOnFireOnStart before the
                // model arrived) but the authority says this object is not burning.
                // Give the authority a short grace window to catch up, then force it out.
                if (_phantomFireSince < 0f)
                    _phantomFireSince = Time.time;

                if (Time.time - _phantomFireSince > phantomFireGraceSeconds)
                    ApplyExtinguishedVisual(false);
            }
        }
        else
        {
            _phantomFireSince = -1f;
        }
    }

    /// <summary>
    /// Prevents this puppet's local Ignis from making permanent ignition decisions
    /// (spread triggers etc.). SetOnFireFromCenter() still works for visual ignition
    /// because it bypasses the ignition-time accumulation.
    /// </summary>
    private void NeuterLocalIgnition()
    {
        if (_ignitionNeutered)
            return;

        _originalIgnitionTime = _flammableObject.ignitionTime;
        _flammableObject.ignitionTime = float.MaxValue;
        _ignitionNeutered = true;
    }

    private void RestoreAuthorityIgnition()
    {
        if (!_ignitionNeutered)
            return;

        _flammableObject.ignitionTime = _originalIgnitionTime;
        _ignitionNeutered = false;
    }

    private void ApplyBurningVisual()
    {
        if (!_flammableObject.onFire)
            _flammableObject.SetOnFireFromCenter();
    }

    /// <summary>
    /// Grows the local Ignis put-out area toward the replicated progress.
    /// IncrementalExtinguish is additive, so we feed it the remaining gap in small
    /// steps; if we are already ahead we let Ignis' own back-spread shrink it.
    /// </summary>
    private void ApplyExtinguishProgressVisual()
    {
        if (!_flammableObject.onFire)
            return;

        float threshold = GetExtinguishThreshold();
        if (threshold <= 0f)
            return;

        float targetRadius = model.extinguishProgress * threshold;
        float localRadius = _flammableObject.GetPutOutRadius();
        float gap = targetRadius - localRadius;
        if (gap <= 0.001f)
            return;

        Vector3 centerWorld = transform.TransformPoint(model.putOutCenterLocal);
        float step = Mathf.Min(gap, Mathf.Max(0.002f, gap * progressCatchUpSpeed * Time.deltaTime));
        _flammableObject.IncrementalExtinguish(centerWorld, step, step);
    }

    /// <summary>
    /// Forces the local Ignis visual into the extinguished/burned-out fade, using the
    /// same public onFireTimer mechanism Ignis uses internally when a fire goes out.
    /// </summary>
    private void ApplyExtinguishedVisual(bool skipToBurnedOut)
    {
        if (!_flammableObject.onFire)
            return;

        _flammableObject.onFireTimer = skipToBurnedOut
            ? _flammableObject.burnOutStart_s + _flammableObject.burnOutLength_s + 0.01f
            : _flammableObject.burnOutStart_s + 0.01f;
    }

    // ------------------------------------------------------------------
    // Model change handlers (all clients)
    // ------------------------------------------------------------------

    private void OnIsBurningDidChange(FireStateModel changedModel, bool value)
    {
        if (IsAuthority)
            return; // authority's Ignis already shows reality

        if (value)
            ApplyBurningVisual();
        else if (changedModel.isExtinguished || changedModel.isBurnedOut)
            ApplyExtinguishedVisual(changedModel.isBurnedOut);
    }

    private void OnIsExtinguishedDidChange(FireStateModel changedModel, bool value)
    {
        if (!value)
            return;

        if (!IsAuthority)
            ApplyExtinguishedVisual(false);

        RunSharedExtinguishCleanup();
    }

    private void OnIsBurnedOutDidChange(FireStateModel changedModel, bool value)
    {
        if (!value)
            return;

        if (!IsAuthority)
            ApplyExtinguishedVisual(true);

        RunSharedExtinguishCleanup();
    }

    private void OnExtinguishProgressDidChange(FireStateModel changedModel, float value)
    {
        if (!IsAuthority)
            ApplyExtinguishProgressVisual();
    }

    private void OnPutOutCenterDidChange(FireStateModel changedModel, Vector3 value)
    {
        if (!IsAuthority)
            ApplyExtinguishProgressVisual();
    }

    private void OnHeatVisualDidChange(FireStateModel changedModel, bool value)
    {
        ApplyHeatVisual(value);
    }

    private void OnOwnerIDDidChange(RealtimeModel changedModel, int newOwnerID)
    {
        // The request either succeeded locally, was granted to another client, or
        // ownership was released. In all cases it is no longer pending.
        _ownershipRequestPending = false;

        if (isOwnedLocallySelf)
        {
            // We just became the authority (initial claim or failover).
            RestoreAuthorityIgnition();

            if (model != null && model.isBurning && !_flammableObject.onFire && !_flammableObject.hasBurnedOut())
                _flammableObject.SetOnFireFromCenter();
        }
        else if (newOwnerID != -1)
        {
            // Someone else is the authority now: become a puppet.
            NeuterLocalIgnition();
        }
    }

    // ------------------------------------------------------------------
    // Shared state side effects
    // ------------------------------------------------------------------

    /// <summary>
    /// Replicated "should glow hot on the thermal camera" flag. Called by the hazard
    /// spread logic on the authority when it ignites an object's HazardTemperature.
    /// Safe to call offline (applies locally instead of writing the model).
    /// </summary>
    public void SetHeatVisual(bool value)
    {
        if (model != null && realtime != null && realtime.connected)
        {
            if (IsAuthority && model.heatVisual != value)
                model.heatVisual = value;
        }
        else
        {
            ApplyHeatVisual(value);
        }
    }

    private void ApplyHeatVisual(bool value)
    {
        if (value)
        {
            // Mirrors UniversalHazardController.Spreading(): add + ignite if missing.
            if (!TryGetComponent<HazardTemperature>(out HazardTemperature temp))
                temp = gameObject.AddComponent<HazardTemperature>();
            temp.Ignite();
        }
        else if (TryGetComponent<HazardTemperature>(out HazardTemperature existing))
        {
            existing.ResetTemperature();
        }
    }

    /// <summary>
    /// Runs on every client when the replicated state says this fire was extinguished
    /// or burned out. Replaces the old per-client Spray.MonitorHazardState flow so all
    /// clients perform the same cleanup for the same fire at the same logical moment.
    /// Idempotent by construction (tag/material/temperature resets are idempotent).
    /// </summary>
    private void RunSharedExtinguishCleanup()
    {
        if (_hazardController == null)
            _hazardController = FindAnyObjectByType<UniversalHazardController>();

        if (_hazardController != null)
            _hazardController.resetSingleObject(gameObject);

        if (TryGetComponent<HazardTemperature>(out HazardTemperature temp))
            temp.ResetTemperature();
    }
}
