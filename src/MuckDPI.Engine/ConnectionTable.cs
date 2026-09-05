using System.Collections.Concurrent;
using System.Net;

namespace MuckDPI.Engine;

internal sealed class ConnectionTable
{
    private readonly ConcurrentDictionary<string, Conn> _map = new();
    private readonly int _firstPackets;

    public ConnectionTable(int firstPackets) => _firstPackets = Math.Max(2, firstPackets);

    public bool ShouldProcess(string key, int payloadLen, int? firstPackets = null)
    {
        if (payloadLen <= 0) return false;
        var conn = _map.GetOrAdd(key, _ => new Conn());
        var n = Interlocked.Increment(ref conn.DataPackets);
        conn.LastUtc = DateTime.UtcNow;
        var limit = Math.Max(2, firstPackets ?? _firstPackets);
        return n <= limit;
    }

    public void RememberHost(string key, string host) =>
        _map.AddOrUpdate(key, _ => new Conn { Host = host }, (_, c) => { c.Host = host; return c; });

    public string? HostOf(string key) => _map.TryGetValue(key, out var c) ? c.Host : null;

    public void Sweep()
    {
        var cut = DateTime.UtcNow.AddMinutes(-2);
        foreach (var kv in _map)
        {
            if (kv.Value.LastUtc < cut)
                _map.TryRemove(kv.Key, out _);
        }
    }

    public static string Key(IPAddress src, ushort sp, IPAddress dst, ushort dp) =>
        $"{src}:{sp}>{dst}:{dp}";

    private sealed class Conn
    {
        public int DataPackets;
        public DateTime LastUtc = DateTime.UtcNow;
        public string? Host;
    }
}
