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

    // --- Continuous progress: unreliable (frequent updates while being sprayed). ---

    // Normalized 0..1 extinguish progress (put-out radius relative to the
    // object's full-extinguish threshold computed from its size/toughness).
    [RealtimeProperty(4, false, true)]
    private float _extinguishProgress;

    // Local-space center of the put-out area so remote clients can show the
    // extinguish "hole" in the same place on the object.
    [RealtimeProperty(5, false, true)]
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
}
