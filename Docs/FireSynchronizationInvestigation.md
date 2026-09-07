# Ignis Multiplayer Synchronization Investigation

Date: 2026-09-07
Branch investigated: `fire-and-smoke-borders` at `973073f`
History inspected: all refs, with focused diffs from the first Ignis integration (`6eb319d`) through the current branch, plus `normcore_setup`, `stable_multiplayer`, `normcoreSync`, and `network-fixes`.

## 1. Existing architecture

`NetworkedFireState` is attached to each networked `Ignis.FlammableObject`. Its Normcore model has ownership metadata, and the first client to claim an unowned fire becomes the stable authority. If that owner leaves, a remaining client reclaims it. Tool ownership is independent from fire ownership.

The authority alone advances autonomous Ignis lifecycle state and fire-profile state. It evaluates synchronized spray poses, applies extinguish hits, permits ignition/spread triggers, rolls smolder/reignition, and publishes logical state. A non-authority disables `FlammableObject.Update` lifecycle simulation and consumes the replicated model as a puppet. `SpraySync` replicates only the tool's spraying boolean; the nozzle pose comes from `RealtimeTransform`, and water particles remain local.

The authoritative boundary currently ends at high-level state and VFX inputs: burning/extinguished/burned-out flags, extinguish sphere, temperature/profile state, ignition epoch/origin/age, scalar spread, VFX seed, and reset epochs. Each client still instantiates and executes its own Ignis Visual Effect Graph. Therefore equal `FireStateModel` values do not imply equal generated particle geometry.

Offline behavior is intentionally preserved: an unconnected client is authority, and unnetworked fires/tools use legacy local behavior.

## 2. Git-history findings

### Branch comparison

`origin/normcore_setup` is the March-May networking line. Commit `ed2a8ce` installed Normcore and configured a `Realtime`/`RealtimeAvatarManager` VR player with head/hand synchronization. Its successor `stable_multiplayer` added connection scripts and firefighter avatar IK. It predates the July Ignis import and contains no `NetworkedFireState`, `FireStateModel`, or `SpraySync`. It addressed player presence/avatar synchronization, not fire synchronization.

`origin/normcoreSync` is a separate line whose merge base with `normcore_setup` is the early repository commit `ad3d538`. It accumulated later gameplay, Ignis, networked interactable, extinguisher, and fire work. `network-fixes` is its direct successor (`normcoreSync` at `6c86e91`, then `network-fixes` at `03d1697`), and both were merged into the current borders branch. This line established the current authority/puppet fire design.

No history examined contains a fixed-emitter occupancy bitset, UV mask, surface grid, voxel mask, or network-owned `SharedFireVFX`. Those are new architectural options, unlike the already-attempted deterministic-Ignis work.

### Attempt timeline

| Commit | Approach and files | Status / reason supported by history |
|---|---|---|
| `1e1c78a` | Initial authority/puppet design in `NetworkedFireState`, `FireStateModel`, `SpraySync`, `Spray`, and Ignis extinguish gates. Stable per-fire owner ran Ignis and authority-side spray raycasts; puppets approximated extinguish progress by repeatedly calling `IncrementalExtinguish`. | Foundation remains. Local particle collisions had caused divergent permanent state, so authority gates and centralized cleanup replaced per-client decisions. Approximate additive puppet extinguishing was later replaced because it could not correct a puppet that was ahead or follow back-spread exactly. |
| `9367f2a` | Added `extinguishFadeStartRoomTime` and duration. Authority and puppets positioned Ignis's burnout timer from shared room time. | Remains. This was the first shared-clock presentation mechanism and handles late joiners better than local timers. It synchronizes fade phase, not packet arrival or particle geometry. |
| `cf24d0b` | Added exact extinguish adapter in modified `FlammableObject`; replicated temperature, smolder roll, reignition decision/deadline; authority-only `FireProfileController`; scheduled teammate-join reset with room-time epoch/barrier; rate limits. | Remains, with transport refinements. It replaced incremental puppet catch-up with exact radius/center correction and prevented each client from independently rolling reignition. Reset barriers exist because stale pre-reset fields could otherwise be consumed after a timed reset. |
| `f2165cf` | Deterministic-Ignis attempt: stable hierarchy-derived seed, per-emitter derived seeds, deterministic mesh sampling, ignition start room time/epoch, shared fire age, sampled spread, late-join VFX prewarm, and transport priority changes. It also changed continuous properties away from reliable delivery. | Remains and was strengthened later. This is direct evidence that “add a seed and age” has already been tried. Reliable continuous updates were superseded to avoid head-of-line delay; final flags stayed reliable. |
| `6c86e91` (`normcoreSync` tip) | Converted puppets from merely increasing `ignitionTime` to strict lifecycle disable; gated all Ignis ignition paths; added exact inactive-state repair; paused VFX Graph auto-time and manually advanced it on a shared 30 Hz logical clock; capped backfill at 90 steps. | Remains. This superseded running Ignis autonomously on both clients. The cap deliberately skips simulation history after a hitch/late join longer than three seconds to protect headset performance, so particle state can permanently differ even with equal logical tick numbers. |
| `03d1697` (`network-fixes`) | Added synchronized ignition origin and metadata barrier, deterministic material interpolation, Simpson-rule spread extrapolation from latest authoritative sample, local extinguish prediction, and consistent profile power. | Remains. Origin was added because equal seeds were insufficient when clients ignited at different positions. Prediction was explicitly introduced for responsiveness and intentionally allows a local temporary visual lead. Spread extrapolation reduces apparent staleness but independently predicts after the newest sample and can diverge from authority integration. |
| `9c2edec` | Unified extinguisher/fire profile compatibility across particle collision, authority raycast, and local prediction. | Remains. |
| `1d8f362` | Borders/visual work: chose automatic origin from room/camera-facing surfaces, reduced box emission to visible/top/wall surfaces, adjusted density and material darkening. | Remains. The camera fallback can produce a different pre-authority local origin, but `03d1697`'s replicated origin later corrects the meaningful spread center. Camera-dependent VFX LOD remains by design. |
| `5c73866` | Delayed configured fire-on-start until two avatars exist, then used a room-time reset/start barrier with 0.75 s lead. | Remains. This replaced independent startup on each client because each could create a different autonomous fire before ownership resolved. |
| `4ad0499`, `f006a29`, `118be99`, `7174fab`, `973073f` | Hardened model readiness, quorum diagnostics, Ignis initialization/teardown, shader reset, delayed-start retry, and `FlameEngine` singleton lifetime. | Remains. These commits show startup/reset complexity is addressing observed initialization races and must not be casually removed. |

