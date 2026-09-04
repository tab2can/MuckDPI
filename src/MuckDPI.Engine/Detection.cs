using System.Text.Json;
using System.Text.RegularExpressions;

namespace MuckDPI.Engine;

public static class IspDetector
{
    public static async Task<IspGuess> DetectAsync(CancellationToken ct = default)
    {
        foreach (var probe in new[] { TryIpApi, TryIpInfoIo, TryIpApiCo })
        {
            try
            {
                var guess = await probe(ct).ConfigureAwait(false);
                if (guess is not null)
                    return guess;
            }
            catch
            {
                // next endpoint
            }
        }
        return new IspGuess { Id = "unknown", Name = "Unknown", ConfidenceHigh = false };
    }

    private static async Task<IspGuess?> TryIpApi(CancellationToken ct)
    {
        using var http = NewClient();
        using var doc = await JsonDocument.ParseAsync(
            await http.GetStreamAsync("http://ip-api.com/json/?fields=status,isp,org,as,query,country", ct),
            cancellationToken: ct).ConfigureAwait(false);
        var r = doc.RootElement;
        if (r.GetProperty("status").GetString() != "success") return null;
        return Finish(
            r.GetProperty("as").GetString(),
            r.GetProperty("isp").GetString(),
            r.GetProperty("org").GetString(),
            r.GetProperty("query").GetString(),
            r.GetProperty("country").GetString());
    }

    private static async Task<IspGuess?> TryIpInfoIo(CancellationToken ct)
    {
        using var http = NewClient();
        using var doc = await JsonDocument.ParseAsync(
            await http.GetStreamAsync("https://ipinfo.io/json", ct), cancellationToken: ct).ConfigureAwait(false);
        var r = doc.RootElement;
        return Finish(
            r.TryGetProperty("org", out var org) ? org.GetString() : null,
            r.TryGetProperty("org", out var org2) ? org2.GetString() : null,
            r.TryGetProperty("org", out var org3) ? org3.GetString() : null,
            r.TryGetProperty("ip", out var ip) ? ip.GetString() : null,
            r.TryGetProperty("country", out var c) ? c.GetString() : null);
    }

    private static async Task<IspGuess?> TryIpApiCo(CancellationToken ct)
    {
        using var http = NewClient();
        using var doc = await JsonDocument.ParseAsync(
            await http.GetStreamAsync("https://ipapi.co/json/", ct), cancellationToken: ct).ConfigureAwait(false);
        var r = doc.RootElement;
        var asn = r.TryGetProperty("asn", out var a) ? a.GetString() : null;
        return Finish(
            asn,
            r.TryGetProperty("org", out var org) ? org.GetString() : null,
            r.TryGetProperty("org", out var org2) ? org2.GetString() : null,
            r.TryGetProperty("ip", out var ip) ? ip.GetString() : null,
            r.TryGetProperty("country_name", out var c) ? c.GetString() : null);
    }

    private static IspGuess Finish(string? asnRaw, string? isp, string? org, string? ip, string? country)
    {
        var asn = ParseAsn(asnRaw) ?? ParseAsn(org) ?? ParseAsn(isp);
        var profile = IspCatalog.Match(asn, string.Join(' ', new[] { asnRaw, isp, org }.Where(s => !string.IsNullOrWhiteSpace(s))));
        return new IspGuess
        {
            Id = profile?.Id ?? "unknown",
            Name = profile?.Name ?? isp ?? org ?? "Unknown",
            Asn = asn is null ? asnRaw : $"AS{asn}",
            Org = org ?? isp,
            PublicIp = ip,
            Country = country,
            ConfidenceHigh = profile is not null
        };
    }

    private static int? ParseAsn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"AS(\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
        if (int.TryParse(text.Trim(), out var plain)) return plain;
        return null;
    }

    private static HttpClient NewClient() => new() { Timeout = TimeSpan.FromSeconds(5) };
}

public sealed class ProbeOutcome
{
    public required string Url { get; init; }
    public bool Ok { get; init; }
    public string Detail { get; init; } = "";
    public int ElapsedMs { get; init; }
}

public sealed class StrategyScore
{
    public required Strategy Strategy { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public List<ProbeOutcome> Outcomes { get; init; } = [];
    public double Ratio => Passed + Failed == 0 ? 0 : (double)Passed / (Passed + Failed);
}

public sealed class AutoTuner
{
    public static IReadOnlyList<string> DefaultUrls { get; } =
    [
        "https://www.youtube.com/",
        "https://discord.com/api/v9/experiments",
        "https://www.instagram.com/",
        "https://x.com/",
        "https://open.spotify.com/"
    ];

    public async Task<IReadOnlyList<StrategyScore>> RunAsync(
        IEnumerable<string> strategyIds,
        IReadOnlyList<string> urls,
        Func<Strategy, CancellationToken, Task> applyAsync,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var scores = new List<StrategyScore>();
        foreach (var id in strategyIds)
        {
            ct.ThrowIfCancellationRequested();
            var strategy = StrategyCatalog.Get(id);
            progress?.Report(strategy.NameTr);
            await applyAsync(strategy, ct).ConfigureAwait(false);
            await Task.Delay(400, ct).ConfigureAwait(false);
            var outcomes = new List<ProbeOutcome>();
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                outcomes.Add(await ProbeAsync(url, ct).ConfigureAwait(false));
            }
            scores.Add(new StrategyScore
            {
                Strategy = strategy,
                Passed = outcomes.Count(o => o.Ok),
                Failed = outcomes.Count(o => !o.Ok),
                Outcomes = outcomes
            });
        }
        return scores.OrderByDescending(s => s.Ratio).ThenByDescending(s => s.Passed).ToList();
    }

    public static async Task<ProbeOutcome> ProbeAsync(string url, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(6)
            })
            { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MuckDPI/1.0");
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();
            var ok = (int)resp.StatusCode is >= 200 and < 500;
            return new ProbeOutcome
            {
                Url = url,
                Ok = ok,
                Detail = $"{(int)resp.StatusCode} {resp.ReasonPhrase}",
                ElapsedMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeOutcome
            {
                Url = url,
                Ok = false,
                Detail = ex.GetBaseException().Message,
                ElapsedMs = (int)sw.ElapsedMilliseconds
            };
        }
    }
}
