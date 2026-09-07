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

    [Header("Shared Fire Presentation")]
    [Tooltip("Shared room-time delay used to buffer authoritative visual snapshots.")]
    [SerializeField, Range(0.05f, 0.5f)] private float presentationDelaySeconds = 0.15f;

    [Tooltip("Local hazard deformation prediction intentionally allows temporary disagreement. Keep disabled for synchronized training.")]
    [SerializeField] private bool enableLocalExtinguishPrediction = false;

    [Tooltip("Emit one aggregate synchronization line periodically for two-client comparison.")]
    [SerializeField] private bool enableSynchronizationDiagnostics = true;

    private FlammableObject _flammableObject;
    private FireProfileController _fireProfileController;

    // Fire-profile temperature can change every frame while regenerating. Publish
    // it at a bounded rate rather than producing one model write per rendered frame.
    private const float ProfileTemperatureSyncInterval = 0.1f;
    private const float ProfileTemperatureSyncEpsilon = 0.25f;
    private const float ExtinguishStateSyncInterval = 0.05f;
    private const float FireSpreadSyncInterval = 0.2f;
    private const float FireSpreadSyncEpsilon = 0.002f;
    private const float PresentationSnapshotInterval = 0.1f;
    private const float PresentationKeepAliveInterval = 0.5f;
    private const int PresentationBufferCapacity = 32;
    private float _nextProfileTemperatureSyncTime;
    private float _nextExtinguishStateSyncTime;
    private float _nextFireSpreadSyncTime;
    private float _nextPresentationSnapshotTime;
    private float _lastPresentationSnapshotLocalTime = float.NegativeInfinity;
    private float _lastPublishedWaterHitLocalTime = float.NegativeInfinity;

    private struct FirePresentationSnapshot
    {
        public double roomTime;
        public float fireSpread;
        public float extinguishProgress;
        public Vector3 putOutCenterLocal;
        public float temperature;
        public int lifecycleFlags;
        public int ignitionEpoch;
        public Vector3 ignitionOriginLocal;
        public double ignitionStartRoomTime;
        public double extinguishFadeStartRoomTime;
        public float extinguishFadeDuration;

        public bool IsBurning => (lifecycleFlags & 1) != 0;
        public bool IsExtinguished => (lifecycleFlags & 2) != 0;
        public bool IsBurnedOut => (lifecycleFlags & 4) != 0;
    }

    private readonly List<FirePresentationSnapshot> _presentationBuffer =
        new List<FirePresentationSnapshot>(PresentationBufferCapacity);
    private FirePresentationSnapshot _lastPublishedPresentationSnapshot;
    private FirePresentationSnapshot _presentedSnapshot;
    private double _lastReceivedPresentationSnapshotRoomTime;
    private int _lastReceivedPresentationTransitionEpoch;
    private int _latePresentationFrameCount;
    private int _lastLatePresentationUnityFrame = -1;
    private int _lastAppliedPresentedLifecycleFlags = -1;
    private bool _hasPublishedPresentationSnapshot;
    private bool _hasPresentedSnapshot;

    // Local connection quality diagnostics. A shared adaptive delay needs reports
    // from every client; until that coordinator exists these values are diagnostic.
    private float _smoothedRttMs;
    private float _smoothedRttVariationMs;
    private float _receiveJitterMs;
    private float _lastRttSampleMs = -1f;
    private static float _nextGlobalDiagnosticsTime;

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
    private bool _waitingForIgnisReset;

    // One avatar-manager subscription fans join notifications out to all fire
    // components in this process. This avoids hundreds of identical subscriptions.
    private static readonly HashSet<NetworkedFireState> ActiveFires = new HashSet<NetworkedFireState>();
    private static RealtimeAvatarManager _watchedAvatarManager;
    private static bool _hasMultiplayerQuorum;
    private static int _lastObservedPlayerCount = -1;

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
        _lastObservedPlayerCount = -1;
        _hazardController = null;
        _nextGlobalDiagnosticsTime = 0f;
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
        _waitingForIgnisReset = false;
        _presentationBuffer.Clear();
        _lastReceivedPresentationSnapshotRoomTime = 0.0;
        _lastReceivedPresentationTransitionEpoch = 0;
        _latePresentationFrameCount = 0;
        _lastLatePresentationUnityFrame = -1;
        _hasPublishedPresentationSnapshot = false;
        _hasPresentedSnapshot = false;
        _lastAppliedPresentedLifecycleFlags = -1;
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
            previousModel.presentationSnapshotRoomTimeDidChange -= OnPresentationSnapshotRoomTimeDidChange;
            previousModel.presentationTransitionEpochDidChange -= OnPresentationTransitionEpochDidChange;
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
                currentModel.presentationDelaySeconds = Mathf.Clamp(presentationDelaySeconds, 0.05f, 0.5f);
                currentModel.presentationTransitionRoomTime = currentModel.ignitionStartRoomTime;
                currentModel.presentationTransitionEpoch = initiallyBurning ? 1 : 0;

                FirePresentationSnapshot initialPresentation = CreateCurrentPresentationSnapshot(
                    realtime != null && realtime.connected
                        ? realtime.roomTime
                        : 0.0);
                WritePresentationSnapshotToModel(initialPresentation);

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

                if (currentModel.presentationDelaySeconds <= 0f && IsAuthority)
                    currentModel.presentationDelaySeconds = Mathf.Clamp(presentationDelaySeconds, 0.05f, 0.5f);
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
            currentModel.presentationSnapshotRoomTimeDidChange += OnPresentationSnapshotRoomTimeDidChange;
            currentModel.presentationTransitionEpochDidChange += OnPresentationTransitionEpochDidChange;
            currentModel.ownerIDSelfDidChange += OnOwnerIDDidChange;

            CapturePresentationSnapshotFromModel();
            OnPresentationTransitionEpochDidChange(
                currentModel,
                currentModel.presentationTransitionEpoch);
        }
    }

    private void Update()
    {
        if (model == null || _flammableObject == null)
            return;

        UpdateConnectionDiagnostics();
        CapturePresentationSnapshotFromModel();
        TryLogSynchronizationDiagnostics();

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

    private void LateUpdate()
    {
        // The normal Ignis/Profile updates write their local visual values first.
        // Apply only the buffered semantic presentation values afterward, without
        // rewinding the authority's actual simulation state.
        if (model == null || _flammableObject == null || realtime == null || !realtime.connected)
            return;

        if (!TryGetPresentedSnapshot(out FirePresentationSnapshot presentation))
            return;

        _presentedSnapshot = presentation;
        _hasPresentedSnapshot = true;
        ApplyPresentedLifecycleSideEffects(presentation);

        float currentScale = _fireProfileController != null
            ? _fireProfileController.GetVisualScaleForTemperature(_fireProfileController.currentTemperature)
            : 1f;
        float presentedScale = _fireProfileController != null
            ? _fireProfileController.GetVisualScaleForTemperature(presentation.temperature)
            : 1f;
        float scaleCorrection = currentScale > 0.0001f ? presentedScale / currentScale : 1f;
        float burnoutMultiplier = GetPresentedBurnoutMultiplier(presentation);

        _flammableObject.ApplyNetworkPresentationVisualState(
            presentation.IsBurning && !presentation.IsBurnedOut,
            presentation.ignitionOriginLocal,
            presentation.fireSpread,
            presentation.putOutCenterLocal,
            presentation.extinguishProgress * Mathf.Max(0f, GetExtinguishThreshold()),
            scaleCorrection,
            burnoutMultiplier);
    }

    private void UpdateConnectionDiagnostics()
    {
        if (realtime == null || !realtime.connected || realtime.ping < 0)
            return;

        float sampleMs = realtime.ping;
        if (_lastRttSampleMs >= 0f && Mathf.Approximately(sampleMs, _lastRttSampleMs))
            return;

        if (_lastRttSampleMs < 0f)
        {
            _smoothedRttMs = sampleMs;
            _smoothedRttVariationMs = 0f;
        }
        else
        {
            float delta = Mathf.Abs(sampleMs - _smoothedRttMs);
            _smoothedRttMs = Mathf.Lerp(_smoothedRttMs, sampleMs, 0.1f);
            _smoothedRttVariationMs = Mathf.Lerp(_smoothedRttVariationMs, delta, 0.1f);
        }

        _lastRttSampleMs = sampleMs;

        ConnectionStatistics? statistics = realtime.room?.GetConnectionStatistics(ChannelFlags.All);
        if (statistics.HasValue)
            _receiveJitterMs = statistics.Value.channel.receiveJitter;
    }

    private void TryLogSynchronizationDiagnostics()
    {
        if (!enableSynchronizationDiagnostics || realtime == null || !realtime.connected ||
            Time.unscaledTime < _nextGlobalDiagnosticsTime)
        {
            return;
        }

        int fireCount = 0;
        int minBufferDepth = int.MaxValue;
        int maxBufferDepth = 0;
        int lateFrames = 0;
        double oldestLatestSnapshotAge = 0.0;
        NetworkedFireState representative = null;

        foreach (NetworkedFireState fire in ActiveFires)
        {
            if (fire == null || fire.model == null)
                continue;

            fireCount++;
            int depth = fire._presentationBuffer.Count;
            minBufferDepth = Mathf.Min(minBufferDepth, depth);
            maxBufferDepth = Mathf.Max(maxBufferDepth, depth);
            lateFrames += fire._latePresentationFrameCount;
            if (depth > 0)
            {
                oldestLatestSnapshotAge = System.Math.Max(
                    oldestLatestSnapshotAge,
                    realtime.roomTime - fire._presentationBuffer[depth - 1].roomTime);
            }

            if (representative == null &&
                (fire.model.isBurning || fire._hasPresentedSnapshot && fire._presentedSnapshot.IsBurning))
            {
                representative = fire;
            }
        }

        if (fireCount == 0)
            return;

        _nextGlobalDiagnosticsTime = Time.unscaledTime + 2f;

        float recommendedDelay = Mathf.Clamp(
            (_smoothedRttMs * 0.5f + Mathf.Max(_smoothedRttVariationMs, _receiveJitterMs) * 2f + 30f) / 1000f,
            0.1f,
            0.5f);
        string representativeState = representative != null
            ? $" sample={representative.name} canonical(spread={representative.model.fireSpread:F3},ext={representative.model.extinguishProgress:F3},flags={GetLifecycleFlags(representative.model.isBurning, representative.model.isExtinguished, representative.model.isBurnedOut)}) presented(spread={(representative._hasPresentedSnapshot ? representative._presentedSnapshot.fireSpread : -1f):F3},ext={(representative._hasPresentedSnapshot ? representative._presentedSnapshot.extinguishProgress : -1f):F3},flags={(representative._hasPresentedSnapshot ? representative._presentedSnapshot.lifecycleFlags : -1)}) vfxTick={representative._flammableObject.GetNetworkVfxLogicalTick()} emitters={representative._flammableObject.GetNetworkVfxEmitterCount()}"
            : string.Empty;

        Debug.Log(
            $"[FireSync] client={realtime.clientID} room={realtime.roomTime:F3} presentation={realtime.roomTime - GetSharedPresentationDelay():F3} delay={GetSharedPresentationDelay() * 1000f:F0}ms RTT={_smoothedRttMs:F0}ms variation={_smoothedRttVariationMs:F1}ms receiveJitter={_receiveJitterMs:F1}ms recommended={recommendedDelay * 1000f:F0}ms fires={fireCount} buffer={minBufferDepth}-{maxBufferDepth} latestAgeMax={oldestLatestSnapshotAge * 1000.0:F0}ms lateFrames={lateFrames}{representativeState}",
            this);
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
        PublishPresentationSnapshot(false);
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

        bool hasPresentation = TryGetPresentedSnapshot(out FirePresentationSnapshot presentation);
        if (hasPresentation && !presentation.IsBurning)
            return;

        int presentedEpoch = hasPresentation
            ? Mathf.Max(1, presentation.ignitionEpoch)
            : model.ignitionEpoch;
        int seed = model.vfxSeed == 0
            ? DeriveCycleVfxSeed(presentedEpoch)
            : model.vfxSeed;
        bool restartVfx = _appliedIgnitionEpoch != presentedEpoch ||
                          _appliedVfxSeed != seed;

        _flammableObject.ApplyNetworkVfxSimulationState(
            seed,
            hasPresentation ? GetSynchronizedFireAge(presentation) : GetSynchronizedFireAge(),
            restartVfx);

        _appliedIgnitionEpoch = presentedEpoch;
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

        if (playerCount != _lastObservedPlayerCount)
        {
            _lastObservedPlayerCount = playerCount;
            Debug.Log($"[NetworkedFireState] Multiplayer fire quorum: {playerCount}/{MinimumPlayersToStartConfiguredFires} player avatars ready.");
        }

        SetMultiplayerQuorum(playerCount >= MinimumPlayersToStartConfiguredFires);
    }

    private static void SetMultiplayerQuorum(bool hasQuorum)
    {
        if (_hasMultiplayerQuorum == hasQuorum)
            return;

        _hasMultiplayerQuorum = hasQuorum;
        if (!hasQuorum)
            return;

        Debug.Log("[NetworkedFireState] Multiplayer fire quorum reached; scheduling synchronized configured-fire start.");

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

        if (shouldStartConfiguredFire)
        {
            Debug.Log($"[NetworkedFireState] Authority scheduled configured fire '{name}' for room time {resetAt:F3}.", this);
        }

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

            // Ignis can legitimately defer ignition for a frame while its
            // FlameEngine singleton is initializing. Do not acknowledge the reset
            // until a flame was actually created; leaving the epoch unapplied makes
            // this method retry naturally on the next Update.
            if (!_flammableObject.onFire)
            {
                if (!_waitingForIgnisReset)
                {
                    Debug.LogWarning(
                        $"[NetworkedFireState] Ignis was not ready to start '{name}' for reset epoch {resetEpoch}; retrying.",
                        this);
                    _waitingForIgnisReset = true;
                }

                return true;
            }

            _waitingForIgnisReset = false;
            _flammableObject.ApplyNetworkFireVisualState(resetVfxSeed, 0f, 0f, true);
            _appliedIgnitionEpoch = nextIgnitionEpoch;
            _appliedVfxSeed = resetVfxSeed;
        }

        _lastAppliedResetEpoch = resetEpoch;
        if (shouldBurn && _delayConfiguredStartUntilQuorum)
        {
            _configuredStartReleased = true;
            Debug.Log($"[NetworkedFireState] Applied synchronized configured-fire start for '{name}' (reset epoch {resetEpoch}).", this);
        }
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

            // Reset/start is already scheduled in room time. Record that same
            // instant as a presentation transition so all clients reveal the new
            // cycle after the common presentation delay, including burning-to-
            // burning resets where lifecycle flags alone do not change.
            model.presentationTransitionRoomTime = model.resetAtRoomTime;
            model.presentationTransitionEpoch = model.presentationTransitionEpoch + 1;
            PublishPresentationSnapshot(true);

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
        int previousLifecycleFlags = GetLifecycleFlags(
            model.isBurning,
            model.isExtinguished,
            model.isBurnedOut);
        int nextLifecycleFlags = GetLifecycleFlags(burning, extinguished, burnedOut);
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

        if (nextLifecycleFlags != previousLifecycleFlags && realtime != null && realtime.connected)
        {
            // Reliable epoch is written after the canonical flags and transition
            // time, making it a discrete presentation barrier on remote clients.
            model.presentationTransitionRoomTime = realtime.roomTime;
            model.presentationTransitionEpoch = model.presentationTransitionEpoch + 1;
            _nextPresentationSnapshotTime = 0f;
        }

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

    private static int GetLifecycleFlags(bool burning, bool extinguished, bool burnedOut)
    {
        return (burning ? 1 : 0) |
               (extinguished ? 2 : 0) |
               (burnedOut ? 4 : 0);
    }

    private FirePresentationSnapshot CreateCurrentPresentationSnapshot(double roomTime)
    {
        return new FirePresentationSnapshot
        {
            roomTime = roomTime,
            fireSpread = Mathf.Clamp(_flammableObject.fireSpread, 0f, _flammableObject.maxSpread),
            extinguishProgress = GetExtinguishThreshold() > 0f
                ? Mathf.Clamp01(_flammableObject.GetPutOutRadius() / GetExtinguishThreshold())
                : 0f,
            putOutCenterLocal = transform.InverseTransformPoint(_flammableObject.GetPutOutCenter()),
            temperature = _fireProfileController != null
                ? Mathf.Max(0f, _fireProfileController.currentTemperature)
                : 0f,
            lifecycleFlags = GetLifecycleFlags(
                _flammableObject.onFire,
                _flammableObject.IsExtinguished(),
                _flammableObject.hasBurnedOut()),
            ignitionEpoch = model != null ? model.ignitionEpoch : 0,
            ignitionOriginLocal = transform.InverseTransformPoint(_flammableObject.GetFireOrigin()),
            ignitionStartRoomTime = model != null ? model.ignitionStartRoomTime : 0.0,
            extinguishFadeStartRoomTime = model != null ? model.extinguishFadeStartRoomTime : 0.0,
            extinguishFadeDuration = model != null ? model.extinguishFadeDuration : 0f
        };
    }

    private FirePresentationSnapshot CreatePresentationSnapshotFromModel(double roomTime)
    {
        return new FirePresentationSnapshot
        {
            roomTime = roomTime,
            fireSpread = Mathf.Clamp(model.presentationFireSpread, 0f, _flammableObject.maxSpread),
            extinguishProgress = Mathf.Clamp01(model.presentationExtinguishProgress),
            putOutCenterLocal = model.presentationPutOutCenterLocal,
            temperature = Mathf.Max(0f, model.presentationTemperature),
            lifecycleFlags = model.presentationLifecycleFlags,
            ignitionEpoch = model.presentationIgnitionEpoch,
            ignitionOriginLocal = model.presentationIgnitionOriginLocal,
            ignitionStartRoomTime = model.presentationIgnitionStartRoomTime,
            extinguishFadeStartRoomTime = model.presentationExtinguishFadeStartRoomTime,
            extinguishFadeDuration = model.presentationExtinguishFadeDuration
        };
    }

    private void WritePresentationSnapshotToModel(FirePresentationSnapshot snapshot)
    {
        if (model == null)
            return;

        model.presentationFireSpread = snapshot.fireSpread;
        model.presentationExtinguishProgress = snapshot.extinguishProgress;
        model.presentationPutOutCenterLocal = snapshot.putOutCenterLocal;
        model.presentationTemperature = snapshot.temperature;
        model.presentationLifecycleFlags = snapshot.lifecycleFlags;
        model.presentationIgnitionEpoch = snapshot.ignitionEpoch;
        model.presentationIgnitionOriginLocal = snapshot.ignitionOriginLocal;
        model.presentationIgnitionStartRoomTime = snapshot.ignitionStartRoomTime;
        model.presentationExtinguishFadeStartRoomTime = snapshot.extinguishFadeStartRoomTime;
        model.presentationExtinguishFadeDuration = snapshot.extinguishFadeDuration;

        // Highest property ID and written last: consumers treat this as the
        // coherent-sample barrier for the latest-value fields above.
        model.presentationSnapshotRoomTime = snapshot.roomTime;
    }

    private void PublishPresentationSnapshot(bool force)
    {
        if (model == null || realtime == null || !realtime.connected || !IsAuthority)
            return;

        float sharedDelay = Mathf.Clamp(
            model.presentationDelaySeconds > 0f ? model.presentationDelaySeconds : presentationDelaySeconds,
            0.05f,
            0.5f);
        if (model.presentationDelaySeconds <= 0f)
            model.presentationDelaySeconds = sharedDelay;

        double now = realtime.roomTime;
        FirePresentationSnapshot snapshot = CreateCurrentPresentationSnapshot(now);
        bool changed = !_hasPublishedPresentationSnapshot ||
                       snapshot.lifecycleFlags != _lastPublishedPresentationSnapshot.lifecycleFlags ||
                       snapshot.ignitionEpoch != _lastPublishedPresentationSnapshot.ignitionEpoch ||
                       Mathf.Abs(snapshot.fireSpread - _lastPublishedPresentationSnapshot.fireSpread) >= 0.002f ||
                       Mathf.Abs(snapshot.extinguishProgress - _lastPublishedPresentationSnapshot.extinguishProgress) >= 0.002f ||
                       (snapshot.putOutCenterLocal - _lastPublishedPresentationSnapshot.putOutCenterLocal).sqrMagnitude >= 0.0001f ||
                       Mathf.Abs(snapshot.temperature - _lastPublishedPresentationSnapshot.temperature) >= 0.25f;
        bool activeOrRecentlyActive = snapshot.IsBurning ||
                                      (_hasPublishedPresentationSnapshot &&
                                       _lastPublishedPresentationSnapshot.IsBurning);
        bool keepAliveDue = activeOrRecentlyActive &&
                            Time.unscaledTime - _lastPresentationSnapshotLocalTime >=
                            PresentationKeepAliveInterval;

        if (!force && Time.unscaledTime < _nextPresentationSnapshotTime && !keepAliveDue)
            return;
        if (!force && !changed && !keepAliveDue)
            return;

        AddPresentationSnapshot(snapshot);
        WritePresentationSnapshotToModel(snapshot);
        _lastPublishedPresentationSnapshot = snapshot;
        _hasPublishedPresentationSnapshot = true;
        _lastPresentationSnapshotLocalTime = Time.unscaledTime;
        _nextPresentationSnapshotTime = Time.unscaledTime + PresentationSnapshotInterval;
    }

    private void CapturePresentationSnapshotFromModel()
    {
        if (model == null || model.presentationSnapshotRoomTime <= 0.0 ||
            model.presentationSnapshotRoomTime <= _lastReceivedPresentationSnapshotRoomTime)
        {
            return;
        }

        _lastReceivedPresentationSnapshotRoomTime = model.presentationSnapshotRoomTime;
        AddPresentationSnapshot(CreatePresentationSnapshotFromModel(
            model.presentationSnapshotRoomTime));
    }

    private void OnPresentationSnapshotRoomTimeDidChange(FireStateModel changedModel, double value)
    {
        CapturePresentationSnapshotFromModel();
    }

    private void OnPresentationTransitionEpochDidChange(FireStateModel changedModel, int value)
    {
        if (value <= _lastReceivedPresentationTransitionEpoch ||
            changedModel.presentationTransitionRoomTime <= 0.0)
        {
            return;
        }

        _lastReceivedPresentationTransitionEpoch = value;

        // The reliable epoch is written after the canonical lifecycle metadata.
        // Capture those fields at the authority's transition time even if the next
        // bounded-rate continuous snapshot has not arrived yet.
        AddPresentationSnapshot(new FirePresentationSnapshot
        {
            roomTime = changedModel.presentationTransitionRoomTime,
            fireSpread = Mathf.Clamp(changedModel.fireSpread, 0f, _flammableObject.maxSpread),
            extinguishProgress = Mathf.Clamp01(changedModel.extinguishProgress),
            putOutCenterLocal = changedModel.putOutCenterLocal,
            temperature = Mathf.Max(0f, changedModel.profileTemperature),
            lifecycleFlags = GetLifecycleFlags(
                changedModel.isBurning,
                changedModel.isExtinguished,
                changedModel.isBurnedOut),
            ignitionEpoch = changedModel.ignitionEpoch,
            ignitionOriginLocal = changedModel.ignitionOriginLocal,
            ignitionStartRoomTime = changedModel.ignitionStartRoomTime,
            extinguishFadeStartRoomTime = changedModel.extinguishFadeStartRoomTime,
            extinguishFadeDuration = changedModel.extinguishFadeDuration
        });
    }

    private void AddPresentationSnapshot(FirePresentationSnapshot snapshot)
    {
        if (snapshot.roomTime <= 0.0)
            return;

        int insertIndex = _presentationBuffer.Count;
        while (insertIndex > 0 && _presentationBuffer[insertIndex - 1].roomTime > snapshot.roomTime)
            insertIndex--;

        if (insertIndex > 0 &&
            System.Math.Abs(_presentationBuffer[insertIndex - 1].roomTime - snapshot.roomTime) < 0.000001)
        {
            _presentationBuffer[insertIndex - 1] = snapshot;
        }
        else if (insertIndex < _presentationBuffer.Count &&
                 System.Math.Abs(_presentationBuffer[insertIndex].roomTime - snapshot.roomTime) < 0.000001)
        {
            _presentationBuffer[insertIndex] = snapshot;
        }
        else
        {
            _presentationBuffer.Insert(insertIndex, snapshot);
        }

        while (_presentationBuffer.Count > PresentationBufferCapacity)
            _presentationBuffer.RemoveAt(0);
    }

    private bool TryGetPresentedSnapshot(out FirePresentationSnapshot result)
    {
        result = default;
        if (_presentationBuffer.Count == 0 || realtime == null || !realtime.connected)
            return false;

        double presentationTime = realtime.roomTime - GetSharedPresentationDelay();
        int upperIndex = -1;
        for (int i = 0; i < _presentationBuffer.Count; i++)
        {
            if (_presentationBuffer[i].roomTime >= presentationTime)
            {
                upperIndex = i;
                break;
            }
        }

        if (upperIndex < 0)
        {
            result = _presentationBuffer[_presentationBuffer.Count - 1];
            result.roomTime = presentationTime;
            if (presentationTime - _presentationBuffer[_presentationBuffer.Count - 1].roomTime >
                PresentationSnapshotInterval * 1.5 &&
                _lastLatePresentationUnityFrame != Time.frameCount)
            {
                _lastLatePresentationUnityFrame = Time.frameCount;
                _latePresentationFrameCount++;
            }

            return true;
        }

        if (upperIndex == 0)
        {
            result = _presentationBuffer[0];
            result.roomTime = presentationTime;
            return true;
        }

        FirePresentationSnapshot from = _presentationBuffer[upperIndex - 1];
        FirePresentationSnapshot to = _presentationBuffer[upperIndex];
        double duration = to.roomTime - from.roomTime;
        float t = duration > 0.000001
            ? Mathf.Clamp01((float)((presentationTime - from.roomTime) / duration))
            : 1f;

        // Lifecycle and ignition epochs are step values. Do not blend across a
        // discrete transition; switch exactly at its authoritative sample time.
        if (from.lifecycleFlags != to.lifecycleFlags || from.ignitionEpoch != to.ignitionEpoch)
        {
            result = presentationTime + 0.000001 >= to.roomTime ? to : from;
            result.roomTime = presentationTime;
            return true;
        }

        result = from;
        result.roomTime = presentationTime;
        result.fireSpread = Mathf.Lerp(from.fireSpread, to.fireSpread, t);
        result.extinguishProgress = Mathf.Lerp(from.extinguishProgress, to.extinguishProgress, t);
        result.putOutCenterLocal = Vector3.Lerp(from.putOutCenterLocal, to.putOutCenterLocal, t);
        result.temperature = Mathf.Lerp(from.temperature, to.temperature, t);
        result.ignitionOriginLocal = Vector3.Lerp(from.ignitionOriginLocal, to.ignitionOriginLocal, t);
        return true;
    }

    private float GetSharedPresentationDelay()
    {
        if (model != null && model.presentationDelaySeconds > 0f)
            return Mathf.Clamp(model.presentationDelaySeconds, 0.05f, 0.5f);

        return Mathf.Clamp(presentationDelaySeconds, 0.05f, 0.5f);
    }

    private bool HasBufferedPresentationMetadata()
    {
        return model != null && model.presentationDelaySeconds > 0f;
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

        if (TryGetPresentedSnapshot(out FirePresentationSnapshot presentation))
        {
            _presentedSnapshot = presentation;
            _hasPresentedSnapshot = true;
            if (presentation.IsBurning)
            {
                ApplySynchronizedBurningVisual(presentation);
                ApplyExactExtinguishState(presentation);
                if (enableLocalExtinguishPrediction)
                    ApplyLocalExtinguishPrediction();
                else
                    ResetLocalExtinguishPrediction();
            }
            else
            {
                _flammableObject.ApplyNetworkInactiveState(
                    presentation.IsExtinguished,
                    presentation.IsBurnedOut);
                ResetLocalExtinguishPrediction();
            }

            if (presentation.IsExtinguished)
            {
                ApplySynchronizedExtinguishFade(
                    presentation.roomTime,
                    presentation.extinguishFadeStartRoomTime,
                    presentation.extinguishFadeDuration);
            }
        }
        else
        {
            // Backward compatibility for rooms created by builds without buffered
            // presentation metadata.
            if (model.isBurning)
            {
                ApplySynchronizedBurningVisual();
                ApplyExactExtinguishState();
                if (enableLocalExtinguishPrediction)
                    ApplyLocalExtinguishPrediction();
            }
            else
            {
                _flammableObject.ApplyNetworkInactiveState(
                    model.isExtinguished,
                    model.isBurnedOut);
            }

            ApplySynchronizedExtinguishFade();
        }

        if (_flammableObject.onFire &&
            ((_hasPresentedSnapshot && (_presentedSnapshot.IsExtinguished || _presentedSnapshot.IsBurnedOut)) ||
             (!_hasPresentedSnapshot && (model.isExtinguished || model.isBurnedOut))))
        {
            _flammableObject.RefreshNetworkFireVisualState();

            int seed = model.vfxSeed == 0
                ? DeriveCycleVfxSeed(model.ignitionEpoch)
                : model.vfxSeed;
            _flammableObject.ApplyNetworkVfxSimulationState(
                seed,
                _hasPresentedSnapshot
                    ? GetSynchronizedFireAge(_presentedSnapshot)
                    : GetSynchronizedFireAge(),
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

    private void ApplySynchronizedBurningVisual(FirePresentationSnapshot presentation)
    {
        if (!presentation.IsBurning)
            return;

        int epoch = Mathf.Max(1, presentation.ignitionEpoch);
        int seed = model.vfxSeed == 0 ? DeriveCycleVfxSeed(epoch) : model.vfxSeed;
        bool restartVfx = _appliedIgnitionEpoch != epoch || _appliedVfxSeed != seed;

        _flammableObject.ApplyNetworkFireOriginLocal(presentation.ignitionOriginLocal);
        if (!_flammableObject.onFire)
        {
            _flammableObject.ConfigureNetworkVfxSeed(seed);
            _flammableObject.SetOnFireFromCenterFromNetwork();
            _flammableObject.ApplyNetworkFireOriginLocal(presentation.ignitionOriginLocal);
            restartVfx = true;
        }

        if (!_flammableObject.onFire)
            return;

        if (!presentation.IsExtinguished && !presentation.IsBurnedOut)
        {
            _flammableObject.ApplyNetworkFireVisualState(
                seed,
                GetSynchronizedFireAge(presentation),
                presentation.fireSpread,
                restartVfx);
        }

        _appliedIgnitionEpoch = epoch;
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

    private float GetSynchronizedFireAge(FirePresentationSnapshot presentation)
    {
        if (presentation.ignitionStartRoomTime <= 0.0 || _flammableObject.fireCrawlSpeed <= 0.00001f)
            return 0f;

        float age = Mathf.Max(
            0f,
            (float)(presentation.roomTime - presentation.ignitionStartRoomTime));
        if (!presentation.IsBurnedOut && !presentation.IsExtinguished)
        {
            float burnoutThreshold = _flammableObject.burnOutStart_s + _flammableObject.burnOutLength_s;
            age = Mathf.Min(age, Mathf.Max(0f, burnoutThreshold - 0.01f));
        }

        return age;
    }

    private float GetPredictedAuthoritySpread(float fireAge)
    {
        // Compatibility only: current rooms render authoritative buffered samples.
        // Retain this for a mixed-version/old datastore whose presentation delay
        // and snapshot fields are absent; do not use it in the synchronized path.
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

    private void ApplyExactExtinguishState(FirePresentationSnapshot presentation)
    {
        if (!_flammableObject.onFire)
            return;

        float threshold = GetExtinguishThreshold();
        Vector3 centerWorld = transform.TransformPoint(presentation.putOutCenterLocal);
        float targetRadius = presentation.extinguishProgress * Mathf.Max(0f, threshold);
        _flammableObject.ApplyNetworkExtinguishState(
            centerWorld,
            targetRadius,
            presentation.IsExtinguished);
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
        if (model == null || !model.isExtinguished)
            return;

        ApplySynchronizedExtinguishFade(
            realtime != null && realtime.connected ? realtime.roomTime : 0.0,
            model.extinguishFadeStartRoomTime,
            model.extinguishFadeDuration);
    }

    private void ApplySynchronizedExtinguishFade(
        double presentationRoomTime,
        double fadeStartRoomTime,
        float fadeDuration)
    {
        if (fadeStartRoomTime <= 0.0 ||
            fadeDuration <= 0f ||
            realtime == null ||
            !realtime.connected ||
            !_flammableObject.onFire)
        {
            return;
        }

        float duration = fadeDuration;
        double elapsedRoomTime = presentationRoomTime - fadeStartRoomTime;
        float elapsed = Mathf.Clamp((float)elapsedRoomTime, 0f, duration + 0.01f);

        // Ignis compares onFireTimer against its local burnOutLength_s, so apply
        // the authority's duration before positioning the timer on the shared
        // timeline. Assigning (rather than only advancing) also corrects clients
        // whose local simulation ran slightly ahead.
        _flammableObject.burnOutLength_s = duration;
        _flammableObject.onFireTimer = _flammableObject.burnOutStart_s + elapsed;
    }

    private float GetPresentedBurnoutMultiplier(FirePresentationSnapshot presentation)
    {
        if (presentation.IsBurnedOut)
            return 0f;
        if (!presentation.IsExtinguished ||
            presentation.extinguishFadeStartRoomTime <= 0.0 ||
            presentation.extinguishFadeDuration <= 0f)
        {
            return 1f;
        }

        float progress = (float)((presentation.roomTime - presentation.extinguishFadeStartRoomTime) /
                                 presentation.extinguishFadeDuration);
        return 1f - Mathf.Clamp01(progress);
    }

    private void ApplyPresentedLifecycleSideEffects(FirePresentationSnapshot presentation)
    {
        if (presentation.lifecycleFlags == _lastAppliedPresentedLifecycleFlags)
            return;

        _lastAppliedPresentedLifecycleFlags = presentation.lifecycleFlags;
        if (presentation.IsExtinguished || presentation.IsBurnedOut)
            RunSharedExtinguishCleanup();
    }

    // ------------------------------------------------------------------
    // Model change handlers (all clients)
    // ------------------------------------------------------------------

    private void OnIsBurningDidChange(FireStateModel changedModel, bool value)
    {
        if (IsAuthority)
            return; // authority's Ignis already shows reality

        if (HasBufferedPresentationMetadata())
            return;

        if (value)
            ApplySynchronizedBurningVisual();
        else if (changedModel.isExtinguished || changedModel.isBurnedOut)
            ApplyExtinguishedVisual(changedModel.isBurnedOut);
    }

    private void OnIsExtinguishedDidChange(FireStateModel changedModel, bool value)
    {
        if (!value)
            return;

        if (IsAuthority)
        {
            if (!HasBufferedPresentationMetadata())
                RunSharedExtinguishCleanup();
            return;
        }

        if (!HasBufferedPresentationMetadata())
        {
            ApplyExtinguishedVisual(false);
            RunSharedExtinguishCleanup();
        }
    }

    private void OnIsBurnedOutDidChange(FireStateModel changedModel, bool value)
    {
        if (!value)
            return;

        if (IsAuthority)
        {
            if (!HasBufferedPresentationMetadata())
                RunSharedExtinguishCleanup();
            return;
        }

        if (!HasBufferedPresentationMetadata())
        {
            ApplyExtinguishedVisual(true);
            RunSharedExtinguishCleanup();
        }
    }

    private void OnExtinguishProgressDidChange(FireStateModel changedModel, float value)
    {
        if (!IsAuthority)
        {
            _awaitingPostResetExtinguishSample = false;
            if (!HasBufferedPresentationMetadata())
                ApplyExactExtinguishState();
        }
    }

    private void OnPutOutCenterDidChange(FireStateModel changedModel, Vector3 value)
    {
        if (!IsAuthority && !HasBufferedPresentationMetadata())
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
        if (!IsAuthority && !HasBufferedPresentationMetadata() && changedModel.isBurning)
            ApplySynchronizedBurningVisual();
    }

    private void OnFireSpreadDidChange(FireStateModel changedModel, float value)
    {
        _awaitingPostResetSpreadSample = false;

        if (!IsAuthority && !HasBufferedPresentationMetadata() && changedModel.isBurning)
            ApplySynchronizedBurningVisual();
    }

    private void OnVisualMetadataEpochDidChange(FireStateModel changedModel, int value)
    {
        if (!IsAuthority && !HasBufferedPresentationMetadata() &&
            changedModel.isBurning && value == changedModel.ignitionEpoch)
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
