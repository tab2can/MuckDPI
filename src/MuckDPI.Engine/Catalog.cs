namespace MuckDPI.Engine;

public static class StrategyCatalog
{
    public static IReadOnlyList<Strategy> All { get; } =
    [
        new Strategy
        {
            Id = "turkey",
            Name = "Turkey recommended (-5 + TTL 5)",
            NameTr = "Türkiye önerilen (-5 + TTL 5)",
            Description = "GoodbyeDPI-Turkey default: reverse fragment, split at 2, fake TTL 5. Use with Yandex DNS :1253.",
            DescriptionTr = "GoodbyeDPI-Turkey varsayılanı: ters parçalama, 2 bayt bölme, sahte TTL 5. Yandex DNS 1253 ile kullanın.",
            SplitAtSni = false,
            SplitPos = 2,
            ReverseFragments = true,
            SendFake = true,
            FakeTtl = 5,
            FakeObfuscateSni = true,
            MaxPayload = 1200
        },
        new Strategy
        {
            Id = "so-ttl3",
            Name = "Superonline alt.1 (TTL 3)",
            NameTr = "Superonline alt.1 (TTL 3)",
            Description = "Only fake TTL 3. Light Superonline / Discord-update profile.",
            DescriptionTr = "Yalnızca sahte TTL 3. Hafif Superonline / Discord güncelleme profili.",
            SplitAtSni = false,
            SplitPos = 2,
            SendFake = true,
            FakeTtl = 3,
            ReverseFragments = false,
            MaxPayload = 1200
        },
        new Strategy
        {
            Id = "so-mode5",
            Name = "Superonline alt.2 (mode -5)",
            NameTr = "Superonline alt.2 (mod -5)",
            Description = "Reverse fragment + auto TTL. No DNS in the original script; we still redirect DNS.",
            DescriptionTr = "Ters parçalama + otomatik TTL. Orijinal script'te DNS yok; biz yine de DNS yönlendiririz.",
            SplitAtSni = false,
            SplitPos = 2,
            ReverseFragments = true,
            SendFake = true,
            AutoTtl = true,
            FakeTtl = 5,
            MaxPayload = 1200
        },
        new Strategy
        {
            Id = "so-ttl3-dns",
            Name = "Superonline alt.3 (TTL 3 + DNS)",
            NameTr = "Superonline alt.3 (TTL 3 + DNS)",
            Description = "Fake TTL 3 plus DNS redirect. Strong on Superonline Discord.",
            DescriptionTr = "Sahte TTL 3 ve DNS yönlendirme. Superonline Discord'da güçlü.",
            SplitAtSni = false,
            SplitPos = 2,
            SendFake = true,
            FakeTtl = 3,
            MaxPayload = 1200
        },
        new Strategy
        {
            Id = "mode9",
            Name = "Strong / TTNet YouTube (-9)",
            NameTr = "Güçlü / TTNet YouTube (-9)",
            Description = "Wrong seq + checksum + reverse frag + QUIC drop. Use when -5 is not enough.",
            DescriptionTr = "Yanlış sıra + sağlama + ters parça + QUIC düşürme. -5 yetmezse.",
            SplitAtSni = false,
            SplitPos = 2,
            ReverseFragments = true,
            SendFake = true,
            FakeWrongSeq = true,
            FakeWrongChecksum = true,
            FakeRepeats = 2,
            BlockQuic = true,
            MaxPayload = 1200
        },
        new Strategy
        {
            Id = "split-sni",
            Name = "SNI split",
            NameTr = "SNI bölme",
            Description = "Split TLS ClientHello at the server name. Gentle, rarely breaks sites.",
            DescriptionTr = "TLS ClientHello paketini sunucu adının tam sınırından böler. Siteleri bozma ihtimali düşüktür.",
            SplitAtSni = true,
            SplitPos = 2,
            ReverseFragments = false,
            SendFake = false
        },
        new Strategy
        {
            Id = "split-reverse",
            Name = "Reverse SNI split",
            NameTr = "Ters SNI bölme",
            Description = "Send the second fragment first so DPI reassembles the wrong first packet.",
            DescriptionTr = "İkinci parçayı önce gönderir; DPI yanlış ilk paketi görür.",
            SplitAtSni = true,
            ReverseFragments = true
        },
        new Strategy
        {
            Id = "fake-ttl",
            Name = "Fake TTL + split",
            NameTr = "Sahte TTL + bölme",
            Description = "Send a decoy handshake that dies before the origin, then the real split hello.",
            DescriptionTr = "Hedefe ulaşmayan sahte el sıkışma gönderir, ardından gerçek bölünmüş paketi yollar.",
            SplitAtSni = true,
            ReverseFragments = true,
            SendFake = true,
            FakeTtl = 3,
            FakeObfuscateSni = true
        },
        new Strategy
        {
            Id = "fake-seq",
            Name = "Fake sequence + checksum",
            NameTr = "Sahte sıra + sağlama",
            Description = "Decoy packets with bad TCP sequence and checksum. Safer on some routers than low TTL.",
            DescriptionTr = "Yanlış TCP sıra numarası ve sağlama toplamı ile sahte paket. Bazı modemlerde düşük TTL'den daha güvenli.",
            SplitAtSni = true,
            ReverseFragments = true,
            SendFake = true,
            FakeWrongSeq = true,
            FakeWrongChecksum = true,
            FakeTtl = 0,
            FakeRepeats = 2
        },
        new Strategy
        {
            Id = "aggressive",
            Name = "Aggressive (TTL + seq + reverse)",
            NameTr = "Agresif (TTL + sıra + ters)",
            Description = "Combined desync. Use when milder modes fail. Host-list only recommended.",
            DescriptionTr = "Tüm yöntemlerin birleşimi. Hafif modlar yetmezse. Yalnızca host listesiyle kullanın.",
            SplitAtSni = true,
            ReverseFragments = true,
            SendFake = true,
            FakeTtl = 3,
            FakeWrongSeq = true,
            FakeWrongChecksum = true,
            FakeObfuscateSni = true,
            FakeRepeats = 2,
            HttpObfuscate = true,
            SplitPos = 1
        },
        new Strategy
        {
            Id = "http-mix",
            Name = "HTTP obfuscation + split",
            NameTr = "HTTP gizleme + bölme",
            Description = "Host header tricks plus fragmentation. Helps HTTP and some mixed stacks.",
            DescriptionTr = "Host başlığı oyunları ve parçalama. HTTP ve karma yığınlarda işe yarar.",
            SplitAtSni = true,
            HttpObfuscate = true,
            SplitPos = 2
        },
        new Strategy
        {
            Id = "tiny-frag",
            Name = "Tiny fragments",
            NameTr = "Küçük parçalar",
            Description = "Very early split. Used by several Superonline lines.",
            DescriptionTr = "Çok erken bölme. Birçok Superonline hattında işe yarar.",
            SplitAtSni = false,
            SplitPos = 1,
            ReverseFragments = true
        }
    ];

