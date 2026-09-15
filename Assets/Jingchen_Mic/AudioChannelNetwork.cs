using System;
using Normal.Realtime;
using Normal.Realtime.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jingchen.Mic
{
    [DefaultExecutionOrder(-1000)]
    public sealed class AudioChannelNetwork : RealtimeComponent<AudioChannelRoomModel>
    {
        public const double LeaseSeconds = 6.0;
        public const double LeaseGuardSeconds = 0.5;
        private const double RenewalSeconds = 2.0;
        private const string FloorKey = "floor";
        private const string SequenceKey = "join-sequence";
        private const string ResourceName = "JingchenAudioChannelNetwork";
        private const string SceneViewUuid = "265e320a-26b3-49be-aa7a-7bb7a59e2463";

        private Realtime target;
        private bool joined;
        private bool talkIntent;
        private bool floorOperationPending;
        private bool joinOperationPending;
        private int sessionEpoch;
        private int sessionClientId = -1;
        private int localChannel = -1;
        private string intentNonce = "";
        private double nextRenewal;
        private double nextFloorAttempt;
        private double nextJoinAttempt;
        private bool hasNetworkTick;
        private bool clockSafeThisFrame;
        private double lastNetworkTick;
        private double previousNetworkTick;
        private string confirmedNonce = "";
        private double confirmedExpiresAt;
        private double confirmedMonotonicDeadline;

        public Realtime TargetRealtime { get { return target; } }
        public bool Ready { get { return Connected && joined && isActiveAndEnabled && ClockSafe; } }
        public int LocalChannel { get { return localChannel; } }
        public bool IsRequestPending { get { return talkIntent && Ready && !HasFloor; } }
        public string ConnectionStatus { get { return !Connected ? "Connecting" : !joined ? "Joining channel" : "Ready"; } }
        private bool Connected
        {
            get { return target != null && target.connected && model != null && model.isRoomConnected; }
        }

        private static double MonotonicNow
        {
            get { return (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency; }
        }

        private bool ClockSafe
        {
            get { return clockSafeThisFrame && MonotonicNow - lastNetworkTick < LeaseGuardSeconds; }
        }

        public bool HasFloor
        {
            get
            {
                AudioChannelRecordModel floor;
                return Ready && talkIntent && TryGetFloor(out floor)
                    && floor.clientId == target.clientID && floor.nonce == intentNonce
                    && floor.nonce == confirmedNonce && floor.expiresAt == confirmedExpiresAt
                    && MonotonicNow < confirmedMonotonicDeadline
                    && target.roomTime < floor.expiresAt - LeaseGuardSeconds;
            }
        }

        public int ActiveSpeakerClientId
        {
            get
            {
                AudioChannelRecordModel floor;
                return Connected && ClockSafe && TryGetFloor(out floor) && target.roomTime < floor.expiresAt
                    ? floor.clientId : -1;
            }
        }

        public int ActiveChannel
        {
            get
            {
                AudioChannelRecordModel floor;
                return Connected && ClockSafe && TryGetFloor(out floor) && target.roomTime < floor.expiresAt
                    ? floor.channel : -1;
            }
        }

        public static AudioChannelNetwork Create(Realtime realtimeInstance)
        {
            if (realtimeInstance == null)
                throw new ArgumentNullException(nameof(realtimeInstance));

            foreach (AudioChannelNetwork existing in FindObjectsByType<AudioChannelNetwork>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing.target == realtimeInstance)
                    return existing;
            }

            GameObject template = Resources.Load<GameObject>(ResourceName);
            if (template == null)
            {
                Debug.LogError("Audio channels: missing network Resources prefab.");
                return null;
            }

            // Normcore has no public runtime scene-view UUID setter. Its serialized
            // scene-view schema is therefore applied to this isolated, inactive
            // template before Awake/Start, using Unity's public serialization API.
            // Unlike an instantiated network prefab, every peer registers the same
            // UUID and therefore the same transactional dictionary in its room.
            GameObject instance = UnityEngine.Object.Instantiate(template);
            instance.name = "Audio Channels (" + realtimeInstance.GetInstanceID() + ")";
            Scene scene = realtimeInstance.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.MoveGameObjectToScene(instance, scene);

            AudioChannelNetwork network = instance.GetComponent<AudioChannelNetwork>();
            RealtimeView view = instance.GetComponent<RealtimeView>();
            network.target = realtimeInstance;
            JsonUtility.FromJsonOverwrite("{\"_viewUUID\":\"" + SceneViewUuid + "\"}", view);
            view.sceneViewWillRegisterWithRealtime = ignored => realtimeInstance;
            instance.SetActive(true);
            return network;
        }

        public void SetTalkIntent(bool held)
        {
            held = held && Ready;
            if (talkIntent == held)
                return;

            talkIntent = held;
            if (held)
            {
                intentNonce = Guid.NewGuid().ToString("N");
                nextFloorAttempt = 0.0;
            }
            else
            {
                ReleaseOwnFloor();
            }
        }

        public void CycleChannel()
        {
            if (!Ready)
                return;

            SetTalkIntent(false);
            localChannel = (localChannel + 1) % 3;
        }

        protected override void OnRealtimeModelReplaced(AudioChannelRoomModel previousModel, AudioChannelRoomModel currentModel)
        {
            ResetSession();
        }

        private void ResetSession()
        {
            sessionEpoch++;
            sessionClientId = -1;
            joined = false;
            talkIntent = false;
            floorOperationPending = false;
            joinOperationPending = false;
            localChannel = -1;
            intentNonce = "";
            confirmedNonce = "";
            confirmedExpiresAt = 0.0;
            confirmedMonotonicDeadline = 0.0;
            nextJoinAttempt = 0.0;
            nextFloorAttempt = 0.0;
        }

        private void Update()
        {
            double now = MonotonicNow;
            clockSafeThisFrame = hasNetworkTick && now - lastNetworkTick < LeaseGuardSeconds;
            previousNetworkTick = hasNetworkTick ? lastNetworkTick : now;
            lastNetworkTick = now;
            hasNetworkTick = true;
            if (!clockSafeThisFrame)
            {
                // Normcore ticks at execution order 0 and caches room.time. Our
                // input/audio gate runs earlier, so after a stall it must reject
                // that old snapshot for one frame before room.time is refreshed.
                SetTalkIntent(false);
                confirmedMonotonicDeadline = 0.0;
            }

            if (target == null)
            {
                Destroy(gameObject);
                return;
            }

            if (!Connected)
            {
                if (sessionClientId != -1 || joined || talkIntent)
                    ResetSession();
                return;
            }

            if (sessionClientId != target.clientID)
            {
                ResetSession();
                sessionClientId = target.clientID;
            }

            if (!joined)
            {
                RegisterJoin();
                return;
            }

            if (!clockSafeThisFrame)
            {
                ReleaseOwnFloor();
                return;
            }

            if (floorOperationPending)
                return;

            AudioChannelRecordModel floor;
            if (TryGetFloor(out floor))
            {
                if (floor.clientId == target.clientID)
                {
                    if (!talkIntent || floor.nonce != intentNonce
                        || target.roomTime >= floor.expiresAt - LeaseGuardSeconds)
                    {
                        ReleaseOwnFloor();
                    }
                    else if (target.roomTime >= nextRenewal)
                    {
                        WriteFloor(floor.nonce, floor.channel);
                    }
                }
                else if (target.roomTime >= floor.expiresAt + LeaseGuardSeconds)
                {
                    // The prior speaker has already failed closed before expiry.
                    // This removal races safely with renewal: only one CAS wins.
                    RemoveFloor();
                }
                return;
            }

            if (talkIntent && target.roomTime >= nextFloorAttempt)
                WriteFloor(intentNonce, localChannel);
        }

        private void RegisterJoin()
        {
            if (joinOperationPending || target.roomTime < nextJoinAttempt)
                return;

            AudioChannelRecordModel sequence;
            uint ordinal = model.records.TryGetValue(SequenceKey, out sequence) ? sequence.nextJoin : 0;
            AudioChannelRecordModel replacement = new AudioChannelRecordModel();
            // Keep only the next modulo-three slot, avoiding integer overflow.
            replacement.nextJoin = (ordinal + 1) % 3;
            int epoch = sessionEpoch;
            AudioChannelRoomModel roomModel = model;
            joinOperationPending = true;
            roomModel.records.Insert(SequenceKey, replacement, success =>
            {
                if (!IsCurrent(roomModel, epoch))
                    return;
                joinOperationPending = false;
                if (success)
                {
                    localChannel = (int)(ordinal % 3);
                    joined = true;
                }
                else
                {
                    nextJoinAttempt = target.roomTime + RetryDelay();
                }
            });
        }

        private void WriteFloor(string nonce, int channel)
        {
            AudioChannelRoomModel roomModel = model;
            int epoch = sessionEpoch;
            AudioChannelRecordModel replacement = new AudioChannelRecordModel();
            replacement.clientId = target.clientID;
            replacement.channel = channel;
            replacement.nonce = nonce;
            replacement.expiresAt = target.roomTime + LeaseSeconds;
            floorOperationPending = true;
            roomModel.records.Insert(FloorKey, replacement, success =>
            {
                if (!IsCurrent(roomModel, epoch))
                    return;
                floorOperationPending = false;
                nextFloorAttempt = target.roomTime + RetryDelay();
                if (success)
                {
                    nextRenewal = target.roomTime + RenewalSeconds;
                    confirmedNonce = nonce;
                    confirmedExpiresAt = replacement.expiresAt;
                    // Room.time is refreshed inside Normcore's later Update.
                    // The previous network Update predates that sample, including
                    // synchronous offline callbacks. Anchoring here can shorten
                    // a lease, but never extend it by callback or frame latency.
                    confirmedMonotonicDeadline = previousNetworkTick + Math.Max(0.0,
                        replacement.expiresAt - target.roomTime - LeaseGuardSeconds);
                    // A grant can return after button-up, a channel change, focus
                    // loss, or disable. Such a grant never opens the microphone.
                    if (!talkIntent || !isActiveAndEnabled || !ClockSafe || intentNonce != nonce
                        || MonotonicNow >= confirmedMonotonicDeadline
                        || target.roomTime >= replacement.expiresAt - LeaseGuardSeconds)
                    {
                        ReleaseOwnFloor(nonce);
                    }
                }
            });
        }

        private void ReleaseOwnFloor(string expectedNonce = null)
        {
            AudioChannelRecordModel floor;
            if (!Connected || floorOperationPending || !TryGetFloor(out floor)
                || floor.clientId != target.clientID
                || (expectedNonce != null && floor.nonce != expectedNonce))
                return;

            RemoveFloor();
        }

        private void RemoveFloor()
        {
            AudioChannelRoomModel roomModel = model;
            int epoch = sessionEpoch;
            floorOperationPending = true;
            roomModel.records.Remove(FloorKey, success =>
            {
                if (!IsCurrent(roomModel, epoch))
                    return;
                floorOperationPending = false;
                nextFloorAttempt = target.roomTime + RetryDelay();
                // OnDisable cannot rely on a later Update to retry a rejected
                // cleanup. The lease still bounds any abandoned floor.
            });
        }

        private bool IsCurrent(AudioChannelRoomModel roomModel, int epoch)
        {
            return this != null && Connected && ReferenceEquals(model, roomModel)
                && sessionEpoch == epoch && target.clientID == sessionClientId;
        }

        private bool TryGetFloor(out AudioChannelRecordModel floor)
        {
            floor = null;
            return model != null && model.records.TryGetValue(FloorKey, out floor);
        }

        private double RetryDelay()
        {
            // Stagger retries after simultaneous requests to avoid synchronized
            // collisions. This changes latency only, never the arbitration rule.
            return 0.08 + (target.clientID % 7) * 0.017;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                SetTalkIntent(false);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SetTalkIntent(false);
        }

        private void OnDisable()
        {
            SetTalkIntent(false);
        }
    }
}
