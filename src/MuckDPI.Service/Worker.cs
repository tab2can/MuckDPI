using MuckDPI.Engine;

namespace MuckDPI.Service;

public sealed class DpiWorker : BackgroundService
{
    private DpiEngine? _engine;
    private string _strategyId = "";
    private readonly List<string> _log = [];
    private readonly object _gate = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartEngine();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (SettingsIo.ConsumeReload())
                    StartEngine();
                WriteStatus();
                await Task.Delay(800, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        finally
        {
            StopEngine();
            WriteStatus();
        }
    }

    private void StartEngine()
    {
        StopEngine();
        var settings = SettingsIo.Load();
        settings.FilterMode = FilterMode.Global;
        settings.EnableDnsProtect = true;
        settings.EnablePassiveDrop = true;
        settings.QuicMode = QuicMode.BlockAll;
        if (string.IsNullOrWhiteSpace(settings.DnsProvider) ||
            settings.DnsProvider.Equals("off", StringComparison.OrdinalIgnoreCase))
            settings.DnsProvider = "yandex";
        SettingsIo.Save(settings);

        var engine = new DpiEngine(EngineConfig.From(settings));
        engine.Log += (_, e) =>
        {
            var line = $"[{e.Timestamp:HH:mm:ss}] {e.Message}";
            lock (_gate)
            {
                _log.Add(line);
                if (_log.Count > 250) _log.RemoveRange(0, _log.Count - 200);
            }
        };
        engine.Start();
        _engine = engine;
        _strategyId = settings.StrategyId;
    }

    private void StopEngine()
    {
        var engine = _engine;
        _engine = null;
        engine?.Stop();
        engine?.Dispose();
    }

    private void WriteStatus()
    {
        var engine = _engine;
        var st = engine?.Stats;
        List<string> log;
        lock (_gate) log = _log.Count == 0 ? [] : _log.ToList();
        SettingsIo.WriteStatus(new EngineStatusDto
        {
            Running = engine?.IsRunning == true,
            StrategyId = _strategyId,
            Message = engine?.IsRunning == true ? "Koruma açık" : "Koruma kapalı",
            PacketsSeen = st?.PacketsSeen ?? 0,
            PacketsDesynced = st?.PacketsDesynced ?? 0,
            FakeSent = st?.FakeSent ?? 0,
            DnsRedirected = st?.DnsRedirected ?? 0,
            QuicBlocked = st?.QuicBlocked ?? 0,
            Ipv6Dropped = st?.Ipv6Dropped ?? 0,
            RecentLog = log,
            Utc = DateTimeOffset.UtcNow
        });
    }
}
