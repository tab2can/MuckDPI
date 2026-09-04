using System.Collections.Concurrent;
using MuckDPI.Engine.Packet;

namespace MuckDPI.Engine;

internal sealed class TtlTracker
{
    private readonly ConcurrentDictionary<string, byte> _last = new();

    public void ObserveInbound(in ParsedPacket pkt)
    {
        if (pkt.Ttl == 0) return;
        _last[pkt.SrcIp.ToString()] = pkt.Ttl;
    }

    public byte FakeFor(string destIp, byte fallback)
    {
        if (!_last.TryGetValue(destIp, out var ttl))
            return fallback == 0 ? (byte)5 : fallback;
        var hop = ttl > 128 ? 255 - ttl : ttl > 64 ? 128 - ttl : 64 - ttl;
        var fake = hop - 1;
        if (fake < 1) fake = 1;
        if (fake > 10) fake = 10;
        return (byte)fake;
    }
}
