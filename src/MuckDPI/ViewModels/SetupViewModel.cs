using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MuckDPI.Engine;
using MuckDPI.Services;

namespace MuckDPI.ViewModels;

public sealed class SetupViewModel : INotifyPropertyChanged
{
    private readonly Dispatcher _ui;
    private DpiEngine? _engine;
    private string _headline = "Hazırlanıyor…";
    private string _detail = "";
    private double _percent;
    private bool _busy = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Finished;

    public SetupViewModel()
    {
        _ui = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        S = SettingsStore.Current;
    }

    public AppSettings S { get; }
    public bool Busy { get => _busy; private set { _busy = value; OnChanged(); } }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }

    public async Task RunAsync()
    {
        Busy = true;
        try
        {
            ApplyDefaults();
            Headline = "ISS algılanıyor";
            Detail = "Hattınız tanınıyor…";
            Percent = 8;
            try
            {
                var guess = await IspDetector.DetectAsync().ConfigureAwait(true);
                if (guess.ConfidenceHigh)
                {
                    S.IspId = guess.Id;
                    S.LastIspName = guess.Name;
                }
                Detail = guess.Name;
            }
            catch
            {
                Detail = "ISS okunamadı, genel profil kullanılacak";
            }

            Headline = "Engel ölçülüyor";
            Detail = "Yasaklı siteler kontrol ediliyor…";
            Percent = 18;
            await Task.Run(() =>
            {
                WindowsIntegration.Stop();
                StopEngine();
            }).ConfigureAwait(true);
            await Task.Delay(400).ConfigureAwait(true);

            var baseline = await AutoTuner.ProbeManyAsync(TurkeyBlockedSites.ProbeUrls, CancellationToken.None)
                .ConfigureAwait(true);
            var blocked = baseline.Count(o => !o.Ok);
            var probeUrls = AutoTuner.FocusUrls(baseline);
            Percent = 32;

            if (probeUrls.Count == 0)
            {
                Headline = "Koruma kuruluyor";
                Detail = blocked == 0 ? "Bu hatta ek engel yok, Türkiye profili" : "Varsayılan profil";
                S.StrategyId = S.IspId == "turk-telekom" ? "mode9" : "turkey";
            }
            else
            {
                Headline = "Yöntemler deneniyor";
                Detail = $"{blocked} sitede engel var";
                var ids = StrategyCatalog.TuneOrderFor(S.IspId);
                var n = 0;
                var tuner = new AutoTuner();
                var progress = new Progress<string>(name =>
                {
                    n++;
                    Percent = 32 + n * 50.0 / Math.Max(ids.Count, 1);
                    Detail = name;
                });
                var scores = await tuner.RunAsync(
                    ids,
                    probeUrls,
                    async (strategy, ct) =>
                    {
                        S.StrategyId = strategy.Id;
                        S.QuicMode = QuicMode.BlockAll;
                        await ApplyEngineAsync(ct).ConfigureAwait(true);
                    },
                    progress,
                    CancellationToken.None).ConfigureAwait(true);

                var ranked = scores.ToList();
                var best = ranked.FirstOrDefault();
                if (best is null || best.Passed == 0)
                    S.StrategyId = S.IspId == "turk-telekom" ? "mode9" : "turkey";
                else
                {
                    best = PreferYoutubeIfNeeded(ranked, best);
                    S.StrategyId = best.Strategy.Id;
                    S.LastStrategyName = best.Strategy.NameTr;
                    Detail = best.Strategy.NameTr;
                }
            }

            Headline = "Koruma kuruluyor";
            Percent = 90;
            S.QuicMode = QuicMode.BlockAll;
            S.WindowsIntegrate = true;
            S.AutoStartEngine = true;
            S.TuneCompleted = true;
            S.LastTuneUtc = DateTimeOffset.UtcNow;
            SettingsStore.Save();
            await ApplyEngineAsync().ConfigureAwait(true);
            await PromoteToServiceAsync().ConfigureAwait(true);
            Percent = 100;
            Headline = "Hazır";
            Detail = "Arka planda çalışıyor";
            await Task.Delay(1200).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Headline = "Kurulum tamamlanamadı";
            Detail = ex.Message;
            Busy = false;
            return;
        }

        Busy = false;
        Finished?.Invoke();
    }

    private void ApplyDefaults()
    {
        S.FilterMode = FilterMode.Global;
        S.EnableDnsProtect = true;
        S.EnablePassiveDrop = true;
        S.QuicMode = QuicMode.BlockAll;
        S.WindowsIntegrate = true;
        S.AutoStartEngine = true;
        if (string.IsNullOrWhiteSpace(S.DnsProvider) || S.DnsProvider.Equals("off", StringComparison.OrdinalIgnoreCase))
            S.DnsProvider = "yandex";
    }

    private async Task ApplyEngineAsync(CancellationToken ct = default)
    {
        await Task.Run(StopEngine, ct).ConfigureAwait(true);
        var engine = new DpiEngine(EngineConfig.From(S));
        _engine = engine;
        await Task.Run(() => engine.Start(), ct).ConfigureAwait(true);
    }

    private async Task PromoteToServiceAsync()
    {
        SettingsStore.Save();
        await Task.Run(StopEngine).ConfigureAwait(true);
        if (!WindowsIntegration.ServiceExeExists)
            await ApplyEngineAsync().ConfigureAwait(true);
        else
        {
            await Task.Run(() =>
            {
                WindowsIntegration.Install();
                WindowsIntegration.Start();
            }).ConfigureAwait(true);
        }
    }

    private void StopEngine()
    {
        var engine = _engine;
        _engine = null;
        engine?.Stop();
        engine?.Dispose();
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

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        var n = name;
        var v = value;
        _ui.Invoke(() =>
        {
            switch (n)
            {
                case nameof(Headline): _headline = v; break;
                case nameof(Detail): _detail = v; break;
            }
            OnChanged(n);
        });
    }

    private void Set(ref double field, double value, [CallerMemberName] string? name = null)
    {
        var v = value;
        _ui.Invoke(() =>
        {
            _percent = v;
            OnChanged(nameof(Percent));
        });
    }

    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
