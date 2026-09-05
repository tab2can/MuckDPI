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
    Yandex,
    Cloudflare,
    Google,
    Quad9,
    AdGuard,
    Mullvad,
    DohCloudflare,
    DohGoogle,
    Off
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 5;
    public string Language { get; set; } = "tr";
    public string StrategyId { get; set; } = "turkey";
    public string IspId { get; set; } = "auto";
    public string DnsProvider { get; set; } = "yandex";
    public bool EnableDnsProtect { get; set; } = true;
    public bool EnablePassiveDrop { get; set; } = true;
    public FilterMode FilterMode { get; set; } = FilterMode.Global;
    public QuicMode QuicMode { get; set; } = QuicMode.BlockAll;
    public List<string> EnabledServices { get; set; } = new(ServiceCatalog.DefaultEnabled);
    public List<string> CustomHosts { get; set; } = [];
    public List<string> AutoHosts { get; set; } = [];
    public List<string> LearnedHardHosts { get; set; } = [];
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartEngine { get; set; } = true;
    public bool WindowsIntegrate { get; set; } = true;
    public bool AutoTuneOnStart { get; set; }
    public bool TuneCompleted { get; set; }
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
    public bool AutoTtl { get; init; }
    public int FakeRepeats { get; init; } = 1;
    public bool HttpObfuscate { get; init; }
    public int MaxPayload { get; init; } = 1200;
    public int FirstPackets { get; init; } = 8;
    public bool BlockQuic { get; init; }
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
    public long DnsRedirected;
    public long PassiveDropped;
    public long QuicBlocked;
    public long Ipv6Dropped;
    public long AutoLearned;
}

public sealed class EngineStatusDto
{
    public bool Running { get; set; }
    public string StrategyId { get; set; } = "";
    public string Message { get; set; } = "";
    public long PacketsSeen { get; set; }
    public long PacketsDesynced { get; set; }
    public long FakeSent { get; set; }
    public long DnsRedirected { get; set; }
    public long QuicBlocked { get; set; }
    public long Ipv6Dropped { get; set; }
    public List<string> RecentLog { get; set; } = [];
    public DateTimeOffset Utc { get; set; }
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
