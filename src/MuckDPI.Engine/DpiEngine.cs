using System.Buffers.Binary;
using System.Runtime.InteropServices;
using MuckDPI.Engine.DnsProtect;
using MuckDPI.Engine.Native;
using MuckDPI.Engine.Packet;

namespace MuckDPI.Engine;

public sealed class DpiEngine : IDisposable
{
    private readonly EngineConfig _config;
    private readonly ConnectionTable _conns;
    private readonly HostMatcher _hosts;
    private readonly TtlTracker _ttl = new();
    private readonly HostLearner _learner;
    private DohResolver? _doh;
    private DnsNat? _dnsNat;
    private nint _handle = 0;
    private Thread? _thread;
    private volatile bool _run;
    private int _loggedQuic;
    private int _loggedV6Dns;
    private int _loggedV6Http;
    private long _poolKillUntil;
    private readonly object _gate = new();
    private readonly object _sendLock = new();

    public EngineStats Stats { get; } = new();
    public bool IsRunning => _run && _handle != 0 && _handle != nint.Zero && _handle != -1;
    public event EventHandler<LogEventArgs>? Log;
    public event EventHandler<string>? HostLearned;
    public event EventHandler? StatsChanged;

    public DpiEngine(EngineConfig config)
    {
        _config = config;
        _hosts = config.Hosts;
        _conns = new ConnectionTable(config.Strategy.FirstPackets);
        _learner = new HostLearner(config.Settings.LearnedHardHosts);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_run) return;
            if (_config.Settings.EnableDnsProtect && _config.Dns != DnsProviderKind.Off)
            {
                if (_config.Redirect is { } redir)
                {
                    _dnsNat = new DnsNat(redir.Ip, redir.Port);
                    Emit("info", $"DNS redirect {_dnsNat.Target}:{_dnsNat.Port}");
                }
                if (_config.UseDoh)
                    _doh = new DohResolver(_config.Dns);
            }

