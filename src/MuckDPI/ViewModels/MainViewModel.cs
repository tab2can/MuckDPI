using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MuckDPI.Engine;
using MuckDPI.Services;

namespace MuckDPI.ViewModels;

public sealed class RelayCommand(Func<Task> execute, Func<bool>? can = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => can?.Invoke() ?? true;
    public async void Execute(object? parameter) => await execute();
    public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Dispatcher _ui;
    private DpiEngine? _engine;
    private bool _busy;
    private bool _serviceMode;
    private DispatcherTimer? _poll;
    private string _status = "";
    private string _ispLine = "";
    private string _strategyLine = "";
    private string _logText = "";
    private string _statsLine = "";
    private string _tuneProgress = "";
    private string? _error;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _ui = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        S = SettingsStore.Current;
        StartCommand = new RelayCommand(StartAsync, () => !IsRunning && !Busy);
        StopCommand = new RelayCommand(StopAsync, () => IsRunning && !Busy);
        TuneCommand = new RelayCommand(TuneAsync, () => !Busy);
        Status = Loc.T("Protection is off", "Koruma kapalı");
        IspLine = Loc.T("ISP not detected yet", "ISS henüz algılanmadı");
        StrategyLine = DisplayStrategy();
        if (WindowsIntegration.IsServiceRunning)
        {
            _serviceMode = true;
            EnsurePoll();
            SyncFromService();
        }
    }

    public AppSettings S { get; }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand TuneCommand { get; }

    public bool IsRunning => _engine?.IsRunning == true || (_serviceMode && WindowsIntegration.IsServiceRunning);
    public bool Busy
    {
        get => _busy;
        set => OnUi(() =>
        {
            _busy = value;
            OnChanged();
            StartCommand.Raise();
            StopCommand.Raise();
            TuneCommand.Raise();
        });
    }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string IspLine { get => _ispLine; set => Set(ref _ispLine, value); }
    public string StrategyLine { get => _strategyLine; set => Set(ref _strategyLine, value); }
    public string LogText { get => _logText; set => Set(ref _logText, value); }
    public string StatsLine { get => _statsLine; set => Set(ref _statsLine, value); }
    public string TuneProgress { get => _tuneProgress; set => Set(ref _tuneProgress, value); }
    public string? Error { get => _error; set => OnUi(() => { _error = value; OnChanged(nameof(Error)); }); }

    public bool MinimizeToTray
    {
        get => S.MinimizeToTray;
        set { S.MinimizeToTray = value; OnChanged(); SettingsStore.Save(); }
    }

    public bool WindowsIntegrate
    {
        get => S.WindowsIntegrate;
        set
        {
            S.WindowsIntegrate = value;
            S.AutoStartEngine = value;
            OnChanged();
            SettingsStore.Save();
            try
            {
                if (value) WindowsIntegration.Install();
                else
                {
                    WindowsIntegration.Uninstall();
                    _serviceMode = false;
                    RaiseRunning();
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                AppendLog(ex.Message);
            }
        }
    }

    public string AccentStatus => IsRunning
        ? Loc.T("Active", "Aktif")
        : Loc.T("Idle", "Kapalı");

    public async Task StartAsync()
    {
        Error = null;
        try
        {
            Busy = true;
            ApplyRuntimeDefaults();
            S.AutoStartEngine = true;
            Persist();
            if (S.WindowsIntegrate && WindowsIntegration.ServiceExeExists)
            {
                await Task.Run(StopEngine).ConfigureAwait(true);
                await Task.Run(() =>
                {
                    WindowsIntegration.Install();
                    WindowsIntegration.Start();
                }).ConfigureAwait(true);
                _serviceMode = true;
                EnsurePoll();
                Status = Loc.T("Protection is on (Windows service)", "Koruma açık (Windows servisi)");
                StrategyLine = DisplayStrategy();
                RaiseRunning();
                AppendLog("Windows servisi başlatıldı — bilgisayar açılınca koruma otomatik gelir.");
            }
            else
            {
                _serviceMode = false;
                await ApplyEngineAsync();
            }
            TrayService.ShowBalloon("MuckDPI", Loc.T(
                "Protection started. Open sites can retry on their own — no browser restart needed.",
                "Koruma başladı. Açık sekmeler kendiliğinden yenilenir, tarayıcıyı kapatmanıza gerek yok."));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = Loc.T("Failed to start", "Başlatılamadı");
            AppendLog(ex.Message);
        }
        finally { Busy = false; }
    }

    public async Task StopAsync()
    {
        Busy = true;
        try
        {
            await Task.Run(() =>
            {
                WindowsIntegration.Stop();
                StopEngine();
            });
            _serviceMode = false;
            Status = Loc.T("Protection is off", "Koruma kapalı");
            RaiseRunning();
        }
        finally { Busy = false; }
    }

    public async Task TuneAsync()
    {
        Busy = true;
        Error = null;
        try
        {
            ApplyRuntimeDefaults();
            TuneProgress = Loc.T("Detecting ISP…", "ISS algılanıyor…");
            try
            {
                var guess = await IspDetector.DetectAsync().ConfigureAwait(true);
                if (guess.ConfidenceHigh)
                {
                    S.IspId = guess.Id;
                    S.LastIspName = guess.Name;
                }
                IspLine = $"{guess.Name}  ·  {guess.Asn}  ·  {guess.PublicIp}";
                AppendLog($"ISS: {IspLine}");
            }
            catch (Exception ex)
            {
                AppendLog("ISS algılanamadı: " + ex.Message);
            }

            TuneProgress = Loc.T("Measuring line without protection…", "Koruma kapalıyken hat ölçülüyor…");
            await Task.Run(() =>
            {
                WindowsIntegration.Stop();
                StopEngine();
            }).ConfigureAwait(true);
            _serviceMode = false;
            RaiseRunning();
            await Task.Delay(400).ConfigureAwait(true);

            var baseline = await AutoTuner.ProbeManyAsync(TurkeyBlockedSites.ProbeUrls, CancellationToken.None).ConfigureAwait(true);
            var blocked = baseline.Where(o => !o.Ok).ToList();
            var okCount = baseline.Count(o => o.Ok);
            AppendLog($"Temel: {okCount}/{baseline.Count} site açıldı (koruma kapalı)");
            foreach (var o in baseline)
                AppendLog($"  {o.Url} → {(o.Ok ? "OK" : "FAIL")} {o.Detail}");

            var probeUrls = AutoTuner.FocusUrls(baseline);
            if (probeUrls.Count == 0)
            {
                TuneProgress = Loc.T(
                    "No blocked sites on this sample. Applying Turkey profile.",
                    "Bu örnekte yasaklı site görünmedi. Türkiye profili uygulanıyor.");
                S.StrategyId = S.IspId == "turk-telekom" ? "mode9" : "turkey";
                S.QuicMode = QuicMode.BlockAll;
                Persist();
                await ApplyEngineAsync().ConfigureAwait(true);
                await PromoteToServiceAsync().ConfigureAwait(true);
                return;
            }

            TuneProgress = Loc.T(
                $"DPI likely on {blocked.Count} sites. Testing methods…",
                $"{blocked.Count} sitede DPI izi var. Yöntemler deneniyor…");

            var tuner = new AutoTuner();
            var progress = new Progress<string>(s =>
                TuneProgress = Loc.T($"Testing {s}…", $"{s} deneniyor…"));
            var scores = await tuner.RunAsync(
                StrategyCatalog.TuneOrderFor(S.IspId),
                probeUrls,
                async (strategy, ct) =>
                {
                    S.StrategyId = strategy.Id;
                    S.QuicMode = QuicMode.BlockAll;
                    await ApplyEngineAsync(ct).ConfigureAwait(true);
                },
                progress,
                CancellationToken.None).ConfigureAwait(true);

            foreach (var s in scores)
            {
                AppendLog($"{s.Strategy.Id}: {s.Passed} ok / {s.Failed} fail  (ağırlık {s.Weight})");
                foreach (var o in s.Outcomes)
                    AppendLog($"  {o.Url} → {(o.Ok ? "OK" : "FAIL")} {o.Detail} ({o.ElapsedMs}ms)");
            }

            var ranked = scores.ToList();
            var best = ranked.FirstOrDefault();
            if (best is null || best.Passed == 0)
            {
                S.StrategyId = S.IspId == "turk-telekom" ? "mode9" : "turkey";
                S.QuicMode = QuicMode.BlockAll;
                Persist();
                await ApplyEngineAsync().ConfigureAwait(true);
                await PromoteToServiceAsync().ConfigureAwait(true);
                TuneProgress = Loc.T(
                    "Scan did not fully pass. Applied the TTNet/Turkey fallback.",
                    "Tarama tam geçmedi. TTNet/Türkiye yedeği uygulandı.");
                return;
            }

            best = PreferYoutubeIfNeeded(ranked, best);

            S.StrategyId = best.Strategy.Id;
            S.LastStrategyName = best.Strategy.Name;
            S.LastTuneUtc = DateTimeOffset.UtcNow;
            S.QuicMode = QuicMode.BlockAll;
            Persist();
            await ApplyEngineAsync().ConfigureAwait(true);
            await PromoteToServiceAsync().ConfigureAwait(true);

            TuneProgress = Loc.T(
                $"Applied {best.Strategy.Name} ({best.Passed}/{best.Passed + best.Failed}, weight {best.Weight}). QUIC dropped — browser can stay open.",
                $"Uygulandı: {best.Strategy.NameTr} ({best.Passed}/{best.Passed + best.Failed}, ağırlık {best.Weight}). HTTP/3 kapatıldı, tarayıcıyı kapatmanıza gerek yok.");
        }
        catch (Exception ex)
        {
            TuneProgress = ex.Message;
            Error = ex.Message;
            AppendLog(ex.Message);
        }
        finally { Busy = false; }
    }

    public void Persist()
    {
        SettingsStore.Save();
        StrategyLine = DisplayStrategy();
    }

    public async Task OnCloseAsync()
    {
        if (!S.WindowsIntegrate)
            await StopAsync();
        Persist();
    }

    private async Task PromoteToServiceAsync()
    {
        if (!S.WindowsIntegrate || !WindowsIntegration.ServiceExeExists)
            return;
        Persist();
        await Task.Run(StopEngine).ConfigureAwait(true);
        await Task.Run(() =>
        {
            WindowsIntegration.Install();
            WindowsIntegration.Start();
        }).ConfigureAwait(true);
        _serviceMode = true;
        EnsurePoll();
        Status = Loc.T("Protection is on (Windows service)", "Koruma açık (Windows servisi)");
        RaiseRunning();
        AppendLog("Windows servisine alındı — pencereyi kapatsanız da koruma açık kalır.");
    }

    private void EnsurePoll()
    {
        if (_poll is not null) return;
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => SyncFromService();
        _poll.Start();
    }

    private void SyncFromService()
    {
        var dto = SettingsIo.ReadStatus();
        var running = WindowsIntegration.IsServiceRunning && dto?.Running == true;
        if (dto is not null)
        {
            var text = Loc.T(
                $"seen {dto.PacketsSeen:N0} · desync {dto.PacketsDesynced:N0} · fake {dto.FakeSent:N0} · dns {dto.DnsRedirected:N0} · quic {dto.QuicBlocked:N0} · v6 {dto.Ipv6Dropped:N0}",
                $"görülen {dto.PacketsSeen:N0} · desync {dto.PacketsDesynced:N0} · sahte {dto.FakeSent:N0} · dns {dto.DnsRedirected:N0} · quic {dto.QuicBlocked:N0} · v6 {dto.Ipv6Dropped:N0}");
            StatsLine = text;
            if (dto.RecentLog.Count > 0)
            {
                var joined = string.Join(Environment.NewLine, dto.RecentLog);
                if (joined != _logText)
                {
                    _logText = joined;
                    OnChanged(nameof(LogText));
                }
            }
            if (running && !string.IsNullOrWhiteSpace(dto.Message))
                Status = dto.Message + " (Windows servisi)";
        }
        OnChanged(nameof(IsRunning));
        OnChanged(nameof(AccentStatus));
        StartCommand.Raise();
        StopCommand.Raise();
    }

    private static StrategyScore PreferYoutubeIfNeeded(List<StrategyScore> ranked, StrategyScore best)
    {
        if (!YoutubeStillDown(best)) return best;
        var mode9 = ranked.FirstOrDefault(s => s.Strategy.Id == "mode9");
        if (mode9 is null || YoutubeStillDown(mode9)) return best;
        var discordHeld = !mode9.Outcomes.Any(o => ProbeWeights.IsDiscord(o.Url))
            || mode9.Outcomes.Where(o => ProbeWeights.IsDiscord(o.Url)).Any(o => o.Ok);
        return discordHeld ? mode9 : best;
    }

    private static bool YoutubeStillDown(StrategyScore score) =>
        score.Outcomes.Any(o => ProbeWeights.IsYoutube(o.Url)) &&
        score.Outcomes.Where(o => ProbeWeights.IsYoutube(o.Url)).All(o => !o.Ok);

    private void ApplyRuntimeDefaults()
    {
        S.FilterMode = FilterMode.Global;
        S.EnableDnsProtect = true;
        S.EnablePassiveDrop = true;
        S.QuicMode = QuicMode.BlockAll;
        if (string.IsNullOrWhiteSpace(S.DnsProvider) || S.DnsProvider.Equals("off", StringComparison.OrdinalIgnoreCase))
            S.DnsProvider = "yandex";
    }

    private async Task ApplyEngineAsync(CancellationToken ct = default)
    {
        await Task.Run(StopEngine, ct).ConfigureAwait(true);
        var cfg = EngineConfig.From(S);
        var engine = new DpiEngine(cfg);
        WireEngine(engine);
        _engine = engine;
        await Task.Run(() => engine.Start(), ct).ConfigureAwait(true);
        Status = Loc.T("Protection is on", "Koruma açık");
        StrategyLine = DisplayStrategy();
        RaiseRunning();
    }

    private void WireEngine(DpiEngine engine)
    {
        engine.Log += (_, e) =>
        {
            var line = $"[{e.Timestamp:HH:mm:ss}] {e.Message}";
            _ui.BeginInvoke(() => AppendLog(line));
        };
        engine.HostLearned += (_, host) =>
        {
            if (!S.AutoHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                S.AutoHosts.Add(host);
                SettingsStore.Save();
            }
        };
        engine.StatsChanged += (_, _) =>
        {
            var st = engine.Stats;
            var text = Loc.T(
                $"seen {st.PacketsSeen:N0} · desync {st.PacketsDesynced:N0} · fake {st.FakeSent:N0} · dns {st.DnsRedirected:N0} · quic {st.QuicBlocked:N0} · v6 {st.Ipv6Dropped:N0}",
                $"görülen {st.PacketsSeen:N0} · desync {st.PacketsDesynced:N0} · sahte {st.FakeSent:N0} · dns {st.DnsRedirected:N0} · quic {st.QuicBlocked:N0} · v6 {st.Ipv6Dropped:N0}");
            _ui.BeginInvoke(() => StatsLine = text);
        };
    }

    private void StopEngine()
    {
        var engine = _engine;
        _engine = null;
        engine?.Stop();
        engine?.Dispose();
    }

    private void RaiseRunning()
    {
        OnUi(() =>
        {
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(AccentStatus));
            StartCommand.Raise();
            StopCommand.Raise();
        });
    }

    private string DisplayStrategy()
    {
        var isp = S.IspId is "auto" or "" ? Loc.T("Auto ISP", "Otomatik ISS") : IspCatalog.Get(S.IspId).Name;
        var st = S.StrategyId is "auto" or ""
            ? Loc.T("Turkey recommended", "Türkiye önerilen")
            : (Loc.Tr ? StrategyCatalog.Get(S.StrategyId).NameTr : StrategyCatalog.Get(S.StrategyId).Name);
        return $"{isp} · {st} · banka/devlet hariç tüm siteler";
    }

    private void AppendLog(string line)
    {
        OnUi(() =>
        {
            var next = string.IsNullOrEmpty(_logText) ? line : _logText + Environment.NewLine + line;
            if (next.Length > 40_000) next = next[^30_000..];
            _logText = next;
            OnChanged(nameof(LogText));
        });
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        var n = name;
        if (_ui.CheckAccess())
        {
            field = value;
            OnChanged(n);
            return;
        }
        var v = value;
        _ui.Invoke(() =>
        {
            switch (n)
            {
                case nameof(Status): _status = v; break;
                case nameof(IspLine): _ispLine = v; break;
                case nameof(StrategyLine): _strategyLine = v; break;
                case nameof(LogText): _logText = v; break;
                case nameof(StatsLine): _statsLine = v; break;
                case nameof(TuneProgress): _tuneProgress = v; break;
            }
            OnChanged(n);
        });
    }

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.Invoke(action);
    }

    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
