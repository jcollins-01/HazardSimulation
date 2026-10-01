using Normal.Realtime;

/// <summary>
/// Normcore datastore model for one avatar's finger curl state.
/// The owning (local) client writes it; every other client reads it to pose the
/// avatar's finger bones.
///
/// Each hand is packed into a single int to keep bandwidth tiny:
///   bits  0..29  five 6-bit curl values (thumb, index, middle, ring, little), 0 = open, 63 = fist
///   bit   30     hand is currently tracked (hand tracking active for that hand)
/// </summary>
// No meta-model: this lives on the avatar root RealtimeView, which RealtimeAvatarManager
// already owns for the local player, so ownership is never transferred.
[RealtimeModel]
public partial class HandPoseModel
{
    // Unreliable: finger poses are continuous and only the newest value matters.
    [RealtimeProperty(1, RealtimePropertyType.Unreliable, true)]
    private int _leftHand;

    [RealtimeProperty(2, RealtimePropertyType.Unreliable, true)]
    private int _rightHand;
}