            var filter = BuildFilter(_dnsNat?.Port);
            _handle = WinDivertNative.WinDivertOpen(filter, WinDivertNative.LayerNetwork, 0, 0);
            if (_handle == 0 || _handle == -1)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(ErrorText(err));
            }
            WinDivertNative.WinDivertSetParam(_handle, 0, 8192);
            WinDivertNative.WinDivertSetParam(_handle, 1, 2000);
            WinDivertNative.WinDivertSetParam(_handle, 2, 8 * 1024 * 1024);
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MuckDPI.Engine", Priority = ThreadPriority.AboveNormal };
            _poolKillUntil = Environment.TickCount64 + 12000;
            _thread.Start();
            WindowsDns.Flush();
            Emit("info", $"Engine started ({_config.Strategy.Id}), QUIC drop, Yandex :1253");
            Emit("info", "Eski HTTP/3 oturumları düşürülüyor — tarayıcıyı kapatmanıza gerek yok.");
        }
    }

    public void Stop()
    {
        bool wasRunning;
        lock (_gate)
        {
            wasRunning = _run || (_handle != 0 && _handle != -1);
            _run = false;
            if (_handle != 0 && _handle != -1)
            {
                try { WinDivertNative.WinDivertShutdown(_handle, 3); } catch { /* ignore */ }
                try { WinDivertNative.WinDivertClose(_handle); } catch { /* ignore */ }
                _handle = 0;
            }
        }
        _thread?.Join(1500);
        _thread = null;
        _doh?.Dispose();
        _doh = null;
        _dnsNat = null;
        if (wasRunning)
        {
            WindowsDns.Flush();
            Emit("info", "Engine stopped");
        }
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        var buffer = new byte[0xFFFF + 40];
        var lastSweep = Environment.TickCount64;
        var lastStats = Environment.TickCount64;
        while (_run)
        {
            var addr = new WinDivertNative.Address();
            uint recv;
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    if (!WinDivertNative.WinDivertRecv(_handle, (nint)p, (uint)buffer.Length, out recv, ref addr))
                    {
                        if (!_run) break;
                        continue;
                    }
                }
            }
            if (recv == 0) continue;
            Interlocked.Increment(ref Stats.PacketsSeen);
            try
            {
                Handle(buffer, (int)recv, ref addr);
            }
            catch (Exception ex)
            {
                Emit("warn", ex.Message);
                ReinjectionSafe(buffer, (int)recv, ref addr);
            }

            var now = Environment.TickCount64;
            if (now - lastSweep > 30_000)
            {
                _conns.Sweep();
                _dnsNat?.Sweep();
                lastSweep = now;
            }
            if (now - lastStats > 1000)
            {
                StatsChanged?.Invoke(this, EventArgs.Empty);
                lastStats = now;
            }
        }
    }

    private void Handle(byte[] buffer, int length, ref WinDivertNative.Address addr)
    {
        if (!ParsedPacket.TryParse(buffer.AsSpan(0, length), out var pkt))
        {
            SendRaw(buffer, length, ref addr);
            return;
        }

        if (!addr.Outbound)
            _ttl.ObserveInbound(pkt);

        if (!addr.Outbound && pkt.IsUdp && _dnsNat is not null && pkt.SrcPort == _dnsNat.Port)
        {
            if (_dnsNat.TryRewriteInbound(buffer, length, pkt))
            {
                SendRaw(buffer, length, ref addr);
                Interlocked.Increment(ref Stats.DnsRedirected);
                return;
            }
            SendRaw(buffer, length, ref addr);
            return;
        }

        if (!addr.Outbound && pkt.IsTcp && pkt.TcpRst)
        {
            if (_config.Settings.EnablePassiveDrop && ShouldDropRst(pkt))
            {
                Interlocked.Increment(ref Stats.PassiveDropped);
                var host = _conns.HostOf(ConnectionTable.Key(pkt.DstIp, pkt.DstPort, pkt.SrcIp, pkt.SrcPort));
                if (_learner.NoteRst(host))
                    Emit("info", $"Öğrenildi {host} — bu siteye daha sert yöntem");
                if (!string.IsNullOrEmpty(host) && !_hosts.ShouldTouch(host) && !_hosts.IsExcluded(host)
                    && _config.Settings.FilterMode == FilterMode.Smart)
                {
                    HostLearned?.Invoke(this, host);
                    Interlocked.Increment(ref Stats.AutoLearned);
                    Emit("info", $"Auto-learned {host}");
                }
                return;
            }
            SendRaw(buffer, length, ref addr);
            return;
        }

        if (addr.Outbound && pkt.DstPort == 853)
        {
            Interlocked.Increment(ref Stats.DnsRedirected);
            return;
        }

        if (addr.Outbound && pkt.IsTcp && pkt.DstPort == 53)
        {
            Interlocked.Increment(ref Stats.DnsRedirected);
            return;
        }

        if (addr.Outbound && pkt.IsUdp && pkt.DstPort == 53)
        {
            if (pkt.IsIPv6)
            {
                if (Interlocked.Exchange(ref _loggedV6Dns, 1) == 0)
                    Emit("info", "IPv6 DNS dropped — IPv4 + Yandex :1253 kullanılacak");
                Interlocked.Increment(ref Stats.DnsRedirected);
                return;
            }
            if (_dnsNat is not null && _dnsNat.TryRewriteOutbound(buffer, length, pkt))
            {
                SendRaw(buffer, length, ref addr);
                Interlocked.Increment(ref Stats.DnsRedirected);
                return;
            }
            if (_doh is not null)
            {
                HandleDns(buffer, length, pkt, ref addr);
                return;
            }
            SendRaw(buffer, length, ref addr);
            return;
        }

        if (addr.Outbound && pkt.IsIPv6 && (pkt.DstPort == 443 || pkt.DstPort == 80))
        {
            if (Interlocked.Exchange(ref _loggedV6Http, 1) == 0)
                Emit("info", "IPv6 HTTP(S) dropped — tarayıcı IPv4 kullanacak");
            FailLocal(buffer, length, pkt, ref addr);
            Interlocked.Increment(ref Stats.Ipv6Dropped);
            return;
        }

        if (addr.Outbound && pkt.IsUdp && pkt.DstPort == 443)
        {
            HandleQuic(buffer, length, pkt, ref addr);
            return;
        }

        if (addr.Outbound && pkt.IsTcp && (pkt.DstPort == 443 || pkt.DstPort == 80))
        {
            if (ShouldResetStalePool(pkt))
            {
                FailLocal(buffer, length, pkt, ref addr);
                Interlocked.Increment(ref Stats.PassiveDropped);
                return;
            }
            if (pkt.PayloadLength > 0)
            {
                HandleTcp(buffer, length, pkt, ref addr);
                return;
            }
        }

        SendRaw(buffer, length, ref addr);
    }

    private void HandleTcp(byte[] buffer, int length, ParsedPacket pkt, ref WinDivertNative.Address addr)
    {
        var key = ConnectionTable.Key(pkt.SrcIp, pkt.SrcPort, pkt.DstIp, pkt.DstPort);
        var payload = pkt.Payload;
        string? host = null;
        var http = false;
        var sniOff = 0;
        var sniLen = 0;
        if (pkt.DstPort == 443 && TlsSni.TryGetHost(payload, out var sni, out sniOff, out sniLen))
        {
            host = sni;
            _conns.RememberHost(key, host);
            if (_learner.NoteHello(host))
                Emit("info", $"Öğrenildi {host} — tekrar denemeler algılandı");
        }
        else if (pkt.DstPort == 80 && HttpHost.TryGetHost(payload, out var hh, out _, out _))
        {
            host = hh;
            http = true;
            _conns.RememberHost(key, host);
        }
        else
            host = _conns.HostOf(key);

        var st = _learner.For(host, _config.Strategy);
        var touch = _hosts.ShouldTouch(host);
        if (!touch)
        {
            SendRaw(buffer, length, ref addr);
            return;
        }
        if (!_conns.ShouldProcess(key, pkt.PayloadLength, st.FirstPackets))
        {
            SendRaw(buffer, length, ref addr);
            return;
        }
        var tlsHello = pkt.DstPort == 443 && payload.Length > 0 && payload[0] == 0x16;
        if (!tlsHello && pkt.PayloadLength > st.MaxPayload)
        {
            SendRaw(buffer, length, ref addr);
            return;
        }

        if (http && st.HttpObfuscate)
            HttpHost.Obfuscate(buffer.AsSpan(pkt.PayloadOffset, pkt.PayloadLength));

        if (st.SendFake)
            SendFakes(buffer, length, pkt, pkt.DstPort == 443 ? sniOff : 0, pkt.DstPort == 443 ? sniLen : 0, st, ref addr);

        SendSplit(buffer, length, pkt, st, ref addr);
        Interlocked.Increment(ref Stats.PacketsDesynced);
    }

    private void SendFakes(byte[] original, int length, ParsedPacket pkt, int sniOff, int sniLen, Strategy st, ref WinDivertNative.Address addr)
    {
        var repeats = Math.Clamp(st.FakeRepeats, 1, 6);
        for (var i = 0; i < repeats; i++)
        {
            var fake = new byte[length];
            Buffer.BlockCopy(original, 0, fake, 0, length);
            if (st.FakeObfuscateSni && sniLen > 0)
                TlsSni.ObfuscateHostname(fake.AsSpan(pkt.PayloadOffset, pkt.PayloadLength), sniOff, sniLen);
            if (st.FakeTtl > 0 || st.AutoTtl)
            {
                var ttl = st.AutoTtl
                    ? _ttl.FakeFor(pkt.DstIp.ToString(), st.FakeTtl)
                    : st.FakeTtl;
                PacketMutator.SetTtl(fake, pkt.IsIPv6, ttl);
            }
            if (st.FakeWrongSeq)
                PacketMutator.SetTcpSeq(fake, pkt.IpHeaderLength, pkt.Seq - 100000);
            var flags = 0UL;
            if (st.FakeWrongChecksum)
            {
                PacketMutator.SetTcpChecksum(fake, pkt.IpHeaderLength, 0x0001);
                flags = WinDivertNative.HelperNoTcpChecksum;
            }
            SendRaw(fake, length, ref addr, flags);
            Interlocked.Increment(ref Stats.FakeSent);
        }
    }

    private void SendSplit(byte[] original, int length, ParsedPacket pkt, Strategy st, ref WinDivertNative.Address addr)
    {
        var payloadLen = pkt.PayloadLength;
        if (payloadLen < 3)
        {
            SendRaw(original, length, ref addr);
            return;
        }

        var split = st.SplitAtSni
            ? TlsSni.SniSplitOffset(pkt.Payload)
            : st.SplitPos;
        split = Math.Clamp(split, 1, payloadLen - 1);

        var firstLen = pkt.PayloadOffset + split;
        var secondPayload = payloadLen - split;
        var secondLen = pkt.PayloadOffset + secondPayload;

        var first = new byte[firstLen];
        Buffer.BlockCopy(original, 0, first, 0, firstLen);
        ResizeIp(first, firstLen, pkt.IsIPv6);

        var second = new byte[secondLen];
        Buffer.BlockCopy(original, 0, second, 0, pkt.PayloadOffset);
        Buffer.BlockCopy(original, pkt.PayloadOffset + split, second, pkt.PayloadOffset, secondPayload);
        PacketMutator.SetTcpSeq(second, pkt.IpHeaderLength, pkt.Seq + (uint)split);
        ResizeIp(second, secondLen, pkt.IsIPv6);

        if (st.ReverseFragments)
        {
            SendRaw(second, secondLen, ref addr);
            SendRaw(first, firstLen, ref addr);
        }
        else
        {
            SendRaw(first, firstLen, ref addr);
            SendRaw(second, secondLen, ref addr);
        }
    }

    private static void ResizeIp(byte[] packet, int total, bool ipv6)
    {
        if (ipv6)
            PacketMutator.SetIpv6PayloadLength(packet, total - 40);
        else
            PacketMutator.SetIpv4Length(packet, total);
    }

    private void HandleQuic(byte[] buffer, int length, ParsedPacket pkt, ref WinDivertNative.Address addr)
    {
        if (!QuicInitial.LooksLikeQuic(pkt.Payload))
        {
            SendRaw(buffer, length, ref addr);
            return;
        }

        var host = _conns.HostOf(ConnectionTable.Key(pkt.SrcIp, pkt.SrcPort, pkt.DstIp, pkt.DstPort));
        var block = _config.Strategy.BlockQuic
                    || _config.Settings.QuicMode is QuicMode.BlockAll or QuicMode.BlockHostlist
                    || (host is not null && _hosts.ShouldBlockQuic(host, _config.Settings));

        if (_config.Settings.QuicMode is QuicMode.FakeHostlist && host is not null && _hosts.ShouldTouch(host))
        {
            var fake = new byte[length];
            Buffer.BlockCopy(buffer, 0, fake, 0, length);
            PacketMutator.SetTtl(fake, pkt.IsIPv6, 3);
            SendRaw(fake, length, ref addr);
            SendRaw(buffer, length, ref addr);
            Interlocked.Increment(ref Stats.FakeSent);
            return;
        }

        if (block)
        {
            if (Interlocked.Exchange(ref _loggedQuic, 1) == 0)
                Emit("info", "QUIC/HTTP3 dropped — tarayıcı TCP + desync kullanacak");
            FailLocal(buffer, length, pkt, ref addr);
            Interlocked.Increment(ref Stats.QuicBlocked);
            return;
        }
        SendRaw(buffer, length, ref addr);
    }

    private void HandleDns(byte[] buffer, int length, ParsedPacket pkt, ref WinDivertNative.Address addr)
    {
        if (!DnsMessage.TryParseQuery(pkt.Payload, out var id, out var qname, out var qtype))
        {
            SendRaw(buffer, length, ref addr);
            return;
        }
        var copy = new byte[length];
        Buffer.BlockCopy(buffer, 0, copy, 0, length);
        var outbound = addr;
        var doh = _doh;
        var payloadOffset = pkt.PayloadOffset;
        var ipHeaderLength = pkt.IpHeaderLength;
        var ipv6 = pkt.IsIPv6;
        var srcPort = pkt.SrcPort;
        var dstPort = pkt.DstPort;
        if (doh is null)
        {
            SendRaw(buffer, length, ref addr);
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var typeName = qtype switch { 1 => "A", 28 => "AAAA", 5 => "CNAME", _ => qtype.ToString() };
                var answers = await doh.ResolveAsync(qname, typeName, CancellationToken.None).ConfigureAwait(false);
                var body = DnsMessage.BuildResponse(id, qname, qtype, answers);
                var resp = BuildUdpReply(copy, payloadOffset, ipHeaderLength, ipv6, srcPort, dstPort, body);
                var inbound = outbound;
                inbound.Outbound = false;
                SendRaw(resp, resp.Length, ref inbound);
                Interlocked.Increment(ref Stats.DnsRewritten);
            }
            catch
            {
                // client will retry
            }
        });
    }

    private static byte[] BuildUdpReply(
        byte[] original,
        int payloadOffset,
        int ipHeaderLength,
        bool ipv6,
        ushort srcPort,
        ushort dstPort,
        byte[] dnsBody)
    {
        var packet = new byte[payloadOffset + dnsBody.Length];
        Buffer.BlockCopy(original, 0, packet, 0, payloadOffset);
        Buffer.BlockCopy(dnsBody, 0, packet, payloadOffset, dnsBody.Length);

        if (!ipv6)
        {
            Buffer.BlockCopy(original, 16, packet, 12, 4);
            Buffer.BlockCopy(original, 12, packet, 16, 4);
            PacketMutator.SetIpv4Length(packet, packet.Length);
        }
        else
        {
            Buffer.BlockCopy(original, 24, packet, 8, 16);
            Buffer.BlockCopy(original, 8, packet, 24, 16);
            PacketMutator.SetIpv6PayloadLength(packet, packet.Length - 40);
        }

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipHeaderLength, 2), dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipHeaderLength + 2, 2), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(ipHeaderLength + 4, 2), (ushort)(8 + dnsBody.Length));
        return packet;
    }

    private bool ShouldDropRst(ParsedPacket pkt)
    {
        if (pkt.IpId <= 1) return true;
        var host = _conns.HostOf(ConnectionTable.Key(pkt.DstIp, pkt.DstPort, pkt.SrcIp, pkt.SrcPort));
        return host is not null && _hosts.ShouldTouch(host);
    }

    private bool ShouldResetStalePool(in ParsedPacket pkt)
    {
        if (Environment.TickCount64 >= _poolKillUntil) return false;
        if (pkt.TcpSyn || pkt.TcpRst) return false;
        return PacketReply.IsPublicInternet(pkt.DstIp);
    }

    private void FailLocal(byte[] buffer, int length, in ParsedPacket pkt, ref WinDivertNative.Address addr)
    {
        try
        {
            var reply = pkt.IsTcp
                ? PacketReply.InboundRst(buffer.AsSpan(0, length), pkt)
                : PacketReply.IcmpUnreachable(buffer.AsSpan(0, length), pkt);
            var inbound = addr;
            inbound.Outbound = false;
            inbound.Impostor = true;
            SendRaw(reply, reply.Length, ref inbound);
        }
        catch
        {
            // drop original anyway
        }
    }

    private void ReinjectionSafe(byte[] buffer, int length, ref WinDivertNative.Address addr)
    {
        try { SendRaw(buffer, length, ref addr); } catch { /* drop rather than stall the stack */ }
    }

    private void SendRaw(byte[] buffer, int length, ref WinDivertNative.Address addr, ulong checksumFlags = 0)
    {
        if (_handle == 0 || _handle == -1) return;
        lock (_sendLock)
        {
            unsafe
            {
                fixed (byte* p = buffer)
                {
                    var n = (nint)p;
                    WinDivertNative.WinDivertHelperCalcChecksums(n, (uint)length, ref addr, checksumFlags);
                    WinDivertNative.WinDivertSend(_handle, n, (uint)length, out _, ref addr);
                }
            }
        }
    }

    private static string BuildFilter(ushort? dnsPort)
    {
        var extra = dnsPort is > 0
            ? $" or (inbound and udp.SrcPort == {dnsPort.Value})"
            : "";
        return
            "(outbound and tcp.DstPort == 443) or " +
            "(outbound and tcp.DstPort == 80) or " +
            "(outbound and tcp.DstPort == 53) or " +
            "(outbound and tcp.DstPort == 853) or " +
            "(outbound and udp.DstPort == 53) or " +
            "(outbound and udp.DstPort == 443) or " +
            "(outbound and udp.DstPort == 853) or " +
            "(inbound and tcp and tcp.Rst)" + extra;
    }

    private void Emit(string level, string message) =>
        Log?.Invoke(this, new LogEventArgs { Timestamp = DateTime.Now, Level = level, Message = message });

    private static string ErrorText(int err) => err switch
    {
        5 => "Administrator rights are required to load the WinDivert driver.",
        2 or 3 => "WinDivert.dll / WinDivert64.sys was not found next to MuckDPI.exe.",
        577 or 225 => "Windows blocked the WinDivert driver. Check SmartScreen / antivirus.",
        654 => "Another program already owns a conflicting WinDivert filter.",
        _ => $"WinDivertOpen failed (Win32 {err})."
    };
}

