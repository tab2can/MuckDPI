using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using MuckDPI.Engine.Packet;

namespace MuckDPI.Engine.DnsProtect;

/// <summary>
/// GoodbyeDPI-Turkey style DNS NAT: rewrite UDP/53 to a resolver on a non-standard
/// port (Yandex 77.88.8.8:1253) so ISP port-53 hijack does not see the query, then
/// restore the original server address on the reply so Windows accepts it.
/// </summary>
internal sealed class DnsNat
{
    private readonly ConcurrentDictionary<string, Rec> _map = new();
    private readonly IPAddress _target;
    private readonly ushort _port;
    private readonly byte[] _targetBytes;

    public DnsNat(IPAddress target, ushort port)
    {
        _target = target;
        _port = port;
        _targetBytes = target.GetAddressBytes();
    }

    public ushort Port => _port;
    public IPAddress Target => _target;

    public bool TryRewriteOutbound(byte[] buffer, int length, in ParsedPacket pkt)
    {
        if (!pkt.IsUdp || pkt.DstPort != 53) return false;
        if (pkt.IsIPv6 != (_target.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6))
            return false;

        var key = Key(pkt.SrcIp, pkt.SrcPort);
        _map[key] = new Rec(pkt.DstIp, pkt.DstPort, DateTime.UtcNow);

        var span = buffer.AsSpan(0, length);
        if (pkt.IsIPv6) PacketMutator.SetIpv6Dst(span, _targetBytes);
        else PacketMutator.SetIpv4Dst(span, _targetBytes);
        PacketMutator.SetUdpDstPort(span, pkt.IpHeaderLength, _port);
        return true;
    }

    public bool TryRewriteInbound(byte[] buffer, int length, in ParsedPacket pkt)
    {
        if (!pkt.IsUdp || pkt.SrcPort != _port) return false;
        var key = Key(pkt.DstIp, pkt.DstPort);
        if (!_map.TryRemove(key, out var rec)) return false;

        var span = buffer.AsSpan(0, length);
        var orig = rec.OrigDst.GetAddressBytes();
        if (pkt.IsIPv6) PacketMutator.SetIpv6Src(span, orig);
        else PacketMutator.SetIpv4Src(span, orig);
        PacketMutator.SetUdpSrcPort(span, pkt.IpHeaderLength, rec.OrigPort);
        return true;
    }

    public void Sweep()
    {
        var cut = DateTime.UtcNow.AddSeconds(-30);
        foreach (var kv in _map)
        {
            if (kv.Value.Utc < cut)
                _map.TryRemove(kv.Key, out _);
        }
    }

    private static string Key(IPAddress ip, ushort port) => $"{ip}:{port}";

    private readonly record struct Rec(IPAddress OrigDst, ushort OrigPort, DateTime Utc);
}

internal static class WindowsDns
{
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
    private static extern uint DnsFlushResolverCache();

    public static void Flush()
    {
        try { DnsFlushResolverCache(); }
        catch { /* best-effort */ }
        _ = Task.Run(NudgeBrowsers);
    }

    /// <summary>
    /// Brief loopback address flap so Chrome/Edge treat it as a network change
    /// and drop their DNS + HTTP/3 socket pools without a full restart.
    /// </summary>
    private static void NudgeBrowsers()
    {
        try
        {
            var idx = 1;
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;
                idx = nic.GetIPProperties().GetIPv4Properties()?.Index ?? 1;
                break;
            }
            var dummy = "169.254.253.17";
            RunNetsh($"interface ipv4 add address {idx} {dummy} 255.255.255.255");
            Thread.Sleep(180);
            RunNetsh($"interface ipv4 delete address {idx} {dummy}");
        }
        catch
        {
            // best-effort
        }
    }

    private static void RunNetsh(string args)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        p?.WaitForExit(4000);
    }
}
