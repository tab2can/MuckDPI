namespace MuckDPI.Engine;

public enum FilterMode
{
    Smart,
    Hostlist,
    Global
}

public enum QuicMode
{
    Off,
    BlockHostlist,
    FakeHostlist,
    BlockAll
}

public enum DnsProviderKind
{
    Cloudflare,
    Google,
    Quad9,
    AdGuard,
    Mullvad,
    Off
}

public sealed class AppSettings
{
    public string Language { get; set; } = "tr";
    public string StrategyId { get; set; } = "auto";
    public string IspId { get; set; } = "auto";
    public string DnsProvider { get; set; } = "cloudflare";
    public bool EnableDnsProtect { get; set; } = true;
    public bool EnablePassiveDrop { get; set; } = true;
    public FilterMode FilterMode { get; set; } = FilterMode.Smart;
    public QuicMode QuicMode { get; set; } = QuicMode.BlockHostlist;
    public List<string> EnabledServices { get; set; } = new(ServiceCatalog.DefaultEnabled);
    public List<string> CustomHosts { get; set; } = [];
    public List<string> AutoHosts { get; set; } = [];
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartEngine { get; set; }
    public bool AutoTuneOnStart { get; set; }
    public string? LastIspName { get; set; }
    public string? LastStrategyName { get; set; }
    public DateTimeOffset? LastTuneUtc { get; set; }
}

public sealed class Strategy
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string NameTr { get; init; }
    public required string Description { get; init; }
    public required string DescriptionTr { get; init; }
    public bool SplitAtSni { get; init; } = true;
    public int SplitPos { get; init; } = 2;
    public bool ReverseFragments { get; init; }
    public bool SendFake { get; init; }
    public byte FakeTtl { get; init; } = 3;
    public bool FakeWrongSeq { get; init; }
    public bool FakeWrongChecksum { get; init; }
    public bool FakeObfuscateSni { get; init; } = true;
    public int FakeRepeats { get; init; } = 1;
    public bool HttpObfuscate { get; init; }
    public int MaxPayload { get; init; } = 1400;
    public int FirstPackets { get; init; } = 8;
}

public sealed class IspProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int[] Asns { get; init; }
    public required string[] Keywords { get; init; }
    public required string DefaultStrategyId { get; init; }
    public required string NotesTr { get; init; }
    public required string NotesEn { get; init; }
    public bool DnsPoisonLikely { get; init; } = true;
}

public sealed class ServicePack
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string NameTr { get; init; }
    public required string[] Hosts { get; init; }
    public required string[] ProbeUrls { get; init; }
    public bool BlockQuic { get; init; }
}

public sealed class EngineStats
{
    public long PacketsSeen;
    public long PacketsDesynced;
    public long FakeSent;
    public long DnsRewritten;
    public long PassiveDropped;
    public long QuicBlocked;
    public long AutoLearned;
}

public sealed class LogEventArgs : EventArgs
{
    public required DateTime Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
}

public sealed class IspGuess
{
    public string Id { get; init; } = "unknown";
    public string Name { get; init; } = "Unknown";
    public string? Asn { get; init; }
    public string? Org { get; init; }
    public string? PublicIp { get; init; }
    public string? Country { get; init; }
    public bool ConfidenceHigh { get; init; }
}
