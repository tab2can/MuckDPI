using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

public static class TurkeyBlockedSites
{
    public static IReadOnlyList<string> ProbeUrls { get; } =
    [
        "https://www.ztod.com/",
        "https://discord.com/",
        "https://discord.com/api/v9/experiments",
        "https://cdn.discordapp.com/",
        "https://www.4shared.com/",
        "https://www.anabolic.com/",
        "https://www.brazzers.com/",
        "https://onlyfans.com/",
        "https://pastebin.com/",
        "https://www.pornhub.com/",
        "https://www.roblox.com/",
        "http://shanesworld.com/",
        "https://www.tango.me/",
        "https://www.wattpad.com/",
        "https://www.wattpad.com/home",
        "https://www.wikileaks.org/",
        "https://www.xvideos.com/",
        "https://www.xnxx.com/",
        "https://www.redtube.com/",
        "https://www.youporn.com/",
        "https://bangbros.com/",
        "https://www.realitykings.com/",
        "https://fansly.com/",
        "https://www.voaturkce.com/",
        "https://www.bet365.com/",
        "https://www.bwin.com/",
        "https://www.pinnacle.com/",
        "https://betsson.com/",
        "https://www.youtube.com/",
        "https://i.ytimg.com/generate_204",
        "https://www.dw.com/tr/",
        "https://www.bbc.com/turkce"
    ];

    public static IReadOnlyList<string> YoutubeUrls { get; } =
    [
        "https://www.youtube.com/",
        "https://m.youtube.com/",
        "https://www.youtube.com/generate_204",
        "https://i.ytimg.com/generate_204"
    ];

    public static IReadOnlyList<string> ContentUrls { get; } =
    [
        "https://discord.com/api/v9/experiments",
        "https://cdn.discordapp.com/",
        "https://www.wattpad.com/home",
        "https://i.ytimg.com/generate_204",
        "http://shanesworld.com/"
    ];
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
    public int Weight { get; init; }
    public List<ProbeOutcome> Outcomes { get; init; } = [];
    public double Ratio => Passed + Failed == 0 ? 0 : (double)Passed / (Passed + Failed);
}

public static class ProbeWeights
{
    public static int Of(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("discord")) return 3;
        if (u.Contains("/api/") || u.Contains("generate_204") || u.Contains("/home")) return 3;
        if (u.Contains("youtube") || u.Contains("ytimg") || u.Contains("roblox") || u.Contains("wattpad")) return 2;
        if (u.Contains("pornhub") || u.Contains("xvideos") || u.Contains("onlyfans") || u.Contains("bet365")) return 2;
        if (u.Contains("instagram") || u.Contains("dw.com") || u.Contains("bbc.")) return 2;
        return 1;
    }

    public static bool IsYoutube(string url)
    {
        var u = url.ToLowerInvariant();
        return u.Contains("youtube") || u.Contains("ytimg");
    }

    public static bool IsDiscord(string url) => url.Contains("discord", StringComparison.OrdinalIgnoreCase);

    public static bool SameSite(string a, string b)
    {
        static string Host(string u)
        {
            try
            {
                var h = new Uri(u).Host.ToLowerInvariant();
                return h.StartsWith("www.") ? h[4..] : h;
            }
            catch { return u; }
        }
        var ha = Host(a);
        var hb = Host(b);
        return ha.Contains(hb, StringComparison.OrdinalIgnoreCase)
            || hb.Contains(ha, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AutoTuner
{
    public static IReadOnlyList<string> DefaultUrls => TurkeyBlockedSites.ProbeUrls;

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
            await Task.Delay(800, ct).ConfigureAwait(false);
            var outcomes = await ProbeManyAsync(urls, ct).ConfigureAwait(false);
            scores.Add(ScoreOf(strategy, outcomes));
        }
        return Rank(scores);
    }

    public static List<string> FocusUrls(IReadOnlyList<ProbeOutcome> baseline)
    {
        var failed = baseline.Where(o => !o.Ok)
            .OrderByDescending(o => ProbeWeights.Of(o.Url))
            .Select(o => o.Url)
            .Take(10)
            .ToList();
        if (failed.Count == 0) return [];
        var set = new HashSet<string>(failed, StringComparer.OrdinalIgnoreCase);
        if (failed.Any(ProbeWeights.IsYoutube))
        {
            foreach (var u in TurkeyBlockedSites.YoutubeUrls)
                set.Add(u);
        }
        foreach (var u in TurkeyBlockedSites.ContentUrls)
        {
            if (failed.Any(f => ProbeWeights.SameSite(f, u)))
                set.Add(u);
        }
        return set.Take(16).ToList();
    }

    public static StrategyScore ScoreOf(Strategy strategy, IReadOnlyList<ProbeOutcome> outcomes) =>
        new()
        {
            Strategy = strategy,
            Passed = outcomes.Count(o => o.Ok),
            Failed = outcomes.Count(o => !o.Ok),
            Weight = outcomes.Sum(o => o.Ok ? ProbeWeights.Of(o.Url) : 0),
            Outcomes = outcomes.ToList()
        };

    public static List<StrategyScore> Rank(IEnumerable<StrategyScore> scores) =>
        scores
            .OrderByDescending(s => s.Weight)
            .ThenByDescending(s => s.Ratio)
            .ThenByDescending(s => s.Passed)
            .ThenBy(s => Array.IndexOf(StrategyCatalog.TuneOrder.ToArray(), s.Strategy.Id))
            .ToList();

    public static async Task<IReadOnlyList<ProbeOutcome>> ProbeManyAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        var results = new ProbeOutcome[urls.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, urls.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = 6,
            CancellationToken = ct
        }, async (i, token) =>
        {
            results[i] = await ProbeAsync(urls[i], token).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return results;
    }

    public static async Task<ProbeOutcome> ProbeAsync(string url, CancellationToken ct)
    {
        var h1 = await ProbeOnceAsync(url, HttpVersion.Version11, HttpVersionPolicy.RequestVersionOrLower, ct)
            .ConfigureAwait(false);
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || !h1.Ok)
            return h1;
        var h2 = await ProbeOnceAsync(url, HttpVersion.Version20, HttpVersionPolicy.RequestVersionOrLower, ct)
            .ConfigureAwait(false);
        return h2.Ok ? h1 : h2;
    }

    private static async Task<ProbeOutcome> ProbeOnceAsync(
        string url, Version version, HttpVersionPolicy policy, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.Zero,
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true,
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            })
            {
                Timeout = TimeSpan.FromSeconds(8),
                DefaultRequestVersion = version,
                DefaultVersionPolicy = policy
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MuckDPI/1.3");
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
