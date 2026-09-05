using System.Collections.Concurrent;

namespace MuckDPI.Engine;

internal sealed class HostLearner
{
    private readonly ConcurrentDictionary<string, Track> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hard = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hardLock = new();
    private readonly Strategy _hardStrategy = StrategyCatalog.Get("aggressive");

    public HostLearner(IEnumerable<string>? saved)
    {
        if (saved is null) return;
        foreach (var h in saved)
        {
            var n = Norm(h);
            if (n.Length > 0) _hard.Add(n);
        }
    }

    public Strategy For(string? host, Strategy fallback)
    {
        if (string.IsNullOrWhiteSpace(host)) return fallback;
        lock (_hardLock)
            return _hard.Contains(Norm(host)) ? _hardStrategy : fallback;
    }

    public bool NoteHello(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var key = Norm(host);
        var t = _tracks.GetOrAdd(key, _ => new Track());
        lock (t)
        {
            var now = DateTime.UtcNow;
            t.Hellos.Add(now);
            t.Hellos.RemoveAll(x => x < now.AddSeconds(-18));
            if (t.Hellos.Count >= 3)
                return Escalate(key);
        }
        return false;
    }

    public bool NoteRst(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var key = Norm(host);
        var t = _tracks.GetOrAdd(key, _ => new Track());
        lock (t)
        {
            var now = DateTime.UtcNow;
            t.Rsts.Add(now);
            t.Rsts.RemoveAll(x => x < now.AddSeconds(-18));
            if (t.Rsts.Count >= 2 && t.Hellos.Count >= 1)
                return Escalate(key);
        }
        return false;
    }

    private bool Escalate(string host)
    {
        lock (_hardLock)
        {
            if (!_hard.Add(host)) return false;
        }
        SettingsIo.RememberHardHost(host);
        return true;
    }

    private static string Norm(string host) =>
        host.Trim().TrimEnd('.').ToLowerInvariant().TrimStart('*', '.');

    private sealed class Track
    {
        public List<DateTime> Hellos { get; } = [];
        public List<DateTime> Rsts { get; } = [];
    }
}
