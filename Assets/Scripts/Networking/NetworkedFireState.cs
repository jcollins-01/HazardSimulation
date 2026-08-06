using Ignis;
using Normal.Realtime;
using System.Collections.Generic;
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
/// Ignis adapter note: FlammableObject exposes a network-only exact-state adapter so
/// puppets can be corrected in both directions instead of accumulating local steps.
/// The authority's replicated flags and progress are always the truth.
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
    [Tooltip("Seconds a puppet tolerates a locally-ignited fire that the authority says is not burning before forcing it out (covers ignition timing jitter).")]
    [SerializeField] private float phantomFireGraceSeconds = 1.5f;

    private FlammableObject _flammableObject;
    private FireProfileController _fireProfileController;

    // Fire-profile temperature can change every frame while regenerating. Publish
    // it at a bounded rate rather than producing one model write per rendered frame.
    private const float ProfileTemperatureSyncInterval = 0.1f;
    private const float ProfileTemperatureSyncEpsilon = 0.25f;
    private const float ExtinguishStateSyncInterval = 0.05f;
    private float _nextProfileTemperatureSyncTime;
    private float _nextExtinguishStateSyncTime;
    private float _lastPublishedWaterHitLocalTime = float.NegativeInfinity;

    // Puppet-mode bookkeeping
    private float _originalIgnitionTime;
    private bool _ignitionNeutered;
    private float _phantomFireSince = -1f;

    // Ownership-claim throttling. A Normcore ownership request is asynchronous,
    // so do not issue one every Update while waiting for the server response.
    private float _nextClaimAttemptTime;
    private bool _ownershipRequestPending;
    private const float OwnershipRequestRetrySeconds = 1f;

    // A remote avatar is considered ready once Normcore has instantiated it. The
    // authority schedules a short room-time lead so every client receives the reset.
    private const double JoinResetLeadSeconds = 0.75;
    private int _lastAppliedResetEpoch;

    // One avatar-manager subscription fans join notifications out to all fire
    // components in this process. This avoids hundreds of identical subscriptions.
    private static readonly HashSet<NetworkedFireState> ActiveFires = new HashSet<NetworkedFireState>();
    private static RealtimeAvatarManager _watchedAvatarManager;

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
        _fireProfileController = GetComponent<FireProfileController>();
    }

    private void OnDestroy()
    {
        ActiveFires.Remove(this);
        if (ActiveFires.Count == 0 && _watchedAvatarManager != null)
        {
            _watchedAvatarManager.avatarCreated -= OnAvatarCreated;
            _watchedAvatarManager = null;
        }
    }

    protected override void OnRealtimeModelReplaced(FireStateModel previousModel, FireStateModel currentModel)
    {
        _ownershipRequestPending = false;
        _nextClaimAttemptTime = 0f;
        _lastAppliedResetEpoch = 0;

        if (previousModel != null)
        {
            previousModel.isBurningDidChange -= OnIsBurningDidChange;
            previousModel.isExtinguishedDidChange -= OnIsExtinguishedDidChange;
            previousModel.isBurnedOutDidChange -= OnIsBurnedOutDidChange;
            previousModel.extinguishProgressDidChange -= OnExtinguishProgressDidChange;
            previousModel.putOutCenterLocalDidChange -= OnPutOutCenterDidChange;
            previousModel.heatVisualDidChange -= OnHeatVisualDidChange;
            previousModel.profileTemperatureDidChange -= OnProfileTemperatureDidChange;
            previousModel.profileReadyForSmolderDidChange -= OnProfileReadyForSmolderDidChange;
            previousModel.profileHasRolledSmolderDidChange -= OnProfileHasRolledSmolderDidChange;
            previousModel.profileWillReigniteDidChange -= OnProfileWillReigniteDidChange;
            previousModel.profileLastWaterHitRoomTimeDidChange -= OnProfileLastWaterHitRoomTimeDidChange;
            previousModel.profileReigniteAtRoomTimeDidChange -= OnProfileReigniteAtRoomTimeDidChange;
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
                currentModel.extinguishFadeStartRoomTime = 0.0;
                currentModel.extinguishFadeDuration = 0f;

                if (_fireProfileController != null)
                {
                    currentModel.profileTemperature = _fireProfileController.currentTemperature;
                    currentModel.profileReadyForSmolder = _fireProfileController.readyForSmolder;
                    currentModel.profileHasRolledSmolder = _fireProfileController.HasRolledSmolder;
                    currentModel.profileWillReignite = _fireProfileController.WillReignite;
                    currentModel.profileLastWaterHitRoomTime = GetLastWaterHitRoomTime();
                    currentModel.profileReigniteAtRoomTime = GetReigniteAtRoomTime();
                }

                currentModel.resetEpoch = 0;
                currentModel.resetAtRoomTime = 0.0;
                currentModel.resetShouldBurn = false;
                currentModel.resetCompletedEpoch = 0;
            }
            else
            {
                // Existing state from the datastore (e.g. we just joined): snap our
                // local visuals to it on the next Update pass.
                if (currentModel.isExtinguished || currentModel.isBurnedOut)
                    RunSharedExtinguishCleanup();

                ApplyProfileStateFromModel();

                // A past reset is already represented by this datastore snapshot.
                // Do not replay it when loading the scene long after it completed.
                if (realtime != null &&
                    currentModel.resetCompletedEpoch >= currentModel.resetEpoch &&
                    currentModel.resetAtRoomTime <= realtime.roomTime)
                {
                    _lastAppliedResetEpoch = currentModel.resetEpoch;
                }
            }

            currentModel.isBurningDidChange += OnIsBurningDidChange;
            currentModel.isExtinguishedDidChange += OnIsExtinguishedDidChange;
            currentModel.isBurnedOutDidChange += OnIsBurnedOutDidChange;
            currentModel.extinguishProgressDidChange += OnExtinguishProgressDidChange;
            currentModel.putOutCenterLocalDidChange += OnPutOutCenterDidChange;
            currentModel.heatVisualDidChange += OnHeatVisualDidChange;
            currentModel.profileTemperatureDidChange += OnProfileTemperatureDidChange;
            currentModel.profileReadyForSmolderDidChange += OnProfileReadyForSmolderDidChange;
            currentModel.profileHasRolledSmolderDidChange += OnProfileHasRolledSmolderDidChange;
            currentModel.profileWillReigniteDidChange += OnProfileWillReigniteDidChange;
            currentModel.profileLastWaterHitRoomTimeDidChange += OnProfileLastWaterHitRoomTimeDidChange;
            currentModel.profileReigniteAtRoomTimeDidChange += OnProfileReigniteAtRoomTimeDidChange;
            currentModel.ownerIDSelfDidChange += OnOwnerIDDidChange;
        }
    }

    private void Update()
    {
        if (model == null || _flammableObject == null)
            return;

        TryRegisterForJoinResets();

        TryClaimAuthority();

        if (TryApplyScheduledReset())
            return;

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
        ObserveFireProfileAndWriteModel();
        ApplySynchronizedExtinguishFade();
    }

    private void TryRegisterForJoinResets()
    {
        ActiveFires.Add(this);

        if (_watchedAvatarManager != null || realtime == null)
            return;

        RealtimeAvatarManager manager = realtime.GetComponent<RealtimeAvatarManager>();
        if (manager == null)
            return;

        _watchedAvatarManager = manager;
        _watchedAvatarManager.avatarCreated += OnAvatarCreated;
    }

    private static void OnAvatarCreated(RealtimeAvatarManager manager, RealtimeAvatar avatar, bool isLocalAvatar)
    {
        if (isLocalAvatar)
            return;

        // Copy before invoking because a reset can indirectly disable/destroy a fire.
        NetworkedFireState[] fires = new NetworkedFireState[ActiveFires.Count];
        ActiveFires.CopyTo(fires);
        for (int i = 0; i < fires.Length; i++)
        {
            if (fires[i] != null)
                fires[i].ScheduleJoinReset();
        }
    }

    private void ScheduleJoinReset()
    {
        if (model == null || realtime == null || !realtime.connected || !IsAuthority)
            return;

        // Dormant and already-finished fires remain dormant. A partially
        // extinguished but still-burning fire restarts from full strength.
        if (!model.isBurning || model.isExtinguished || model.isBurnedOut)
            return;

        double resetAt = realtime.roomTime + JoinResetLeadSeconds;
        if (model.resetEpoch > model.resetCompletedEpoch && model.resetAtRoomTime > realtime.roomTime)
        {
            // Coalesce teammates who arrive together into the same reset.
            model.resetAtRoomTime = resetAt;
            model.resetShouldBurn = true;
            return;
        }

        model.resetAtRoomTime = resetAt;
        model.resetShouldBurn = true;
        model.resetEpoch = model.resetEpoch + 1;
    }

    private bool TryApplyScheduledReset()
    {
        // After resetting locally, hold the pristine state until the authority's
        // reliable completion barrier arrives. Otherwise stale pre-reset progress
        // could be applied for a few frames on a higher-latency puppet.
        if (model.resetEpoch == _lastAppliedResetEpoch &&
            model.resetCompletedEpoch < model.resetEpoch)
        {
            return true;
        }

        if (model.resetEpoch <= _lastAppliedResetEpoch ||
            model.resetAtRoomTime <= 0.0 ||
            realtime == null ||
            !realtime.connected ||
            realtime.roomTime < model.resetAtRoomTime)
        {
            return false;
        }

        int resetEpoch = model.resetEpoch;
        bool shouldBurn = model.resetShouldBurn;

        _flammableObject.ResetObj();
        _flammableObject.ResetMaterialFromIgnis();
        if (_fireProfileController != null)
            _fireProfileController.ResetForNetworkRestart();
        if (shouldBurn)
            _flammableObject.SetOnFireFromCenter();

        _lastAppliedResetEpoch = resetEpoch;

        if (IsAuthority)
        {
            model.isBurning = shouldBurn;
            model.isExtinguished = false;
            model.isBurnedOut = false;
            model.extinguishProgress = 0f;
            model.putOutCenterLocal = Vector3.zero;
            model.extinguishFadeStartRoomTime = 0.0;
            model.extinguishFadeDuration = 0f;

            if (_fireProfileController != null)
            {
                model.profileTemperature = _fireProfileController.currentTemperature;
                model.profileReadyForSmolder = false;
                model.profileHasRolledSmolder = false;
                model.profileWillReignite = true;
                model.profileLastWaterHitRoomTime = 0.0;
                model.profileReigniteAtRoomTime = 0.0;
            }

            // Written last: reliable property ordering makes this the barrier that
            // tells puppets all post-reset state above is ready to consume.
            model.resetCompletedEpoch = resetEpoch;
        }

        return true;
    }

    private void ObserveFireProfileAndWriteModel()
    {
        if (_fireProfileController == null)
            return;

        float temperature = Mathf.Max(0f, _fireProfileController.currentTemperature);
        bool temperatureNeedsImmediateSync =
            temperature <= 0f ||
            model.profileTemperature <= 0f ||
            Mathf.Abs(temperature - model.profileTemperature) >= ProfileTemperatureSyncEpsilon;
        bool waterHitNeedsSync =
            _fireProfileController.LastWaterHitTime > _lastPublishedWaterHitLocalTime;

        if ((temperatureNeedsImmediateSync || waterHitNeedsSync) &&
            Time.unscaledTime >= _nextProfileTemperatureSyncTime)
        {
            model.profileTemperature = temperature;
            model.profileLastWaterHitRoomTime = GetLastWaterHitRoomTime();
            _lastPublishedWaterHitLocalTime = _fireProfileController.LastWaterHitTime;
            _nextProfileTemperatureSyncTime = Time.unscaledTime + ProfileTemperatureSyncInterval;
        }

        if (model.profileReadyForSmolder != _fireProfileController.readyForSmolder)
            model.profileReadyForSmolder = _fireProfileController.readyForSmolder;
        if (model.profileHasRolledSmolder != _fireProfileController.HasRolledSmolder)
            model.profileHasRolledSmolder = _fireProfileController.HasRolledSmolder;
        if (model.profileWillReignite != _fireProfileController.WillReignite)
            model.profileWillReignite = _fireProfileController.WillReignite;

        double reigniteAtRoomTime = GetReigniteAtRoomTime();
        if (System.Math.Abs(model.profileReigniteAtRoomTime - reigniteAtRoomTime) >= 0.01)
            model.profileReigniteAtRoomTime = reigniteAtRoomTime;
    }

    private double GetLastWaterHitRoomTime()
    {
        if (_fireProfileController == null ||
            float.IsNegativeInfinity(_fireProfileController.LastWaterHitTime) ||
            realtime == null ||
            !realtime.connected)
        {
            return 0.0;
        }

        return realtime.roomTime - (Time.time - _fireProfileController.LastWaterHitTime);
    }

    private double GetReigniteAtRoomTime()
    {
        if (_fireProfileController == null ||
            _fireProfileController.ReigniteAttemptTime < 0f ||
            realtime == null ||
            !realtime.connected)
        {
            return 0.0;
        }

        return realtime.roomTime + (_fireProfileController.ReigniteAttemptTime - Time.time);
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
        bool forceProgressWrite = extinguished != model.isExtinguished || burnedOut != model.isBurnedOut;

        // Publish the timing values as soon as the authority observes extinguish.
        // Puppets wait until both values are present before using the shared clock,
        // so property delivery order cannot produce an invalid timeline.
        if (extinguished &&
            model.extinguishFadeStartRoomTime <= 0.0 &&
            realtime != null &&
            realtime.connected)
        {
            float duration = Mathf.Max(0.0001f, _flammableObject.burnOutLength_s);
            float fadeProgressSeconds = Mathf.Max(0f, _flammableObject.onFireTimer - _flammableObject.burnOutStart_s);

            model.extinguishFadeDuration = duration;
            model.extinguishFadeStartRoomTime = realtime.roomTime - fadeProgressSeconds;
        }
        else if (!extinguished && model.isExtinguished)
        {
            // The fire was reset/reignited. Retire the previous cycle's clock so a
            // later extinguish publishes a fresh start time and duration.
            model.extinguishFadeStartRoomTime = 0.0;
            model.extinguishFadeDuration = 0f;
        }

        if (model.isBurning != burning)
            model.isBurning = burning;
        if (model.isExtinguished != extinguished)
            model.isExtinguished = extinguished;
        if (model.isBurnedOut != burnedOut)
            model.isBurnedOut = burnedOut;

        if (forceProgressWrite || Time.unscaledTime >= _nextExtinguishStateSyncTime)
        {
            float threshold = GetExtinguishThreshold();
            float progress = threshold > 0f ? Mathf.Clamp01(_flammableObject.GetPutOutRadius() / threshold) : 0f;
            if (Mathf.Abs(progress - model.extinguishProgress) >= 0.002f || forceProgressWrite)
                model.extinguishProgress = progress;

            Vector3 centerLocal = transform.InverseTransformPoint(_flammableObject.GetPutOutCenter());
            if ((centerLocal - model.putOutCenterLocal).sqrMagnitude > 0.0001f || forceProgressWrite)
                model.putOutCenterLocal = centerLocal;

            _nextExtinguishStateSyncTime = Time.unscaledTime + ExtinguishStateSyncInterval;
        }
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
        ApplyProfileStateFromModel();

        if (model.isBurning)
        {
            ApplyBurningVisual();
            ApplyExactExtinguishState();
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

        ApplySynchronizedExtinguishFade();
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
    /// Snaps the local Ignis put-out area to the replicated state. This corrects
    /// puppets that are either ahead or behind the authority.
    /// </summary>
    private void ApplyExactExtinguishState()
    {
        if (!_flammableObject.onFire)
            return;

        float threshold = GetExtinguishThreshold();
        Vector3 centerWorld = transform.TransformPoint(model.putOutCenterLocal);
        float targetRadius = model.extinguishProgress * Mathf.Max(0f, threshold);
        _flammableObject.ApplyNetworkExtinguishState(centerWorld, targetRadius, model.isExtinguished);
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

    /// <summary>
    /// Drives the post-extinguish Ignis timer from Normcore's synchronized room
    /// clock. Both the authority and puppets run this path, so all clients finish
    /// the fade at the same logical server time. A late joiner immediately derives
    /// the correct in-progress point instead of restarting the fade locally.
    /// </summary>
    private void ApplySynchronizedExtinguishFade()
    {
        if (model == null ||
            !model.isExtinguished ||
            model.extinguishFadeStartRoomTime <= 0.0 ||
            model.extinguishFadeDuration <= 0f ||
            realtime == null ||
            !realtime.connected ||
            !_flammableObject.onFire)
        {
            return;
        }

        float duration = model.extinguishFadeDuration;
        double elapsedRoomTime = realtime.roomTime - model.extinguishFadeStartRoomTime;
        float elapsed = Mathf.Clamp((float)elapsedRoomTime, 0f, duration + 0.01f);

        // Ignis compares onFireTimer against its local burnOutLength_s, so apply
        // the authority's duration before positioning the timer on the shared
        // timeline. Assigning (rather than only advancing) also corrects clients
        // whose local simulation ran slightly ahead.
        _flammableObject.burnOutLength_s = duration;
        _flammableObject.onFireTimer = _flammableObject.burnOutStart_s + elapsed;
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
            ApplyExactExtinguishState();
    }

    private void OnPutOutCenterDidChange(FireStateModel changedModel, Vector3 value)
    {
        if (!IsAuthority)
            ApplyExactExtinguishState();
    }

    private void OnHeatVisualDidChange(FireStateModel changedModel, bool value)
    {
        ApplyHeatVisual(value);
    }

    private void OnProfileTemperatureDidChange(FireStateModel changedModel, float value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void OnProfileReadyForSmolderDidChange(FireStateModel changedModel, bool value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void OnProfileHasRolledSmolderDidChange(FireStateModel changedModel, bool value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void OnProfileWillReigniteDidChange(FireStateModel changedModel, bool value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void OnProfileLastWaterHitRoomTimeDidChange(FireStateModel changedModel, double value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void OnProfileReigniteAtRoomTimeDidChange(FireStateModel changedModel, double value)
    {
        if (!IsAuthority)
            ApplyProfileStateFromModel();
    }

    private void ApplyProfileStateFromModel()
    {
        if (model == null || _fireProfileController == null)
            return;

        float secondsSinceLastWaterHit = model.profileLastWaterHitRoomTime > 0.0 && realtime != null
            ? Mathf.Max(0f, (float)(realtime.roomTime - model.profileLastWaterHitRoomTime))
            : float.PositiveInfinity;
        float secondsUntilReignite = model.profileReigniteAtRoomTime > 0.0 && realtime != null
            ? Mathf.Max(0f, (float)(model.profileReigniteAtRoomTime - realtime.roomTime))
            : -1f;

        _fireProfileController.ApplyNetworkState(
            model.profileTemperature,
            model.profileReadyForSmolder,
            model.profileHasRolledSmolder,
            model.profileWillReignite,
            secondsSinceLastWaterHit,
            secondsUntilReignite);
    }

    private void OnOwnerIDDidChange(RealtimeModel changedModel, int newOwnerID)
    {
        // The request either succeeded locally, was granted to another client, or
        // ownership was released. In all cases it is no longer pending.
        _ownershipRequestPending = false;

        if (isOwnedLocallySelf)
        {
            // We just became the authority (initial claim or failover).
            ApplyProfileStateFromModel();
            RestoreAuthorityIgnition();

            if (model != null && model.isBurning && !_flammableObject.onFire && !_flammableObject.hasBurnedOut())
                _flammableObject.SetOnFireFromCenter();
        }
        else if (newOwnerID != -1)
        {
            // Someone else is the authority now: become a puppet.
            NeuterLocalIgnition();
            ApplyProfileStateFromModel();
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
