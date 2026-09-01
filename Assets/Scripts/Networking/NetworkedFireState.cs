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
///  - Every non-authority client runs its local FlammableObject in strict puppet mode:
///    autonomous ignition, reignition, burnout and cleanup are disabled, and only the
///    replicated model may drive its visuals and lifecycle flags.
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

    private FlammableObject _flammableObject;
    private FireProfileController _fireProfileController;

    // Fire-profile temperature can change every frame while regenerating. Publish
    // it at a bounded rate rather than producing one model write per rendered frame.
    private const float ProfileTemperatureSyncInterval = 0.1f;
    private const float ProfileTemperatureSyncEpsilon = 0.25f;
    private const float ExtinguishStateSyncInterval = 0.05f;
    private const float FireSpreadSyncInterval = 0.2f;
    private const float FireSpreadSyncEpsilon = 0.002f;
    private float _nextProfileTemperatureSyncTime;
    private float _nextExtinguishStateSyncTime;
    private float _nextFireSpreadSyncTime;
    private float _lastPublishedWaterHitLocalTime = float.NegativeInfinity;

    // Puppet-mode bookkeeping
    private bool _ignitionNeutered;
    private int _stableVfxSeed;
    private int _appliedIgnitionEpoch;
    private int _appliedVfxSeed;
    private float _lastAuthorityOnFireTimer = -1f;
    private bool _lastAuthorityExtinguished;
    private bool _awaitingPostResetExtinguishSample;
    private bool _awaitingPostResetProfileSample;
    private bool _awaitingPostResetSpreadSample;

    // Non-authority clients predict only the local player's extinguish hole. The
    // authority remains canonical and normally catches up before prediction expires.
    private bool _hasLocalExtinguishPrediction;
    private Vector3 _predictedPutOutCenterLocal;
    private float _predictedPutOutRadius;
    private float _lastLocalPredictionHitTime = float.NegativeInfinity;
    private const float LocalExtinguishPredictionHoldSeconds = 0.5f;
    private const float LocalExtinguishPredictionEpsilon = 0.005f;

    // Ownership-claim throttling. A Normcore ownership request is asynchronous,
    // so do not issue one every Update while waiting for the server response.
    private float _nextClaimAttemptTime;
    private bool _ownershipRequestPending;
    private const float OwnershipRequestRetrySeconds = 1f;

    // A remote avatar is considered ready once Normcore has instantiated it. The
    // authority schedules a short room-time lead so every client receives the reset.
    private const int MinimumPlayersToStartConfiguredFires = 2;
    private const double JoinResetLeadSeconds = 0.75;
    private int _lastAppliedResetEpoch;
    private bool _delayConfiguredStartUntilQuorum;
    private bool _configuredStartReleased;
    private bool _quorumResetPending;

    // One avatar-manager subscription fans join notifications out to all fire
    // components in this process. This avoids hundreds of identical subscriptions.
    private static readonly HashSet<NetworkedFireState> ActiveFires = new HashSet<NetworkedFireState>();
    private static RealtimeAvatarManager _watchedAvatarManager;
    private static bool _hasMultiplayerQuorum;

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

        // A scene-configured fire must not run its local Ignis Start() ignition
        // before the shared room has two players. Otherwise each client can create
        // a different autonomous fire before Normcore has selected an authority.
        FlameEngine flameEngine = FlameEngine.instance;
        _delayConfiguredStartUntilQuorum =
            _flammableObject.setThisOnFireOnStart ||
            (flameEngine != null && flameEngine.fireOnStart);
        if (_delayConfiguredStartUntilQuorum)
            NeuterLocalIgnition();

        // All Awake calls complete before Ignis starts its fire-on-start path. Give
        // that first local VFX creation the same deterministic seed on every client.
        _stableVfxSeed = CalculateStableVfxSeed();
        _flammableObject.ConfigureNetworkVfxSeed(DeriveCycleVfxSeed(1));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveFires.Clear();
        _watchedAvatarManager = null;
        _hasMultiplayerQuorum = false;
        _hazardController = null;
    }

    private int CalculateStableVfxSeed()
    {
        unchecked
        {
            uint hash = 2166136261u;
            AddStableHash(ref hash, gameObject.scene.path);

            // Names alone are not unique in these large imported scenes. Include
            // every sibling index from the scene root to this fire object.
            List<Transform> hierarchy = new List<Transform>();
            for (Transform current = transform; current != null; current = current.parent)
                hierarchy.Add(current);

            for (int i = hierarchy.Count - 1; i >= 0; i--)
            {
                Transform item = hierarchy[i];
                AddStableHash(ref hash, item.name);
                hash ^= (uint)item.GetSiblingIndex();
                hash *= 16777619u;
            }

            int seed = (int)(hash & 0x7FFFFFFFu);
            return seed == 0 ? 1 : seed;
        }
    }

    private static void AddStableHash(ref uint hash, string value)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(value))
            {
                hash *= 16777619u;
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
        }
    }

    private int DeriveCycleVfxSeed(int ignitionEpoch)
    {
        // Keep mesh-emitter placement stable across every reignition as well as
        // across clients. ignitionEpoch still restarts the same deterministic
        // sequence from time zero for each new lifecycle.
        return _stableVfxSeed == 0 ? 1 : _stableVfxSeed;
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
        _appliedIgnitionEpoch = 0;
        _appliedVfxSeed = 0;
        _lastAuthorityOnFireTimer = -1f;
        _lastAuthorityExtinguished = false;
        _awaitingPostResetExtinguishSample = false;
        _awaitingPostResetProfileSample = false;
        _awaitingPostResetSpreadSample = false;
        _configuredStartReleased = false;
        _quorumResetPending = false;
        ResetLocalExtinguishPrediction();

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
            previousModel.ignitionEpochDidChange -= OnIgnitionEpochDidChange;
            previousModel.fireSpreadDidChange -= OnFireSpreadDidChange;
            previousModel.visualMetadataEpochDidChange -= OnVisualMetadataEpochDidChange;
            previousModel.ownerIDSelfDidChange -= OnOwnerIDDidChange;
        }

        if (currentModel != null)
        {
            _configuredStartReleased = currentModel.isBurning;

            if (currentModel.isFreshModel)
            {
                // Seed the fresh model from the local Ignis state (usually all false).
                bool initiallyBurning = _flammableObject.onFire;
                int initialSeed = DeriveCycleVfxSeed(1);

                currentModel.isExtinguished = false;
                currentModel.isBurnedOut = false;
                currentModel.extinguishProgress = 0f;
                currentModel.putOutCenterLocal = Vector3.zero;
                currentModel.heatVisual = false;
                currentModel.extinguishFadeStartRoomTime = 0.0;
                currentModel.extinguishFadeDuration = 0f;
                currentModel.ignitionStartRoomTime = initiallyBurning && realtime != null && realtime.connected
                    ? realtime.roomTime - _flammableObject.onFireTimer
                    : 0.0;
                currentModel.vfxSeed = initialSeed;
                currentModel.ignitionOriginLocal = transform.InverseTransformPoint(_flammableObject.GetFireOrigin());
                currentModel.fireSpread = _flammableObject.fireSpread;
                currentModel.fireSpreadSampleRoomTime = initiallyBurning && realtime != null && realtime.connected
                    ? realtime.roomTime
                    : 0.0;
                currentModel.ignitionEpoch = initiallyBurning ? 1 : 0;
                currentModel.isBurning = initiallyBurning;
                currentModel.visualMetadataEpoch = initiallyBurning ? 1 : 0;

                if (initiallyBurning)
                {
                    _flammableObject.ApplyNetworkFireVisualState(
                        initialSeed,
                        _flammableObject.onFireTimer,
                        _flammableObject.fireSpread,
                        true);
                    _appliedIgnitionEpoch = 1;
                    _appliedVfxSeed = initialSeed;
                }

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
            currentModel.ignitionEpochDidChange += OnIgnitionEpochDidChange;
            currentModel.fireSpreadDidChange += OnFireSpreadDidChange;
            currentModel.visualMetadataEpochDidChange += OnVisualMetadataEpochDidChange;
            currentModel.ownerIDSelfDidChange += OnOwnerIDDidChange;
        }
    }

    private void Update()
    {
        if (model == null || _flammableObject == null)
            return;

        TryRegisterForJoinResets();

        TryClaimAuthority();

        TrySchedulePendingQuorumReset();

        // Keep Start Fire On Play objects dormant until the first two-player
        // quorum schedules and reaches its shared reset timestamp.
        if (_delayConfiguredStartUntilQuorum && !_configuredStartReleased)
        {
            NeuterLocalIgnition();

            if (TryApplyScheduledReset())
                return;

            if (!model.isBurning)
                return;

            _configuredStartReleased = true;
        }

        // A fresh scene fire may ignite in Start before Normcore grants the first
        // ownership request. Preserve that candidate authority state until there is
        // an actual owner; once ownership resolves, exactly one authority publishes
        // it and every other client becomes a strict puppet immediately.
        if (realtime != null && realtime.connected && isUnownedSelf)
        {
            RestoreAuthorityIgnition();
            return;
        }

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
        if (model == null || realtime == null || !realtime.connected || !model.isRoomConnected)
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
            ApplySynchronizedBurningVisual();

        EnsureAuthorityIgnitionCycle();
        EnsureAuthorityVisualMetadataBarrier();

        ApplySprayHits();
        ObserveIgnisAndWriteModel();
        ObserveFireProfileAndWriteModel();
        ApplySynchronizedExtinguishFade();
        AdvanceAuthorityVfxClock();

        _lastAuthorityOnFireTimer = _flammableObject.onFireTimer;
        _lastAuthorityExtinguished = _flammableObject.IsExtinguished();
    }

    private void EnsureAuthorityVisualMetadataBarrier()
    {
        if (!model.isBurning || model.ignitionEpoch <= 0 ||
            model.visualMetadataEpoch == model.ignitionEpoch)
        {
            return;
        }

        // Migrates an already-burning room created before the visual barrier was
        // added. Property 24 arrives before property 25 on puppets.
        model.ignitionOriginLocal = transform.InverseTransformPoint(_flammableObject.GetFireOrigin());
        model.visualMetadataEpoch = model.ignitionEpoch;
    }

    private void AdvanceAuthorityVfxClock()
    {
        if (!model.isBurning || !_flammableObject.onFire || model.ignitionEpoch <= 0)
            return;

        int seed = model.vfxSeed == 0
            ? DeriveCycleVfxSeed(model.ignitionEpoch)
            : model.vfxSeed;
        bool restartVfx = _appliedIgnitionEpoch != model.ignitionEpoch ||
                          _appliedVfxSeed != seed;

        _flammableObject.ApplyNetworkVfxSimulationState(
            seed,
            GetSynchronizedFireAge(),
            restartVfx);

        _appliedIgnitionEpoch = model.ignitionEpoch;
        _appliedVfxSeed = seed;
    }

    private void EnsureAuthorityIgnitionCycle()
    {
        if (!_flammableObject.onFire || realtime == null || !realtime.connected)
            return;

        bool missingCycle = !model.isBurning || model.ignitionEpoch <= 0;
        bool reignited =
            (_lastAuthorityExtinguished && !_flammableObject.IsExtinguished()) ||
            (_lastAuthorityOnFireTimer >= _flammableObject.burnOutStart_s &&
             _flammableObject.onFireTimer < 1f);

        if (!missingCycle && !reignited)
            return;

        int nextEpoch = Mathf.Max(1, model.ignitionEpoch + 1);
        int seed = DeriveCycleVfxSeed(nextEpoch);
        double startRoomTime = realtime.roomTime - Mathf.Max(0f, _flammableObject.onFireTimer);

        // Write one-shot metadata before the reliable epoch barrier. The burning
        // flag can arrive first due its lower property ID, so puppets also wait for
        // a non-zero epoch before constructing deterministic VFX.
        model.ignitionStartRoomTime = startRoomTime;
        model.vfxSeed = seed;
        model.ignitionOriginLocal = transform.InverseTransformPoint(_flammableObject.GetFireOrigin());
        model.fireSpread = _flammableObject.fireSpread;
        model.fireSpreadSampleRoomTime = realtime.roomTime;
        model.ignitionEpoch = nextEpoch;
        model.visualMetadataEpoch = nextEpoch;

        _flammableObject.ApplyNetworkFireVisualState(
            seed,
            _flammableObject.onFireTimer,
            _flammableObject.fireSpread,
            true);
        _appliedIgnitionEpoch = nextEpoch;
        _appliedVfxSeed = seed;
    }

    private void TryRegisterForJoinResets()
    {
        bool newlyRegistered = ActiveFires.Add(this);

        if (realtime == null)
            return;

        RealtimeAvatarManager manager = realtime.GetComponent<RealtimeAvatarManager>();
        if (manager == null)
            return;

        if (_watchedAvatarManager != manager)
        {
            if (_watchedAvatarManager != null)
                _watchedAvatarManager.avatarCreated -= OnAvatarCreated;

            _watchedAvatarManager = manager;
            _watchedAvatarManager.avatarCreated += OnAvatarCreated;
        }

        RefreshMultiplayerQuorum();

        // Fires can be enabled after both players already exist. They still need
        // the same authoritative start barrier as fires present during the join.
        if (newlyRegistered && _hasMultiplayerQuorum &&
            _delayConfiguredStartUntilQuorum && !_configuredStartReleased)
        {
            _quorumResetPending = true;
        }
    }

    private static void OnAvatarCreated(RealtimeAvatarManager manager, RealtimeAvatar avatar, bool isLocalAvatar)
    {
        if (isLocalAvatar)
            return;

        SetMultiplayerQuorum(true);
    }

    private static void RefreshMultiplayerQuorum()
    {
        if (_watchedAvatarManager == null)
        {
            SetMultiplayerQuorum(false);
            return;
        }

        int playerCount = 0;
        bool localAvatarIncluded = false;
        RealtimeAvatar localAvatar = _watchedAvatarManager.localAvatar;

        if (_watchedAvatarManager.avatars != null)
        {
            foreach (RealtimeAvatar avatar in _watchedAvatarManager.avatars.Values)
            {
                if (avatar == null)
                    continue;

                playerCount++;
                if (avatar == localAvatar)
                    localAvatarIncluded = true;
            }
        }

        if (localAvatar != null && !localAvatarIncluded)
            playerCount++;

        SetMultiplayerQuorum(playerCount >= MinimumPlayersToStartConfiguredFires);
    }

    private static void SetMultiplayerQuorum(bool hasQuorum)
    {
        if (_hasMultiplayerQuorum == hasQuorum)
            return;

        _hasMultiplayerQuorum = hasQuorum;
        if (!hasQuorum)
            return;

        // Copy before marking because a reset can indirectly disable/destroy a fire.
        NetworkedFireState[] fires = new NetworkedFireState[ActiveFires.Count];
        ActiveFires.CopyTo(fires);
        for (int i = 0; i < fires.Length; i++)
        {
            if (fires[i] != null)
                fires[i]._quorumResetPending = true;
        }
    }

    private void TrySchedulePendingQuorumReset()
    {
        if (!_quorumResetPending || !_hasMultiplayerQuorum)
            return;

        if (ScheduleQuorumReset())
            _quorumResetPending = false;
    }

    private bool ScheduleQuorumReset()
    {
        if (model == null || realtime == null || !realtime.connected || !IsAuthority)
            return false;

        bool shouldStartConfiguredFire =
            _delayConfiguredStartUntilQuorum && !_configuredStartReleased;

        // Dormant and already-finished fires remain dormant. A partially
        // extinguished but still-burning fire restarts from full strength. The
        // exception is a configured start fire deliberately held dormant for the
        // first two-player quorum: that fire starts now.
        if (!shouldStartConfiguredFire &&
            (!model.isBurning || model.isExtinguished || model.isBurnedOut))
        {
            return true;
        }

        double resetAt = realtime.roomTime + JoinResetLeadSeconds;
        if (model.resetEpoch > model.resetCompletedEpoch && model.resetAtRoomTime > realtime.roomTime)
        {
            // Coalesce teammates who arrive together into the same reset.
            model.resetAtRoomTime = resetAt;
            model.resetShouldBurn = true;
            return true;
        }

        model.resetAtRoomTime = resetAt;
        model.resetShouldBurn = true;
        model.resetEpoch = model.resetEpoch + 1;
        return true;
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
        int nextIgnitionEpoch = Mathf.Max(1, model.ignitionEpoch + 1);
        int resetVfxSeed = DeriveCycleVfxSeed(nextIgnitionEpoch);

        _flammableObject.ResetObj();
        _flammableObject.ResetMaterialFromIgnis();
        if (_fireProfileController != null)
            _fireProfileController.ResetForNetworkRestart();
        if (shouldBurn)
        {
            _flammableObject.ConfigureNetworkVfxSeed(resetVfxSeed);
            _flammableObject.SetOnFireFromCenterFromNetwork();
            _flammableObject.ApplyNetworkFireVisualState(resetVfxSeed, 0f, 0f, true);
            _appliedIgnitionEpoch = nextIgnitionEpoch;
            _appliedVfxSeed = resetVfxSeed;
        }

        _lastAppliedResetEpoch = resetEpoch;
        if (shouldBurn && _delayConfiguredStartUntilQuorum)
            _configuredStartReleased = true;
        _awaitingPostResetExtinguishSample = !IsAuthority;
        _awaitingPostResetProfileSample = !IsAuthority;
        _awaitingPostResetSpreadSample = !IsAuthority;

        if (IsAuthority)
        {
            model.ignitionStartRoomTime = model.resetAtRoomTime;
            model.vfxSeed = resetVfxSeed;
            model.ignitionOriginLocal = Vector3.zero;
            model.fireSpread = 0f;
            model.fireSpreadSampleRoomTime = realtime.roomTime;
            model.ignitionEpoch = nextIgnitionEpoch;
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

            // Written after all cycle metadata and state above. Property ID 25 is
            // the visual construction barrier consumed by puppets.
            model.visualMetadataEpoch = nextIgnitionEpoch;

            // Written last: reliable property ordering makes this the barrier that
            // tells puppets all post-reset state above is ready to consume.
            model.resetCompletedEpoch = resetEpoch;

            _lastAuthorityOnFireTimer = 0f;
            _lastAuthorityExtinguished = false;
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
            if (sprayer == null ||
                !sprayer.IsSpraying ||
                !sprayer.CanExtinguish(_fireProfileController))
                continue;

            Transform nozzle = sprayer.NozzleTransform;
            if (nozzle == null)
                continue;

            if (!Physics.SphereCast(nozzle.position, sprayHitRadius, nozzle.forward, out RaycastHit hit, sprayer.MaxSprayDistance, sprayHitMask, QueryTriggerInteraction.Collide))
                continue;

            if (hit.collider.GetComponentInParent<FlammableObject>() != _flammableObject)
                continue;

            float profilePower = _fireProfileController != null
                ? _fireProfileController.GetCurrentExtinguishPowerMultiplier()
                : 1f;
            _flammableObject.IncrementalExtinguish(
                hit.point,
                sprayer.SprayStartRadius * profilePower,
                sprayer.SprayRadiusIncrementSpeed * profilePower * Time.deltaTime);
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

        if (burning && !extinguished && Time.unscaledTime >= _nextFireSpreadSyncTime)
        {
            float spread = Mathf.Clamp(_flammableObject.fireSpread, 0f, _flammableObject.maxSpread);
            if (Mathf.Abs(spread - model.fireSpread) >= FireSpreadSyncEpsilon ||
                model.fireSpreadSampleRoomTime <= 0.0)
            {
                model.fireSpread = spread;
                if (realtime != null && realtime.connected)
                    model.fireSpreadSampleRoomTime = realtime.roomTime;
            }

            _nextFireSpreadSyncTime = Time.unscaledTime + FireSpreadSyncInterval;
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
            ApplySynchronizedBurningVisual();
            ApplyExactExtinguishState();
            ApplyLocalExtinguishPrediction();
        }
        else
        {
            // Reliable authority flags are canonical. Remove a phantom flame
            // immediately and repair terminal flags even if local Ignis ran ahead.
            _flammableObject.ApplyNetworkInactiveState(
                model.isExtinguished,
                model.isBurnedOut);
        }

        ApplySynchronizedExtinguishFade();

        if (model.isBurning &&
            _flammableObject.onFire &&
            (model.isExtinguished || model.isBurnedOut))
        {
            _flammableObject.RefreshNetworkFireVisualState();

            int seed = model.vfxSeed == 0
                ? DeriveCycleVfxSeed(model.ignitionEpoch)
                : model.vfxSeed;
            _flammableObject.ApplyNetworkVfxSimulationState(
                seed,
                GetSynchronizedFireAge(),
                false);
        }
    }

    /// <summary>
    /// Prevents this puppet's local Ignis from making any lifecycle decisions.
    /// Explicit network adapter calls still create and update its visuals.
    /// </summary>
    private void NeuterLocalIgnition()
    {
        if (_ignitionNeutered)
            return;

        _flammableObject.SetLocalLifecycleSimulationEnabled(false);
        _ignitionNeutered = true;
    }

    private void RestoreAuthorityIgnition()
    {
        if (!_ignitionNeutered)
            return;

        _flammableObject.SetLocalLifecycleSimulationEnabled(true);
        _ignitionNeutered = false;
    }

    private void ApplyBurningVisual()
    {
        if (!_flammableObject.onFire)
            _flammableObject.SetOnFireFromCenterFromNetwork();
    }

    private void ApplySynchronizedBurningVisual()
    {
        if (model == null || !model.isBurning)
            return;

        // Backwards-compatible fallback for a datastore created by an older build.
        if (model.ignitionEpoch <= 0 || model.ignitionStartRoomTime <= 0.0)
        {
            ApplyBurningVisual();
            return;
        }

        // Property 25 is written after origin and all other visual metadata. A zero
        // value is accepted only for backward compatibility with an older room.
        if (model.visualMetadataEpoch > 0 &&
            model.visualMetadataEpoch != model.ignitionEpoch)
        {
            return;
        }

        int seed = model.vfxSeed == 0
            ? DeriveCycleVfxSeed(model.ignitionEpoch)
            : model.vfxSeed;
        bool restartVfx =
            _appliedIgnitionEpoch != model.ignitionEpoch ||
            _appliedVfxSeed != seed;

        _flammableObject.ApplyNetworkFireOriginLocal(model.ignitionOriginLocal);

        if (!_flammableObject.onFire)
        {
            _flammableObject.ConfigureNetworkVfxSeed(seed);
            _flammableObject.SetOnFireFromCenterFromNetwork();
            _flammableObject.ApplyNetworkFireOriginLocal(model.ignitionOriginLocal);
            restartVfx = true;
        }

        if (!_flammableObject.onFire)
            return;

        float fireAge = GetSynchronizedFireAge();
        float spread = GetPredictedAuthoritySpread(fireAge);

        // The extinguish fade has its own synchronized room-time clock and takes
        // ownership of onFireTimer once the reliable completion flag arrives.
        if (!model.isExtinguished && !model.isBurnedOut)
            _flammableObject.ApplyNetworkFireVisualState(seed, fireAge, spread, restartVfx);

        _appliedIgnitionEpoch = model.ignitionEpoch;
        _appliedVfxSeed = seed;
    }

    private float GetSynchronizedFireAge()
    {
        if (realtime == null || !realtime.connected || model.ignitionStartRoomTime <= 0.0)
            return Mathf.Max(0f, _flammableObject.onFireTimer);

        // Ignis deliberately freezes both its timer and spread when crawl speed is
        // zero, so retain that behavior instead of advancing it from room time.
        if (_flammableObject.fireCrawlSpeed <= 0.00001f)
            return 0f;

        float age = Mathf.Max(0f, (float)(realtime.roomTime - model.ignitionStartRoomTime));

        // A puppet may render the shared age but must not make the reliable burnout
        // decision a frame before the authority. Hold just below the threshold
        // until the canonical burned-out flag arrives.
        if (!IsAuthority && !model.isBurnedOut && !model.isExtinguished)
        {
            float burnoutThreshold = _flammableObject.burnOutStart_s + _flammableObject.burnOutLength_s;
            age = Mathf.Min(age, Mathf.Max(0f, burnoutThreshold - 0.01f));
        }

        return age;
    }

    private float GetPredictedAuthoritySpread(float fireAge)
    {
        if (_awaitingPostResetSpreadSample)
            return 0f;

        float spread = Mathf.Clamp(model.fireSpread, 0f, _flammableObject.maxSpread);
        if (realtime == null ||
            !realtime.connected ||
            model.fireSpreadSampleRoomTime <= 0.0 ||
            _flammableObject.fireCrawlSpeed <= 0.00001f ||
            spread >= _flammableObject.maxSpread)
        {
            return spread;
        }

        float elapsedSinceSample = Mathf.Max(
            0f,
            (float)(realtime.roomTime - model.fireSpreadSampleRoomTime));
        // Simpson integration follows Ignis' smoothly changing Perlin crawl rate
        // much more closely than extending the newest instantaneous rate across
        // the whole 200 ms sample window. This costs two extra Perlin samples but
        // requires no additional model traffic.
        float sampleAge = Mathf.Max(0f, fireAge - elapsedSinceSample);
        float midAge = sampleAge + elapsedSinceSample * 0.5f;
        float startRate = 0.95f + Mathf.PerlinNoise(sampleAge, 0f) * 0.1f;
        float midRate = 0.95f + Mathf.PerlinNoise(midAge, 0f) * 0.1f;
        float endRate = 0.95f + Mathf.PerlinNoise(fireAge, 0f) * 0.1f;
        float predictedGrowth = _flammableObject.fireCrawlSpeed * elapsedSinceSample *
            (startRate + 4f * midRate + endRate) / 6f;

        return Mathf.Clamp(
            spread + predictedGrowth,
            0f,
            _flammableObject.maxSpread);
    }

    /// <summary>
    /// Gives the local player immediate visual feedback while spraying a fire owned
    /// by another client. This never writes the fire model or makes terminal state
    /// decisions; authoritative progress replaces it as soon as it catches up.
    /// </summary>
    private void ApplyLocalExtinguishPrediction()
    {
        float threshold = GetExtinguishThreshold();
        float authoritativeRadius = model.extinguishProgress * Mathf.Max(0f, threshold);
        bool hitThisFrame = false;

        for (int i = 0; i < SpraySync.All.Count; i++)
        {
            SpraySync sprayer = SpraySync.All[i];
            if (sprayer == null ||
                !sprayer.IsLocallyControlledAndSpraying ||
                !sprayer.CanExtinguish(_fireProfileController))
                continue;

            Transform nozzle = sprayer.NozzleTransform;
            if (nozzle == null ||
                !Physics.SphereCast(nozzle.position, sprayHitRadius, nozzle.forward, out RaycastHit hit,
                    sprayer.MaxSprayDistance, sprayHitMask, QueryTriggerInteraction.Collide) ||
                hit.collider.GetComponentInParent<FlammableObject>() != _flammableObject)
            {
                continue;
            }

            if (!_hasLocalExtinguishPrediction)
            {
                _hasLocalExtinguishPrediction = true;
                _predictedPutOutRadius = authoritativeRadius;
                _predictedPutOutCenterLocal = model.putOutCenterLocal;
            }

            float profilePower = GetLocalPredictionProfilePower();
            AdvanceLocalExtinguishPrediction(
                transform.InverseTransformPoint(hit.point),
                sprayer.SprayStartRadius * profilePower,
                sprayer.SprayRadiusIncrementSpeed * profilePower * Time.deltaTime);
            hitThisFrame = true;
            _lastLocalPredictionHitTime = Time.unscaledTime;
        }

        if (!_hasLocalExtinguishPrediction)
            return;

        if (authoritativeRadius + LocalExtinguishPredictionEpsilon >= _predictedPutOutRadius)
        {
            ResetLocalExtinguishPrediction();
            return;
        }

        if (!hitThisFrame &&
            Time.unscaledTime - _lastLocalPredictionHitTime > LocalExtinguishPredictionHoldSeconds)
        {
            ResetLocalExtinguishPrediction();
            return;
        }

        _flammableObject.ApplyNetworkExtinguishState(
            transform.TransformPoint(_predictedPutOutCenterLocal),
            _predictedPutOutRadius,
            false);
        _flammableObject.RefreshNetworkFireVisualState();
    }

    private float GetLocalPredictionProfilePower()
    {
        return _fireProfileController != null
            ? _fireProfileController.GetCurrentExtinguishPowerMultiplier()
            : 1f;
    }

    private void AdvanceLocalExtinguishPrediction(Vector3 hitLocal, float startRadius, float increment)
    {
        if (_predictedPutOutRadius < 0.05f ||
            Vector3.Distance(_predictedPutOutCenterLocal, hitLocal) - _predictedPutOutRadius > startRadius * 3f)
        {
            _predictedPutOutCenterLocal = hitLocal;
            _predictedPutOutRadius = startRadius;
            return;
        }

        float distance = Vector3.Distance(hitLocal, _predictedPutOutCenterLocal);
        if (distance > _predictedPutOutRadius - startRadius)
        {
            Vector3 previousCenter = _predictedPutOutCenterLocal;
            Vector3 target = Vector3.MoveTowards(
                _predictedPutOutCenterLocal,
                hitLocal,
                distance + startRadius - _predictedPutOutRadius);
            _predictedPutOutCenterLocal = Vector3.Lerp(_predictedPutOutCenterLocal, target, 0.2f);
            _predictedPutOutRadius += Vector3.Distance(previousCenter, _predictedPutOutCenterLocal);
        }
        else
        {
            _predictedPutOutRadius += Mathf.Max(0f, increment);
        }
    }

    private void ResetLocalExtinguishPrediction()
    {
        _hasLocalExtinguishPrediction = false;
        _predictedPutOutCenterLocal = Vector3.zero;
        _predictedPutOutRadius = 0f;
        _lastLocalPredictionHitTime = float.NegativeInfinity;
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
        Vector3 centerWorld = _awaitingPostResetExtinguishSample
            ? transform.position
            : transform.TransformPoint(model.putOutCenterLocal);
        float targetRadius = _awaitingPostResetExtinguishSample
            ? 0f
            : model.extinguishProgress * Mathf.Max(0f, threshold);
        _flammableObject.ApplyNetworkExtinguishState(centerWorld, targetRadius, model.isExtinguished);
    }

    /// <summary>
    /// Forces the local Ignis visual into the extinguished/burned-out fade, using the
    /// same public onFireTimer mechanism Ignis uses internally when a fire goes out.
    /// </summary>
    private void ApplyExtinguishedVisual(bool skipToBurnedOut)
    {
        if (!_flammableObject.onFire && model != null && model.isBurning)
            ApplySynchronizedBurningVisual();

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
            ApplySynchronizedBurningVisual();
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
        {
            _awaitingPostResetExtinguishSample = false;
            ApplyExactExtinguishState();
        }
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
        _awaitingPostResetProfileSample = false;

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

    private void OnIgnitionEpochDidChange(FireStateModel changedModel, int value)
    {
        if (!IsAuthority && changedModel.isBurning)
            ApplySynchronizedBurningVisual();
    }

    private void OnFireSpreadDidChange(FireStateModel changedModel, float value)
    {
        _awaitingPostResetSpreadSample = false;

        if (!IsAuthority && changedModel.isBurning)
            ApplySynchronizedBurningVisual();
    }

    private void OnVisualMetadataEpochDidChange(FireStateModel changedModel, int value)
    {
        if (!IsAuthority && changedModel.isBurning && value == changedModel.ignitionEpoch)
            ApplySynchronizedBurningVisual();
    }

    private void ApplyProfileStateFromModel()
    {
        if (model == null || _fireProfileController == null || _awaitingPostResetProfileSample)
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
            ResetLocalExtinguishPrediction();
            // We just became the authority (initial claim or failover).
            ApplyProfileStateFromModel();
            RestoreAuthorityIgnition();

            if (model != null && model.isBurning && !_flammableObject.onFire && !_flammableObject.hasBurnedOut())
                ApplySynchronizedBurningVisual();
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
