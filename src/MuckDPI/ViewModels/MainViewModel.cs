using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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
    private DpiEngine? _engine;
    private string _page = "home";
    private bool _busy;
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
        S = SettingsStore.Current;
        StartCommand = new RelayCommand(StartAsync, () => !IsRunning && !Busy);
        StopCommand = new RelayCommand(StopAsync, () => IsRunning && !Busy);
        DetectCommand = new RelayCommand(DetectAsync, () => !Busy);
        TuneCommand = new RelayCommand(TuneAsync, () => !Busy);
        SaveCommand = new RelayCommand(() => { Persist(); return Task.CompletedTask; });
        ProbeOneCommand = new RelayCommand(ProbeCustomAsync, () => !Busy);
        ApplyTurkeyCommand = new RelayCommand(() => { ApplyTurkeyPreset(); return Task.CompletedTask; });
        Status = Loc.T("Protection is off", "Koruma kapalı");
        IspLine = Loc.T("ISP not detected yet", "ISS henüz algılanmadı");
        StrategyLine = DisplayStrategy();
        CustomHostsText = string.Join(Environment.NewLine, S.CustomHosts);
        ProbeUrl = "https://www.youtube.com/";
        foreach (var pack in ServiceCatalog.All)
        {
            Services.Add(new ServiceToggle
            {
                Id = pack.Id,
                Title = Loc.T(pack.Name, pack.NameTr),
                Enabled = S.EnabledServices.Contains(pack.Id, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    public AppSettings S { get; }
    public ObservableCollection<ServiceToggle> Services { get; } = [];
    public IReadOnlyList<Strategy> Strategies => StrategyCatalog.All;
    public IReadOnlyList<IspProfile> Isps => IspCatalog.All;

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand DetectCommand { get; }
    public RelayCommand TuneCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ProbeOneCommand { get; }
    public RelayCommand ApplyTurkeyCommand { get; }

    public string Page
    {
        get => _page;
        set { _page = value; OnChanged(); OnChanged(nameof(HomeVisible)); OnChanged(nameof(WizardVisible)); OnChanged(nameof(ServicesVisible)); OnChanged(nameof(DnsVisible)); OnChanged(nameof(ProbeVisible)); OnChanged(nameof(LogVisible)); OnChanged(nameof(SettingsVisible)); }
    }

    public Visibility HomeVisible => V("home");
    public Visibility WizardVisible => V("wizard");
    public Visibility ServicesVisible => V("services");
    public Visibility DnsVisible => V("dns");
    public Visibility ProbeVisible => V("probe");
    public Visibility LogVisible => V("log");
    public Visibility SettingsVisible => V("settings");
    private Visibility V(string id) => Page == id ? Visibility.Visible : Visibility.Collapsed;

    public bool IsRunning => _engine?.IsRunning == true;
    public bool Busy { get => _busy; set { _busy = value; OnChanged(); StartCommand.Raise(); StopCommand.Raise(); DetectCommand.Raise(); TuneCommand.Raise(); } }
    public string Status { get => _status; set { _status = value; OnChanged(); } }
    public string IspLine { get => _ispLine; set { _ispLine = value; OnChanged(); } }
    public string StrategyLine { get => _strategyLine; set { _strategyLine = value; OnChanged(); } }
    public string LogText { get => _logText; set { _logText = value; OnChanged(); } }
    public string StatsLine { get => _statsLine; set { _statsLine = value; OnChanged(); } }
    public string TuneProgress { get => _tuneProgress; set { _tuneProgress = value; OnChanged(); } }
    public string? Error { get => _error; set { _error = value; OnChanged(); } }
    public string CustomHostsText { get; set; }
    public string ProbeUrl { get; set; }
    public string ProbeResult { get => _probeResult; set { _probeResult = value; OnChanged(); } }
    private string _probeResult = "";

    public string SelectedIspId
    {
        get => S.IspId;
        set { S.IspId = value; OnChanged(); StrategyLine = DisplayStrategy(); }
    }

    public string SelectedStrategyId
    {
        get => S.StrategyId;
        set { S.StrategyId = value; OnChanged(); StrategyLine = DisplayStrategy(); }
    }

    public string SelectedDns
    {
        get => S.DnsProvider;
        set { S.DnsProvider = value; OnChanged(); }
    }

    public string SelectedFilter
    {
        get => S.FilterMode.ToString();
        set
        {
            if (Enum.TryParse<FilterMode>(value, out var m)) S.FilterMode = m;
            OnChanged();
        }
    }

    public string SelectedQuic
    {
        get => S.QuicMode.ToString();
        set
        {
            if (Enum.TryParse<QuicMode>(value, out var m)) S.QuicMode = m;
            OnChanged();
        }
    }

    public bool LanguageTr
    {
        get => Loc.Tr;
        set
        {
            Loc.Language = value ? "tr" : "en";
            S.Language = Loc.Language;
            OnChanged();
            OnChanged(nameof(PowerLabel));
            OnChanged(nameof(AccentStatus));
            StrategyLine = DisplayStrategy();
        }
    }

    public bool EnableDnsProtect
    {
        get => S.EnableDnsProtect;
        set { S.EnableDnsProtect = value; OnChanged(); }
    }

    public bool EnablePassiveDrop
    {
        get => S.EnablePassiveDrop;
        set { S.EnablePassiveDrop = value; OnChanged(); }
    }

    public bool MinimizeToTray
    {
        get => S.MinimizeToTray;
        set { S.MinimizeToTray = value; OnChanged(); }
    }

    public bool AutoStartEngine
    {
        get => S.AutoStartEngine;
        set { S.AutoStartEngine = value; OnChanged(); }
    }

    public string PowerLabel => IsRunning
        ? Loc.T("Stop protection", "Korumayı durdur")
        : Loc.T("Start protection", "Korumayı başlat");

    public string AccentStatus => IsRunning
        ? Loc.T("Active", "Aktif")
        : Loc.T("Idle", "Kapalı");

    public async Task StartAsync()
    {
        Error = null;
        try
        {
            Busy = true;
            PullUiIntoSettings();
            Persist();
            StopEngine();
            var cfg = EngineConfig.From(S);
            _engine = new DpiEngine(cfg);
            WireEngine(_engine);
            await Task.Run(() => _engine.Start());
            Status = Loc.T("Protection is on", "Koruma açık");
            StrategyLine = DisplayStrategy();
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(PowerLabel));
            OnChanged(nameof(AccentStatus));
            StartCommand.Raise();
            StopCommand.Raise();
            TrayService.ShowBalloon("MuckDPI", Loc.T("Protection started", "Koruma başladı"));
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
            await Task.Run(StopEngine);
            Status = Loc.T("Protection is off", "Koruma kapalı");
            OnChanged(nameof(IsRunning));
            OnChanged(nameof(PowerLabel));
            OnChanged(nameof(AccentStatus));
            StartCommand.Raise();
            StopCommand.Raise();
        }
        finally { Busy = false; }
    }

    public async Task DetectAsync()
    {
        Busy = true;
        TuneProgress = Loc.T("Detecting ISP…", "ISS algılanıyor…");
        try
        {
            var guess = await IspDetector.DetectAsync();
            if (guess.ConfidenceHigh)
            {
                S.IspId = guess.Id;
                S.LastIspName = guess.Name;
                if (S.StrategyId is "auto" or "")
                    S.StrategyId = "auto";
            }
            IspLine = $"{guess.Name}  ·  {guess.Asn}  ·  {guess.PublicIp}";
            var profile = IspCatalog.Get(guess.ConfidenceHigh ? guess.Id : "universal");
            TuneProgress = Loc.T(profile.NotesEn, profile.NotesTr);
            StrategyLine = DisplayStrategy();
            Persist();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            TuneProgress = ex.Message;
        }
        finally { Busy = false; }
    }

    public async Task TuneAsync()
    {
        Busy = true;
        try
        {
            if (!IsRunning) await StartAsync();
            var tuner = new AutoTuner();
            var progress = new Progress<string>(s => TuneProgress = Loc.T($"Testing {s}…", $"{s} deneniyor…"));
            var scores = await tuner.RunAsync(
                StrategyCatalog.TuneOrder,
                AutoTuner.DefaultUrls,
                async (strategy, ct) =>
                {
                    S.StrategyId = strategy.Id;
                    await Task.Run(() =>
                    {
                        StopEngine();
                    }, ct);
                    var cfg = EngineConfig.From(S);
                    _engine = new DpiEngine(cfg);
                    WireEngine(_engine);
                    await Task.Run(() => _engine.Start(), ct);
                    OnChanged(nameof(IsRunning));
                    OnChanged(nameof(PowerLabel));
                    OnChanged(nameof(AccentStatus));
                },
                progress,
                CancellationToken.None);

            var best = scores.FirstOrDefault();
            if (best is not null && best.Passed > 0)
            {
                S.StrategyId = best.Strategy.Id;
                S.LastStrategyName = best.Strategy.Name;
                S.LastTuneUtc = DateTimeOffset.UtcNow;
                Persist();
                await Task.Run(StopEngine);
                var cfg = EngineConfig.From(S);
                _engine = new DpiEngine(cfg);
                WireEngine(_engine);
                await Task.Run(() => _engine.Start());
                Status = Loc.T("Protection is on", "Koruma açık");
                StrategyLine = DisplayStrategy();
                OnChanged(nameof(IsRunning));
                OnChanged(nameof(PowerLabel));
                OnChanged(nameof(AccentStatus));
                TuneProgress = Loc.T(
                    $"Best: {best.Strategy.Name} ({best.Passed}/{best.Passed + best.Failed} sites).",
                    $"En iyisi: {best.Strategy.NameTr} ({best.Passed}/{best.Passed + best.Failed} site).");
            }
            else
            {
                TuneProgress = Loc.T(
                    "No strategy fully passed. Try another DNS provider or check that the site is not IP-blocked.",
                    "Hiçbir strateji tam geçmedi. Başka bir DNS deneyin veya sitenin IP seviyesinde engelli olup olmadığına bakın.");
            }

            foreach (var s in scores)
            {
                AppendLog($"{s.Strategy.Id}: {s.Passed} ok / {s.Failed} fail");
                foreach (var o in s.Outcomes)
                    AppendLog($"  {o.Url} → {(o.Ok ? "OK" : "FAIL")} {o.Detail} ({o.ElapsedMs}ms)");
            }
        }
        catch (Exception ex)
        {
            TuneProgress = ex.Message;
        }
        finally { Busy = false; }
    }

    public async Task ProbeCustomAsync()
    {
        Busy = true;
        try
        {
            var r = await AutoTuner.ProbeAsync(ProbeUrl, CancellationToken.None);
            ProbeResult = $"{(r.Ok ? "OK" : "FAIL")}  {r.Detail}  ({r.ElapsedMs} ms)";
        }
        finally { Busy = false; }
    }

    public void ApplyTurkeyPreset()
    {
        S.StrategyId = "turkey";
        S.FilterMode = FilterMode.Global;
        S.DnsProvider = "yandex";
        S.EnableDnsProtect = true;
        S.QuicMode = QuicMode.Off;
        OnChanged(nameof(SelectedStrategyId));
        OnChanged(nameof(SelectedFilter));
        OnChanged(nameof(SelectedDns));
        OnChanged(nameof(SelectedQuic));
        OnChanged(nameof(EnableDnsProtect));
        Persist();
        TuneProgress = Loc.T(
            "Applied Turkey profile: -5 + TTL 5 + Yandex 77.88.8.8:1253, all HTTPS except banks.",
            "Türkiye profili uygulandı: -5 + TTL 5 + Yandex 77.88.8.8:1253, banka hariç tüm HTTPS.");
    }

    public void Persist()
    {
        PullUiIntoSettings();
        SettingsStore.Save();
        StrategyLine = DisplayStrategy();
    }

    public async Task OnCloseAsync()
    {
        await StopAsync();
        Persist();
    }

    private void PullUiIntoSettings()
    {
        S.EnabledServices = Services.Where(x => x.Enabled).Select(x => x.Id).ToList();
        S.CustomHosts = CustomHostsText
            .Split(['\r', '\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SettingsStore.Current.Language = Loc.Language;
    }

    private void WireEngine(DpiEngine engine)
    {
        engine.Log += (_, e) =>
        {
            var line = $"[{e.Timestamp:HH:mm:ss}] {e.Message}";
            System.Windows.Application.Current?.Dispatcher.Invoke(() => AppendLog(line));
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
                $"seen {st.PacketsSeen:N0} · desync {st.PacketsDesynced:N0} · fake {st.FakeSent:N0} · dns-nat {st.DnsRedirected:N0} · doh {st.DnsRewritten:N0} · quic-drop {st.QuicBlocked:N0}",
                $"görülen {st.PacketsSeen:N0} · desync {st.PacketsDesynced:N0} · sahte {st.FakeSent:N0} · dns {st.DnsRedirected:N0} · doh {st.DnsRewritten:N0} · quic {st.QuicBlocked:N0}");
            System.Windows.Application.Current?.Dispatcher.Invoke(() => StatsLine = text);
        };
    }

    private void StopEngine()
    {
        _engine?.Stop();
        _engine?.Dispose();
        _engine = null;
    }

    private string DisplayStrategy()
    {
        var isp = S.IspId is "auto" or "" ? Loc.T("Auto ISP", "Otomatik ISS") : IspCatalog.Get(S.IspId).Name;
        var st = S.StrategyId is "auto" or ""
            ? Loc.T("Auto strategy", "Otomatik strateji")
            : (Loc.Tr ? StrategyCatalog.Get(S.StrategyId).NameTr : StrategyCatalog.Get(S.StrategyId).Name);
        return $"{isp} · {st}";
    }

    private void AppendLog(string line)
    {
        var next = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
        if (next.Length > 40_000) next = next[^30_000..];
        LogText = next;
    }

    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ServiceToggle : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    private bool _enabled;
    public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
