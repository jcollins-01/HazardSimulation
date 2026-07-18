using Normal.Realtime;

/// <summary>
/// Normcore datastore model for a spray-capable tool (hose / fire extinguisher).
/// Only the current owner of the tool writes this state; every client reads it
/// to play/stop the local (non-networked) particle simulation.
/// </summary>
// Tool ownership is transferred when a player grabs it. This model therefore
// needs Normcore's ownership meta-model too; RealtimeView ownership alone does
// not make this custom component model ownable.
[RealtimeModel(createMetaModel: true)]
public partial class SpraySyncModel
{
    // PropertyID 1, reliable, with change event.
    // Reliable is correct here: this flips rarely (on trigger pull/release/drop)
    // and must never be lost (otherwise a client could be stuck seeing spray).
    [RealtimeProperty(1, true, true)]
    private bool _isSpraying;
}
