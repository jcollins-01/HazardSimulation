using Normal.Realtime.Collections;

namespace Jingchen.Mic
{
    // The dictionary performs server-confirmed compare-and-swap transactions.
    // Records are replaced as a whole, never edited after insertion.
    [RealtimeModel]
    public partial class AudioChannelRoomModel
    {
        [RealtimeProperty(1, true)]
        private StringKeyDictionary<AudioChannelRecordModel> _records;
    }

    [RealtimeModel]
    public partial class AudioChannelRecordModel
    {
        [RealtimeProperty(1, true)] private int _clientId = -1;
        [RealtimeProperty(2, true)] private int _channel = -1;
        [RealtimeProperty(3, true)] private string _nonce = "";
        [RealtimeProperty(4, true)] private double _expiresAt;
        [RealtimeProperty(5, true)] private uint _nextJoin;
    }
}