public sealed class EngineConfig
{
    public required AppSettings Settings { get; init; }
    public required Strategy Strategy { get; init; }
    public required HostMatcher Hosts { get; init; }
    public DnsProviderKind Dns { get; init; }
    public bool UseDoh { get; init; }
    public (System.Net.IPAddress Ip, ushort Port)? Redirect { get; init; }

    public static EngineConfig From(AppSettings settings)
    {
        settings.FilterMode = FilterMode.Global;
        settings.EnableDnsProtect = true;
        settings.EnablePassiveDrop = true;
        settings.QuicMode = QuicMode.BlockAll;
        if (string.IsNullOrWhiteSpace(settings.DnsProvider) || settings.DnsProvider.Equals("off", StringComparison.OrdinalIgnoreCase))
            settings.DnsProvider = "yandex";

        var isp = settings.IspId is "auto" or "" ? IspCatalog.Get("universal") : IspCatalog.Get(settings.IspId);
        var strategyId = settings.StrategyId is "auto" or "" ? isp.DefaultStrategyId : settings.StrategyId;
        var dns = ParseDns(settings);
        var (useDoh, redirect) = ResolveDns(dns);
        return new EngineConfig
        {
            Settings = settings,
            Strategy = StrategyCatalog.Get(strategyId),
            Hosts = new HostMatcher(settings),
            Dns = dns,
            UseDoh = useDoh,
            Redirect = redirect
        };
    }