    public static Strategy Get(string id) =>
        All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static IReadOnlyList<string> TuneOrder { get; } =
        ["turkey", "so-ttl3", "so-mode5", "so-ttl3-dns", "mode9", "split-reverse", "fake-seq", "aggressive"];
}

public static class IspCatalog
{
    public static IReadOnlyList<IspProfile> All { get; } =
    [
        new IspProfile
        {
            Id = "turk-telekom",
            Name = "Türk Telekom",
            Asns = [9121, 47331],
            Keywords = ["turk telekom", "türk telekom", "ttnet", "ttnet", "turktelekom"],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = true,
            NotesTr = "GoodbyeDPI-Turkey önerileni: -5 + TTL 5 + Yandex :1253. YouTube için yetmezse Güçlü (-9) deneyin.",
            NotesEn = "GoodbyeDPI-Turkey recommended: -5 + TTL 5 + Yandex :1253. If YouTube still fails, try Strong (-9)."
        },
        new IspProfile
        {
            Id = "superonline",
            Name = "Turkcell Superonline",
            Asns = [34984, 16135],
            Keywords = ["superonline", "turkcell", "tellcom"],
            DefaultStrategyId = "so-ttl3-dns",
            DnsPoisonLikely = true,
            NotesTr = "Discord update failed için alt.1 (TTL 3) veya alt.3 (TTL 3 + DNS). Fiberde sırayla deneyin.",
            NotesEn = "For Discord update failed try alt.1 (TTL 3) or alt.3 (TTL 3 + DNS). Try them in order on fiber."
        },
        new IspProfile
        {
            Id = "vodafone",
            Name = "Vodafone",
            Asns = [15897, 8386, 15924, 20978],
            Keywords = ["vodafone", "vodafone net"],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = true,
            NotesTr = "Sahte sıra/sağlama + ters SNI bölme birçok Vodafone hattında daha istikrarlı.",
            NotesEn = "Fake sequence/checksum plus reverse SNI split is more stable on many Vodafone lines."
        },
        new IspProfile
        {
            Id = "turknet",
            Name = "TurkNet",
            Asns = [12735],
            Keywords = ["turknet", "turk net"],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = false,
            NotesTr = "Daha hafif DPI. SNI bölme çoğu zaman yeter; yine de CDN host listesi şart.",
            NotesEn = "Lighter DPI. SNI split is often enough; CDN host lists are still required."
        },
        new IspProfile
        {
            Id = "turksat",
            Name = "Türksat Kablonet",
            Asns = [47524],
            Keywords = ["türksat", "turksat", "kablonet"],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = true,
            NotesTr = "Kablo şebekelerinde sahte TTL + bölme sık kullanılır.",
            NotesEn = "Cable networks often need fake TTL plus split."
        },
        new IspProfile
        {
            Id = "millenicom",
            Name = "Millenicom",
            Asns = [34296],
            Keywords = ["millenicom"],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = true,
            NotesTr = "Türk Telekom omurgasına yakın. Türkiye önerilen profili ile başlayın.",
            NotesEn = "Close to Türk Telekom backbone. Start with the Turkey recommended profile."
        },
        new IspProfile
        {
            Id = "universal",
            Name = "Universal",
            Asns = [],
            Keywords = [],
            DefaultStrategyId = "turkey",
            DnsPoisonLikely = true,
            NotesTr = "ISS tanınmadı. Türkiye önerilen profili (Yandex :1253 + TTL 5) çoğu hatta çalışır; sihirbaz sırayla dener.",
            NotesEn = "ISP not recognized. The Turkey recommended profile (Yandex :1253 + TTL 5) works on most lines; the wizard tries the rest."
        }
    ];