There is no explicit revert commit for the deterministic VFX work or prediction. Instead, subsequent commits successively strengthened it. Where a commit message does not state a failure, the replacement reason above is limited to what the diff/comments demonstrate; it is not claimed as a measured test result.

## 3. Current divergence sources

The repository supports the following concrete sources:

1. Every client owns a separate GPU Visual Effect Graph simulation. All Ignis flame graphs contain numerous random operators (12-29 occurrences in their serialized graphs). `startSeed` and fixed stepping constrain inputs but do not establish cross-device GPU determinism.
2. The graphs explicitly vary particle count by distance to the local main camera (`LODMaxDist`, `CullingDist`, and `AdditionalCameraPosition`). Trainees stand in different places, so they can intentionally receive different particle populations even with the same seed and fire state.
3. Graph variants use different update modes, while the adapter performs manual `VisualEffect.Simulate` calls. Different platforms/frame schedules and batching of several logical steps into one call can still produce different GPU results.
4. `NetworkVfxMaxBackfillSteps = 90` skips older VFX work after a hitch or late join beyond three seconds and then advances the logical tick marker. This protects Quest performance but makes exact reconstruction impossible in that case.
5. `GetPredictedAuthoritySpread` extrapolates beyond the last snapshot. Different receipt times mean clients predict over different horizons. It is mathematically informed, but it is not buffered authoritative interpolation.
6. `ApplyLocalExtinguishPrediction` deliberately shows the locally held tool's extinguish hole ahead of authority for up to 0.5 seconds. This is a direct, intentional visual disagreement.
7. Current model-change handlers apply many values immediately on receipt. Thus clients with different delivery delay display the same transition at different room times.
8. The logical spatial model itself is coarse: one ignition origin, one scalar spread radius, and one extinguish sphere. It cannot describe multiple disconnected burning/extinguished surface regions. More scalar state cannot recover spatial detail Ignis never exports.
9. Fire SFX starts with local `UnityEngine.Random` time and pitch. Shared fire lifecycle does not currently imply shared audio phase.

Instrumentation is required to distinguish items 5-7 from pure VFX-graph divergence. A useful comparison logs authoritative values, presented values, shared room time, snapshot time/age, buffer depth, local RTT, RTT variation, late-snapshot counts, ignition epoch, VFX tick, and the semantic VFX controls actually applied.

## 4. Latency-compensation design

Use `realtime.roomTime` as the presentation clock and keep simulation truth separate from rendered truth.

The authority should publish coherent timestamped presentation snapshots at a bounded rate. Each client, including the authority, buffers the same semantic snapshot stream and renders at `roomTime - presentationDelay`. Continuous values interpolate only between two known authoritative samples. If the next sample is missing, hold the last sample and count the underrun; do not extrapolate spread by default.

Discrete lifecycle values are step-sampled on that same timeline. A reliable transition epoch/timestamp should act as a delivery barrier for ignition, reignition, extinguish, burnout, reset, and later scenario/audio events. Locomotion, tracked poses, local spray particles, trigger response, and haptics remain immediate.

Normcore 3.5.1 exposes local RTT as `Realtime.ping`, and its `ConnectionStatistics` additionally exposes receive jitter. A single client cannot infer the room's slowest client from its own RTT. Therefore the first safe implementation uses one replicated/configured shared delay (initially 150 ms) plus diagnostics. Adaptive room delay should follow only after each avatar reports a slowly smoothed RTT/jitter estimate to a room timing coordinator. The coordinator can select a percentile/maximum one-way estimate plus margin, clamp it, raise it quickly when underruns occur, and lower it slowly. Varying a different delay independently on every client would defeat synchronization.