    private static DnsProviderKind ParseDns(AppSettings settings)
    {
        if (!settings.EnableDnsProtect) return DnsProviderKind.Off;
        return settings.DnsProvider.ToLowerInvariant() switch
        {
            "yandex" => DnsProviderKind.Yandex,
            "google" => DnsProviderKind.Google,
            "quad9" => DnsProviderKind.Quad9,
            "adguard" => DnsProviderKind.AdGuard,
            "mullvad" => DnsProviderKind.Mullvad,
            "doh" or "doh-cloudflare" or "cloudflare-doh" => DnsProviderKind.DohCloudflare,
            "doh-google" => DnsProviderKind.DohGoogle,
            "off" => DnsProviderKind.Off,
            "cloudflare" => DnsProviderKind.Cloudflare,
            _ => DnsProviderKind.Yandex
        };
    }

    private static (bool useDoh, (System.Net.IPAddress, ushort)? redirect) ResolveDns(DnsProviderKind dns) => dns switch
    {
        DnsProviderKind.Yandex => (false, (System.Net.IPAddress.Parse("77.88.8.8"), (ushort)1253)),
        DnsProviderKind.Cloudflare => (false, (System.Net.IPAddress.Parse("1.1.1.1"), (ushort)53)),
        DnsProviderKind.Google => (false, (System.Net.IPAddress.Parse("8.8.8.8"), (ushort)53)),
        DnsProviderKind.Quad9 => (false, (System.Net.IPAddress.Parse("9.9.9.9"), (ushort)53)),
        DnsProviderKind.AdGuard => (false, (System.Net.IPAddress.Parse("94.140.14.14"), (ushort)53)),
        DnsProviderKind.Mullvad => (false, (System.Net.IPAddress.Parse("194.242.2.2"), (ushort)53)),
        DnsProviderKind.DohCloudflare => (true, null),
        DnsProviderKind.DohGoogle => (true, null),
        _ => (false, null)
    };
}
