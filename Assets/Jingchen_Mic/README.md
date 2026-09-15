# Audio channels

This folder replaces the former lapel microphone with a room-wide push-to-talk
radio. Existing scenes, avatars, fire simulation and locomotion require no edits.
Use the same updated project version on every participant's client.

## Controls on Touch / OpenXR controllers

- Hold **right A** to request the microphone. Start speaking when the HUD says
  **TALKING**. Release A to stop immediately.
- Tap **left X** to cycle A -> B -> C -> A. Switching cancels any current request;
  release A before pressing it again.
- New participants receive A, B, C, A, ... in server-confirmed registration order.
  Existing participants are not reassigned when somebody joins or leaves.
- All channels hear the current speaker, regardless of avatar distance. There
  is exactly one shared microphone permission for the entire room, including
  participants in the same channel.
- Holding A while busy waits for an available turn. Releasing A cancels that
  request. Contending requests are arbitrated by the server; this is not a FIFO
  speaking queue.
- No hand-to-chest proximity or lapel model is used. A/X were the former lapel
  buttons. B/Y, triggers, grips and thumbsticks retain their existing bindings.

The status text follows the local avatar's head. It shows the selected channel,
current speaking channel, ready/busy/requesting state and the controls.

## Integration and failure handling

`AudioChannelBootstrap` discovers `RealtimeAvatarManager` instances and installs
one controller per `Realtime`. New voice components are muted immediately.
`AudioChannelController` runs before `RealtimeAvatarVoice.Update`, gates local
transmission, and accepts remote playback only from the confirmed current speaker.
Only the voice component's AudioSource is configured for non-positional radio
reception; other scene audio is not affected.

`AudioChannelNetwork` uses Normcore 3.5.1 `StringKeyDictionary` server-confirmed
transactions. No speculative ownership flag opens a microphone. Every grant and
renewal replaces the entire immutable record, so acquisition, renewal and stale
cleanup compete on the same dictionary key. Each press has a new nonce, and
callbacks are scoped to the current room model and connection generation.

A grant lasts six seconds and renews while held. The sender closes half a second
before an unrenewed expiry; peers may clear it half a second after expiry. This
recovers abandoned microphones even when a process disappears without sending
a release. Normal button release sends an immediate local mute and a transaction
to free the floor. Focus loss, pause, disabled components, missing local avatar,
lost controller tracking and disconnection close the microphone. After focus or
tracking recovers, A must be released before another press can transmit.

The confirmed grant also has a local monotonic deadline. A main-thread stall
longer than half a second closes permission and requires a fresh press. A final
`voiceData` sample guard replaces unauthorized buffers with silence immediately
before Normcore sends them, including a stall between the frame gate and send.

The inactive `Resources/JingchenAudioChannelNetwork.prefab` is a separate shared
scene-view template. Normcore clears scene UUIDs on prefab assets, so the factory
sets the verified serialized `_viewUUID` field with Unity `JsonUtility` before
activation, then uses `sceneViewWillRegisterWithRealtime` to bind the intended
room. This small serialization bridge is specific to the installed Normcore
3.5.1 schema and should be revalidated when upgrading Normcore. No existing
network prefab or scene is modified.

## Validation

`Tests/EditMode` covers the input safety state machine. `Tests/AudioChannelLiveTests`
connects real, separate Realtime clients to a unique test room. The live suite is
opt-in: set `JINGCHEN_RUN_LIVE_NORMCORE_TESTS=1`, then run PlayMode tests in the
`LiveNormcore` category. It uses the project's existing NormcoreAppSettings and
does not open a microphone or alter a production room.

The live test checks sequential channel assignment, simultaneous requests,
cross-channel speaker agreement, release and handoff, cancellation, switching,
lease renewal, long frame stalls, late joining and disconnect/rejoin recovery. Test logs distinguish
network permission checks from real audio or headset validation. Offline rooms
are not a substitute for the live concurrency test.

Before a participant session, use two or more headsets running this branch to
check the HUD in both eyes, A/X input, actual microphone permission and audio,
and takeover after a headset sleeps. No automated desktop test can certify those
hardware behaviors.

### Verified on 2026-09-15

- Unity 6000.0.66f1 project compilation succeeded after merging the latest
  `origin/demo-merge2` commit `fe08195`.
- All 15 EditMode cases passed, including a rendered HUD capture.
- The real three-client PlayMode scenario passed, including a deliberate
  seven-second main-thread stall, lease renewal and disconnect/rejoin recovery.
  The final scenario completed in 56.95 seconds with zero failed tests.
- No headset was connected. The batch run could not initialize OpenXR Display,
  and this Unity installation has no Android build module. Actual headset audio,
  stereo rendering and a Quest APK were not validated.

Normcore reference: https://docs.normcore.io/room/collections