    public static IspProfile Get(string id) =>
        All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[^1];

    public static IspProfile? Match(int? asn, string? org)
    {
        if (asn is > 0)
        {
            var byAsn = All.FirstOrDefault(p => p.Asns.Contains(asn.Value));
            if (byAsn is not null) return byAsn;
        }
        if (!string.IsNullOrWhiteSpace(org))
        {
            var hay = org.ToLowerInvariant();
            foreach (var p in All)
            {
                if (p.Keywords.Any(k => hay.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    return p;
            }
        }
        return null;
    }
}

public static class ServiceCatalog
{
    public static IReadOnlyList<string> DefaultEnabled { get; } =
        ["youtube", "discord", "instagram", "twitter", "spotify", "tiktok", "reddit", "twitch", "telegram", "ai", "github"];

    public static IReadOnlyList<ServicePack> All { get; } =
    [
        new ServicePack
        {
            Id = "youtube",
            Name = "YouTube",
            NameTr = "YouTube",
            BlockQuic = true,
            ProbeUrls = ["https://www.youtube.com/", "https://i.ytimg.com/generate_204"],
            Hosts =
            [
                "youtube.com", "youtu.be", "youtube-nocookie.com", "youtubekids.com",
                "googlevideo.com", "ytimg.com", "yt3.ggpht.com", "yt3.googleusercontent.com",
                "youtubei.googleapis.com", "youtube.googleapis.com", "youtube-ui.l.google.com",
                "wide-youtube.l.google.com", "yt.be", "ggpht.com", "googleusercontent.com"
            ]
        },
        new ServicePack
        {
            Id = "discord",
            Name = "Discord",
            NameTr = "Discord",
            BlockQuic = false,
            ProbeUrls = ["https://discord.com/api/v9/experiments", "https://cdn.discordapp.com/"],
            Hosts =
            [
                "discord.com", "discordapp.com", "discord.gg", "discord.media", "discordapp.net",
                "discord.gift", "discord.co", "gateway.discord.gg", "cdn.discordapp.com",
                "media.discordapp.net", "images-ext-1.discordapp.net", "images-ext-2.discordapp.net",
                "status.discord.com", "discord-attachments-uploads-prd.storage.googleapis.com",
                "dis.gd"
            ]
        },
        new ServicePack
        {
            Id = "instagram",
            Name = "Instagram / Facebook / Threads",
            NameTr = "Instagram / Facebook / Threads",
            BlockQuic = true,
            ProbeUrls = ["https://www.instagram.com/", "https://www.facebook.com/"],
            Hosts =
            [
                "instagram.com", "cdninstagram.com", "igsonar.com", "facebook.com", "fbcdn.net",
                "facebook.net", "fb.com", "fbsbx.com", "messenger.com", "threads.net", "threads.com",
                "whatsapp.com", "whatsapp.net", "wa.me"
            ]
        },
        new ServicePack
        {
            Id = "twitter",
            Name = "X (Twitter)",
            NameTr = "X (Twitter)",
            BlockQuic = false,
            ProbeUrls = ["https://x.com/", "https://twitter.com/"],
            Hosts =
            [
                "x.com", "twitter.com", "t.co", "twimg.com", "pscp.tv", "periscope.tv",
                "tweetdeck.com", "ads-twitter.com"
            ]
        },
        new ServicePack
        {
            Id = "spotify",
            Name = "Spotify",
            NameTr = "Spotify",
            BlockQuic = false,
            ProbeUrls = ["https://open.spotify.com/"],
            Hosts =
            [
                "spotify.com", "scdn.co", "spotifycdn.com", "spotifycdn.net", "pscdn.co",
                "spoti.fi", "audio-ak-spotify-com.akamaized.net"
            ]
        },
        new ServicePack
        {
            Id = "tiktok",
            Name = "TikTok",
            NameTr = "TikTok",
            BlockQuic = true,
            ProbeUrls = ["https://www.tiktok.com/"],
            Hosts =
            [
                "tiktok.com", "tiktokv.com", "tiktokcdn.com", "tiktokcdn-us.com", "musical.ly",
                "bytedance.com", "byteoversea.com", "ibytedtos.com", "ttlivecdn.com"
            ]
        },
        new ServicePack
        {
            Id = "reddit",
            Name = "Reddit",
            NameTr = "Reddit",
            BlockQuic = false,
            ProbeUrls = ["https://www.reddit.com/"],
            Hosts = ["reddit.com", "redditstatic.com", "redditmedia.com", "redd.it", "reddituploads.com"]
        },
        new ServicePack
        {
            Id = "twitch",
            Name = "Twitch",
            NameTr = "Twitch",
            BlockQuic = false,
            ProbeUrls = ["https://www.twitch.tv/"],
            Hosts = ["twitch.tv", "twitchcdn.net", "jtvnw.net", "ttvnw.net", "twitchsvc.net"]
        },
        new ServicePack
        {
            Id = "telegram",
            Name = "Telegram",
            NameTr = "Telegram",
            BlockQuic = false,
            ProbeUrls = ["https://web.telegram.org/"],
            Hosts = ["telegram.org", "t.me", "telegra.ph", "telegram-cdn.org", "telesco.pe"]
        },
        new ServicePack
        {
            Id = "ai",
            Name = "AI services",
            NameTr = "Yapay zeka servisleri",
            BlockQuic = false,
            ProbeUrls = ["https://chatgpt.com/", "https://claude.ai/"],
            Hosts =
            [
                "openai.com", "chatgpt.com", "oaistatic.com", "oaiusercontent.com",
                "anthropic.com", "claude.ai", "groq.com", "x.ai", "gemini.google.com"
            ]
        },
        new ServicePack
        {
            Id = "github",
            Name = "GitHub / Dev",
            NameTr = "GitHub / Geliştirici",
            BlockQuic = false,
            ProbeUrls = ["https://github.com/"],
            Hosts =
            [
                "github.com", "githubusercontent.com", "githubassets.com", "gitlab.com",
                "npmjs.com", "pypi.org", "nuget.org", "crates.io"
            ]
        }
    ];

    public static readonly string[] ExcludeHosts =
    [
        "turkiye.gov.tr", "e-devlet.gov.tr", "gib.gov.tr", "nvi.gov.tr", "sgk.gov.tr",
        "vakifbank.com.tr", "isbank.com.tr", "garanti.com.tr", "akbank.com.tr", "ykb.com",
        "ziraatbank.com.tr", "halkbank.com.tr", "enpara.com", "papara.com",
        "microsoft.com", "windowsupdate.com", "update.microsoft.com", "login.microsoftonline.com",
        "apple.com", "icloud.com"
    ];
}

public sealed class HostMatcher
{
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _suffixes = [];
    private readonly HashSet<string> _exclude = new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _global;

    public HostMatcher(AppSettings settings)
    {
        _global = settings.FilterMode == FilterMode.Global;
        foreach (var ex in ServiceCatalog.ExcludeHosts)
            _exclude.Add(ex);

        if (_global)
            return;

        foreach (var pack in ServiceCatalog.All)
        {
            if (!settings.EnabledServices.Contains(pack.Id, StringComparer.OrdinalIgnoreCase))
                continue;
            foreach (var h in pack.Hosts) Add(h);
        }
        foreach (var h in settings.CustomHosts) Add(h);
        foreach (var h in settings.AutoHosts) Add(h);
    }

    private void Add(string host)
    {
        host = host.Trim().ToLowerInvariant().TrimStart('*', '.');
        if (host.Length == 0) return;
        _exact.Add(host);
        _suffixes.Add("." + host);
    }

    public bool ShouldTouch(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return _global;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (IsExcluded(host)) return false;
        if (_global || _exact.Count == 0) return true;
        if (_exact.Contains(host)) return true;
        foreach (var s in _suffixes)
        {
            if (host.EndsWith(s, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public bool IsExcluded(string host)
    {
        if (_exclude.Contains(host)) return true;
        foreach (var ex in _exclude)
        {
            if (host.EndsWith("." + ex, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool ShouldBlockQuic(string? host, AppSettings settings)
    {
        if (settings.QuicMode == QuicMode.Off) return false;
        if (settings.QuicMode == QuicMode.BlockAll) return true;
        if (string.IsNullOrEmpty(host))
            return settings.QuicMode == QuicMode.BlockAll || _global && settings.QuicMode == QuicMode.BlockHostlist;
        if (!ShouldTouch(host)) return false;
        foreach (var pack in ServiceCatalog.All)
        {
            if (!pack.BlockQuic) continue;
            if (!settings.EnabledServices.Contains(pack.Id, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var h in pack.Hosts)
            {
                if (host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
