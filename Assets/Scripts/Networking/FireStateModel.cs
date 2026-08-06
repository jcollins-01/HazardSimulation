using Normal.Realtime;
using UnityEngine;

/// <summary>
/// Normcore datastore model for one fire / hazard object (an Ignis FlammableObject).
/// Exactly one stable authority (the owner of this component's model) runs the real
/// Ignis simulation and writes these values. All other clients mirror their local
/// Ignis visuals from this state.
/// </summary>
// Fire authority is transferred between clients, so Normcore must generate the
// ownership meta-model in addition to the property datastore model.
[RealtimeModel(createMetaModel: true)]
public partial class FireStateModel
{
    // --- Permanent state flags: reliable so they are never lost and arrive in order. ---

    // The object is currently burning (mirrors Ignis FlammableObject.onFire).
    [RealtimeProperty(1, true, true)]
    private bool _isBurning;

    // The fire was put out by spraying (mirrors Ignis FlammableObject.IsExtinguished()).
    [RealtimeProperty(2, true, true)]
    private bool _isExtinguished;

    // The fire burned out on its own (mirrors Ignis FlammableObject.hasBurnedOut()).
    [RealtimeProperty(3, true, true)]
    private bool _isBurnedOut;

    // --- Continuous progress: latest-value delivery, rate-limited by NetworkedFireState. ---

    // Normalized 0..1 extinguish progress (put-out radius relative to the
    // object's full-extinguish threshold computed from its size/toughness).
    [RealtimeProperty(4, RealtimePropertyType.Unreliable, true)]
    private float _extinguishProgress;

    // Local-space center of the put-out area so remote clients can show the
    // extinguish "hole" in the same place on the object.
    [RealtimeProperty(5, RealtimePropertyType.Unreliable, true)]
    private Vector3 _putOutCenterLocal;

    // Whether this object should glow hot on the thermal camera (mirrors the
    // authority-side HazardTemperature.Ignite() from the hazard spread logic).
    [RealtimeProperty(6, true, true)]
    private bool _heatVisual;

    // Normcore room time at which Ignis entered its post-extinguish burnout fade.
    // Every client derives fade progress from the same server clock, so message
    // latency and local frame rate cannot shift the logical completion time.
    [RealtimeProperty(7, true, true)]
    private double _extinguishFadeStartRoomTime;

    // The authority's Ignis burnOutLength_s for this extinguish cycle. This makes
    // the authority's configured duration canonical even if scene copies differ.
    [RealtimeProperty(8, true, true)]
    private float _extinguishFadeDuration;

    // --- Fire profile runtime state. ---

    // FireProfileController uses temperature as fire health and to scale its
    // flame visuals. This is a continuous visual value, so newer samples supersede
    // older samples instead of waiting behind reliable retransmissions.
    [RealtimeProperty(9, RealtimePropertyType.UnreliableRedundant, true)]
    private float _profileTemperature;

    // Smolder state is discrete gameplay state. These values are reliable so a
    // replacement authority continues the same decision instead of rolling again.
    [RealtimeProperty(10, true, true)]
    private bool _profileReadyForSmolder;

    [RealtimeProperty(11, true, true)]
    private bool _profileHasRolledSmolder;

    [RealtimeProperty(12, true, true)]
    private bool _profileWillReignite;

    // Room timestamps make authority handoff preserve the regeneration delay and
    // the already-selected reignition deadline instead of restarting either timer.
    [RealtimeProperty(13, RealtimePropertyType.UnreliableRedundant, true)]
    private double _profileLastWaterHitRoomTime;

    [RealtimeProperty(14, true, true)]
    private double _profileReigniteAtRoomTime;

    // --- Synchronized restart when a teammate joins. ---

    // Each active fire authority advances this epoch once for a new join. Clients
    // reset at resetAtRoomTime and wait for resetCompletedEpoch before consuming
    // the authority's post-reset state.
    // Timestamp and intent use lower property IDs so they deserialize before the
    // epoch that makes a newly scheduled reset visible to clients.
    [RealtimeProperty(15, true, true)]
    private double _resetAtRoomTime;

    [RealtimeProperty(16, true, true)]
    private bool _resetShouldBurn;

    [RealtimeProperty(17, true, true)]
    private int _resetEpoch;

    [RealtimeProperty(18, true, true)]
    private int _resetCompletedEpoch;

    // --- Deterministic fire visuals. ---

    // Reliable one-shot metadata for an ignition cycle. Every client derives the
    // same fire age from Normcore's synchronized room clock and starts the VFX with
    // the same random seed. ignitionEpoch is written last and acts as the barrier.
    [RealtimeProperty(19, true, true)]
    private double _ignitionStartRoomTime;

    [RealtimeProperty(20, true, true)]
    private int _vfxSeed;

    [RealtimeProperty(21, true, true)]
    private int _ignitionEpoch;

    // Fire spread changes slowly and is visual-only on puppets. Send the newest
    // authority sample without allowing lost packets to delay lifecycle events.
    [RealtimeProperty(22, RealtimePropertyType.UnreliableRedundant, true)]
    private float _fireSpread;

    [RealtimeProperty(23, RealtimePropertyType.UnreliableRedundant, true)]
    private double _fireSpreadSampleRoomTime;
}
