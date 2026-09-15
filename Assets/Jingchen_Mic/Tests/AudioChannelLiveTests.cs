using System;
using System.Collections;
using System.Collections.Generic;
using Normal;
using Normal.Realtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Jingchen.Mic.Tests
{
    /// <summary>
    /// Real room-server tests. Set JINGCHEN_RUN_LIVE_NORMCORE_TESTS=1 and select
    /// the LiveNormcore category to opt in. These tests do not emulate a server
    /// and do not open a microphone or require a connected headset.
    /// </summary>
    [Category("LiveNormcore")]
    public sealed class AudioChannelLiveTests
    {
        private sealed class Client
        {
            public GameObject root;
            public Realtime realtime;
            public AudioChannelNetwork channel;
        }

        private readonly List<Client> clients = new List<Client>();
        private string roomName;
        private NormcoreAppSettings settings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (Environment.GetEnvironmentVariable("JINGCHEN_RUN_LIVE_NORMCORE_TESTS") != "1")
                Assert.Ignore("Live Normcore tests are opt-in. Set JINGCHEN_RUN_LIVE_NORMCORE_TESTS=1.");

            settings = Resources.Load<NormcoreAppSettings>("NormcoreAppSettings");
            Assert.That(settings, Is.Not.Null, "The project's NormcoreAppSettings resource is required.");
            roomName = "audio-channel-validation-" + Guid.NewGuid().ToString("N");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Client client in clients)
            {
                if (client.channel != null)
                    client.channel.SetTalkIntent(false);
                if (client.realtime != null)
                    client.realtime.Disconnect();
            }

            foreach (Client client in clients)
            {
                if (client.channel != null)
                    UnityEngine.Object.Destroy(client.channel.gameObject);
                if (client.root != null)
                    UnityEngine.Object.Destroy(client.root);
            }

            clients.Clear();
            yield return null;
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator ThreeClientsShareOneFloorAndRecoverFromDisconnect()
        {
            for (int i = 0; i < 3; i++)
            {
                Client client = AddClient();
                client.realtime.Connect(roomName);
                yield return WaitFor(() => client.realtime.connected && client.channel.Ready,
                    "client " + i + " connects and receives a channel", 60f);
                Assert.That(client.channel.LocalChannel, Is.EqualTo(i),
                    "Sequential joins must be assigned A, B and C.");
            }

            Debug.Log("AudioChannel live phase: sequential A/B/C assignment passed.");

            // All requests begin before the next frame, so the actual room
            // server resolves the concurrent requests.
            foreach (Client client in clients)
                client.channel.SetTalkIntent(true);

            yield return WaitFor(() => FloorCount() == 1, "one concurrent request wins");
            Client firstSpeaker = FindSpeaker();
            Assert.That(firstSpeaker, Is.Not.Null);
            yield return WaitFor(() => EveryConnectedClientHears(firstSpeaker),
                "all three channels agree on the current speaker");
            yield return ObserveExclusiveFloor((float)AudioChannelNetwork.LeaseSeconds + 1f, firstSpeaker);

            // Deliberately stop Unity's frame clock past the last lease lifetime.
            // Read HasFloor before another Update can refresh room time: safety
            // must use elapsed host time even when the Unity thread is stalled.
            System.Threading.Thread.Sleep((int)((AudioChannelNetwork.LeaseSeconds + 1.0) * 1000.0));
            Assert.That(firstSpeaker.channel.HasFloor, Is.False,
                "An expired microphone lease must fail closed before the first frame after a stall.");
            AssertExclusive();
            yield return null;
            AssertExclusive();

            foreach (Client client in clients)
                client.channel.SetTalkIntent(false);
            yield return WaitFor(NoActiveSpeaker, "stalled clients release expired or pending requests");
            yield return WaitFor(() => firstSpeaker.realtime.connected && firstSpeaker.channel.Ready,
                "the speaker is ready for a fresh request after a frame stall");
            firstSpeaker.channel.SetTalkIntent(true);
            yield return WaitFor(() => firstSpeaker.channel.HasFloor,
                "a fresh request can transmit after a frame stall");
            yield return WaitFor(() => EveryConnectedClientHears(firstSpeaker),
                "listeners agree after recovering from a frame stall");
            Debug.Log("AudioChannel live phase: monotonic stall expiry and fresh request recovery passed.");

            foreach (Client client in clients)
                if (client != firstSpeaker)
                    client.channel.SetTalkIntent(false);
            firstSpeaker.channel.SetTalkIntent(false);
            Assert.That(firstSpeaker.channel.HasFloor, Is.False, "Release must mute locally immediately.");
            yield return WaitFor(NoActiveSpeaker, "release reaches every listener");

            Debug.Log("AudioChannel live phase: concurrent arbitration and cross-channel listening passed.");

            Client responder = clients.Find(client => client != firstSpeaker);
            responder.channel.SetTalkIntent(true);
            yield return WaitFor(() => responder.channel.HasFloor, "another channel can respond");
            yield return WaitFor(() => EveryConnectedClientHears(responder), "response reaches all channels");
            responder.channel.SetTalkIntent(false);
            Assert.That(responder.channel.HasFloor, Is.False);
            yield return WaitFor(NoActiveSpeaker, "response release reaches listeners");

            // Allow a network Update to issue the request before cancellation.
            // Record whether it is still pending rather than assuming a minimum
            // room-server latency on the machine running this test.
            firstSpeaker.channel.SetTalkIntent(true);
            yield return null;
            bool cancelledWhilePending = firstSpeaker.channel.IsRequestPending;
            firstSpeaker.channel.SetTalkIntent(false);
            Debug.Log("AudioChannel live cancellation was pending at release: " + cancelledWhilePending);
            yield return ObserveNoLocalFloor(firstSpeaker, 2f);
            yield return WaitFor(NoActiveSpeaker, "cancelled request leaves the room free");

            int previousChannel = responder.channel.LocalChannel;
            responder.channel.CycleChannel();
            yield return WaitFor(() => responder.channel.LocalChannel == (previousChannel + 1) % 3,
                "channel switch advances A/B/C");
            responder.channel.SetTalkIntent(true);
            yield return WaitFor(() => responder.channel.HasFloor, "switched channel can transmit");
            yield return WaitFor(() => EveryConnectedClientHears(responder), "switched channel is replicated");
            previousChannel = responder.channel.LocalChannel;
            responder.channel.CycleChannel();
            Assert.That(responder.channel.HasFloor, Is.False, "Switching while talking must mute immediately.");
            Assert.That(responder.channel.LocalChannel, Is.EqualTo((previousChannel + 1) % 3));
            yield return WaitFor(NoActiveSpeaker, "switching while held releases the floor");
            responder.channel.SetTalkIntent(false);
            responder.channel.SetTalkIntent(true);
            yield return WaitFor(() => responder.channel.HasFloor, "fresh request after channel switch succeeds");

            Debug.Log("AudioChannel live phase: handoff, cancelled request and channel switch passed.");

            // Deliberately disconnect while holding the floor. The other clients
            // must recover using the real server state and the expired lease.
            responder.realtime.Disconnect();
            yield return null;
            Assert.That(responder.channel.HasFloor, Is.False, "Disconnect must mute the local speaker.");
            firstSpeaker.channel.SetTalkIntent(true);
            yield return WaitFor(() => firstSpeaker.channel.HasFloor,
                "a disconnected speaker cannot keep the room locked", 25f);
            yield return WaitFor(() => EveryConnectedClientHears(firstSpeaker),
                "remaining listeners agree after disconnect recovery");

            // Rejoin during someone else's transmission. It is a late join into
            // active state and must not resume the previous held microphone.
            responder.realtime.Connect(roomName);
            yield return WaitFor(() => responder.realtime.connected && responder.channel.Ready,
                "disconnected client rejoins", 60f);
            Assert.That(responder.channel.LocalChannel, Is.InRange(0, 2));
            Assert.That(responder.channel.HasFloor, Is.False, "Reconnect must not resume old input.");
            yield return WaitFor(() => EveryConnectedClientHears(firstSpeaker),
                "late rejoin receives the active speaker");
            yield return ObserveExclusiveFloor(2f, firstSpeaker);

            firstSpeaker.channel.SetTalkIntent(false);
            yield return WaitFor(NoActiveSpeaker, "final release reaches every listener");
            responder.channel.SetTalkIntent(false);
            responder.channel.SetTalkIntent(true);
            yield return WaitFor(() => responder.channel.HasFloor, "rejoined client can request a fresh turn");
            responder.channel.SetTalkIntent(false);
            yield return WaitFor(NoActiveSpeaker, "room is idle at the end");

            Debug.Log("AudioChannel live phase: disconnect recovery, late rejoin and fresh request passed.");
        }

        private Client AddClient()
        {
            GameObject root = new GameObject("Audio Channel Live Test Client " + clients.Count);
            root.SetActive(false);
            Realtime realtime = root.AddComponent<Realtime>();
            // Configure the same serialized settings used by the existing
            // Realtime prefab. No networking state or arbitration is mocked.
            JsonUtility.FromJsonOverwrite(
                "{\"_serializedVersion\":1,\"_joinRoomOnStart\":false," +
                "\"_joinRoomOnStartOptions\":{\"_enabled\":false}}", realtime);
            realtime.normcoreAppSettings = settings;
            root.SetActive(true);
            Client client = new Client
            {
                root = root,
                realtime = realtime,
                channel = AudioChannelNetwork.Create(realtime)
            };
            Assert.That(client.channel, Is.Not.Null);
            clients.Add(client);
            return client;
        }

        private IEnumerator WaitFor(Func<bool> condition, string description, float timeoutSeconds = 20f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                AssertExclusive();
                if (Time.realtimeSinceStartup >= deadline)
                    Assert.Fail("Timed out waiting for " + description + ". " + DescribeClients());
                yield return null;
            }
            AssertExclusive();
        }

        private IEnumerator ObserveExclusiveFloor(float seconds, Client expectedSpeaker)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                AssertExclusive();
                Assert.That(expectedSpeaker.channel.HasFloor, Is.True,
                    "An acknowledged held microphone must keep its floor across lease renewal.");
                yield return null;
            }
        }

        private IEnumerator ObserveNoLocalFloor(Client client, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                AssertExclusive();
                Assert.That(client.channel.HasFloor, Is.False,
                    "Releasing before acknowledgement must never open the microphone.");
                yield return null;
            }
        }

        private void AssertExclusive()
        {
            Assert.That(FloorCount(), Is.LessThanOrEqualTo(1),
                "Two clients must never simultaneously be allowed to transmit. " + DescribeClients());
        }

        private int FloorCount()
        {
            int count = 0;
            foreach (Client client in clients)
                if (client.channel != null && client.channel.HasFloor)
                    count++;
            return count;
        }

        private Client FindSpeaker()
        {
            return clients.Find(client => client.channel != null && client.channel.HasFloor);
        }

        private bool EveryConnectedClientHears(Client speaker)
        {
            foreach (Client client in clients)
                if (client.realtime.connected &&
                    (client.channel.ActiveSpeakerClientId != speaker.realtime.clientID ||
                     client.channel.ActiveChannel != speaker.channel.LocalChannel))
                    return false;
            return true;
        }

        private bool NoActiveSpeaker()
        {
            foreach (Client client in clients)
                if (client.channel.HasFloor ||
                    (client.realtime.connected && client.channel.ActiveSpeakerClientId >= 0))
                    return false;
            return true;
        }

        private string DescribeClients()
        {
            List<string> descriptions = new List<string>();
            foreach (Client client in clients)
                descriptions.Add("client=" + client.realtime.clientID +
                    ", connected=" + client.realtime.connected +
                    ", ready=" + client.channel.Ready +
                    ", channel=" + client.channel.LocalChannel +
                    ", hasFloor=" + client.channel.HasFloor +
                    ", activeSpeaker=" + client.channel.ActiveSpeakerClientId +
                    ", status=" + client.channel.ConnectionStatus);
            return string.Join("; ", descriptions);
        }
    }
}