Existing code changes:

- Keep `FireStateModel` as canonical simulation/failover state; add a bounded-rate presentation snapshot and a shared delay/transition barrier rather than overloading logical fields.
- Replace puppet use of `GetPredictedAuthoritySpread` with buffered interpolation. Retain it only as a compatibility fallback until the buffer is proven.
- Route visual-only spread/extinguish/intensity controls through a presentation adapter on both authority and puppets, so the authority does not render its simulation 150 ms ahead of everyone else.
- Stop immediate visual mutation in model callbacks; callbacks enqueue data. Apply it from the shared presentation clock.
- Keep current reset epochs and ownership logic. They solve different problems.

## 5. Visual-sync design

The least invasive first boundary is a semantic Ignis presentation adapter: synchronize and buffer the controls Ignis already exposes (`Spread_center/radius`, `PutOutArea_center/radius`, intensity, lifecycle visibility), apply those controls at the same room time on all clients, and accept microscopic particle differences. This preserves Ignis as simulation backend and avoids networking particles.

This can meet the current meaningful requirement only while one radial spread region and one radial extinguish region are adequate. If two-client capture still shows materially different burning regions after the semantic controls match, deterministic Ignis should be considered exhausted—the history already includes its strongest practical seed/time/fixed-step approach.

The next prototype should be fixed surface cells, not a texture first. Deterministically generate a stable ordered set of cells from each configured fire collider/mesh, and replicate compact per-cell intensity (for example, 2-4 bits per cell plus an epoch/timestamp). Local VFX emit around active cells; particles may differ, but region membership cannot. A bit-packed delta/dirty-word scheme is substantially cheaper and easier to inspect than synchronizing a 32x32 texture. A UV mask is appropriate later for high-resolution surfaces, but requires stable UVs, GPU upload/update work, and delta encoding. Neither approach appears in repository history.

## 6. Staged migration plan

1. Add aggregate diagnostics and side-by-side state/presentation logging. Establish whether model values and room clocks match before blaming transport.
2. Add a timestamped bounded presentation snapshot, a shared 150 ms delay, authoritative interpolation, and scheduled lifecycle presentation. Preserve canonical model fields for failover.
3. Make local extinguish prediction opt-in and disabled for synchronized training evaluation; keep water stream/haptics immediate.
4. Capture two clients with equal semantic controls. Record residual differences attributable to local Ignis VFX, including camera distance and late/backfill events.
5. If semantic mismatch remains material, prototype fixed surface cells on one representative object/profile behind a feature flag. Measure bytes/sec, VFX cost, and agreement before generalizing.
6. Only after two-client validation remove obsolete extrapolation, deterministic reconstruction, or duplicate state fields. Do not remove ownership failover, reset barriers, or offline fallbacks without replacements.

Acceptance testing requires two clients and synchronized logs/screenshots. At selected room times, compare canonical state, buffered presented state, VFX control volumes, snapshot age/depth, RTT, and late counts. The pass criterion is equal meaningful burning/extinguished regions and transitions, not identical particle transforms.

## Implementation completed after the investigation

The first independently testable layers are now implemented without removing the established authority, failover, reset, or offline paths:

- `FireStateModel` carries a shared presentation delay, a reliable lifecycle-transition epoch/time barrier, and a compact latest-value semantic visual snapshot. The snapshot timestamp is written last as its coherence barrier.
- The fire authority publishes changed samples at no more than 10 Hz, with a 2 Hz keepalive only while a fire is active or has just become inactive. Dormant fires do not continuously write the model.
- Both clients evaluate `presentationTime = roomTime - sharedDelay`. Continuous spread, extinguish radius/center, and temperature interpolate only between authoritative samples. Lifecycle and ignition epoch remain step values.
- Reset/start transitions reuse their existing scheduled room time. Ignition, reignition, extinguish, and burnout create reliable transition samples and become visible on the delayed shared timeline.
- Puppet spread no longer uses extrapolation when presentation metadata is available. The older Simpson predictor remains solely as compatibility behavior for rooms created by an older build.
- Local extinguish deformation prediction is now opt-in and disabled by default. Local spray particles, tracking, trigger response, and haptics are unchanged.
- A visual-only Ignis adapter reapplies the presented spread center/radius, extinguish center/radius, temperature-derived intensity, fade multiplier, lifecycle visibility, light regions, and fire-audio mute boundary in `LateUpdate` on authority and puppets. It does not alter authority simulation facts.
- `[FireSync]` diagnostics report local RTT, smoothed RTT variation, Normcore receive jitter, configured and suggested delay, room/presentation time, buffer range, snapshot age, late-frame count, canonical versus presented state, VFX logical tick, and emitter count.

The implementation intentionally does not claim GPU-particle determinism. Its two-client test decides whether equal semantic masks are visually adequate. If not, the next change is the fixed-surface-cell prototype described in Stage 5; adding more scalar Ignis inputs is not the fallback.
