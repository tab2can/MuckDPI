using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MuckDPI.Engine.DnsProtect;

public sealed class DohResolver : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;

    public DohResolver(DnsProviderKind provider)
    {
        var (ip, host, path) = provider switch
        {
            DnsProviderKind.Google or DnsProviderKind.DohGoogle => ("8.8.8.8", "dns.google", "/resolve"),
            DnsProviderKind.Quad9 => ("9.9.9.9", "dns.quad9.net", "/dns-query"),
            DnsProviderKind.AdGuard => ("94.140.14.14", "dns.adguard.com", "/dns-query"),
            DnsProviderKind.Mullvad => ("194.242.2.2", "dns.mullvad.net", "/dns-query"),
            _ => ("1.1.1.1", "cloudflare-dns.com", "/dns-query")
        };

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(IPAddress.Parse(ip), 443, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{host}"),
            Timeout = TimeSpan.FromSeconds(4)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
        _url = path;
        ProviderName = host;
        BootstrapIp = ip;
    }

    public string ProviderName { get; }
    public string BootstrapIp { get; }

    public async Task<IReadOnlyList<DnsAnswer>> ResolveAsync(string name, string type, CancellationToken ct)
    {
        var url = $"{_url}?name={Uri.EscapeDataString(name)}&type={Uri.EscapeDataString(type)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var answers = new List<DnsAnswer>();
        if (!doc.RootElement.TryGetProperty("Answer", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return answers;
        foreach (var item in arr.EnumerateArray())
        {
            var t = item.TryGetProperty("type", out var te) ? te.GetInt32() : 0;
            var data = item.TryGetProperty("data", out var de) ? de.GetString() ?? "" : "";
            var ttl = item.TryGetProperty("TTL", out var ttlEl) ? ttlEl.GetInt32() : 60;
            if (data.Length == 0) continue;
            answers.Add(new DnsAnswer(t, data, ttl));
        }
        return answers;
    }

    public void Dispose() => _http.Dispose();
}

public readonly record struct DnsAnswer(int Type, string Data, int Ttl);

internal static class DnsMessage
{
    public static bool TryParseQuery(ReadOnlySpan<byte> payload, out ushort id, out string qname, out ushort qtype)
    {
        id = 0;
        qname = "";
        qtype = 0;
        if (payload.Length < 12) return false;
        id = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2));
        if ((flags & 0x8000) != 0) return false; // response
        var qd = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2));
        if (qd == 0) return false;
        var i = 12;
        var labels = new List<string>();
        while (i < payload.Length)
        {
            var len = payload[i];
            if (len == 0) { i++; break; }
            if ((len & 0xC0) == 0xC0) return false;
            i++;
            if (i + len > payload.Length) return false;
            labels.Add(Encoding.ASCII.GetString(payload.Slice(i, len)));
            i += len;
        }
        if (i + 4 > payload.Length) return false;
        qtype = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(i, 2));
        qname = string.Join('.', labels);
        return qname.Length > 0;
    }

    public static byte[] BuildResponse(ushort id, string qname, ushort qtype, IReadOnlyList<DnsAnswer> answers)
    {
        using var ms = new MemoryStream();
        void U16(ushort v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, v);
            ms.Write(b);
        }
        void U32(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }

        U16(id);
        U16(0x8180); // standard response, recursion
        U16(1);
        var filtered = answers.Where(a => a.Type == qtype || qtype == 255).ToList();
        U16((ushort)filtered.Count);
        U16(0);
        U16(0);
        WriteName(ms, qname);
        U16(qtype);
        U16(1);

        foreach (var a in filtered)
        {
            WriteName(ms, qname);
            U16((ushort)a.Type);
            U16(1);
            U32((uint)Math.Clamp(a.Ttl, 30, 3600));
            if (a.Type == 1 && IPAddress.TryParse(a.Data, out var v4) && v4.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = v4.GetAddressBytes();
                U16((ushort)bytes.Length);
                ms.Write(bytes);
            }
            else if (a.Type == 28 && IPAddress.TryParse(a.Data, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = v6.GetAddressBytes();
                U16((ushort)bytes.Length);
                ms.Write(bytes);
            }
            else if (a.Type is 5 or 2 or 16)
            {
                using var nameMs = new MemoryStream();
                WriteName(nameMs, a.Data.TrimEnd('.'));
                var nb = nameMs.ToArray();
                U16((ushort)nb.Length);
                ms.Write(nb);
            }
        }
        return ms.ToArray();
    }

    private static void WriteName(Stream ms, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);
    }
}
