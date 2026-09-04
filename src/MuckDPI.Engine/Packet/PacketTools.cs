using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace MuckDPI.Engine.Packet;

internal enum IpProtocol : byte
{
    Tcp = 6,
    Udp = 17
}

internal ref struct ParsedPacket
{
    public ReadOnlySpan<byte> Raw;
    public bool IsIPv6;
    public byte Protocol;
    public int IpHeaderLength;
    public int TransportHeaderLength;
    public int PayloadOffset;
    public int PayloadLength;
    public ushort SrcPort;
    public ushort DstPort;
    public uint Seq;
    public uint Ack;
    public ushort IpId;
    public byte Ttl;
    public bool TcpRst;
    public bool TcpSyn;
    public bool TcpAck;
    public bool TcpPsh;
    public bool TcpFin;
    public IPAddress SrcIp;
    public IPAddress DstIp;

    public ReadOnlySpan<byte> Payload => Raw.Slice(PayloadOffset, PayloadLength);
    public bool IsTcp => Protocol == (byte)IpProtocol.Tcp;
    public bool IsUdp => Protocol == (byte)IpProtocol.Udp;

    public static bool TryParse(ReadOnlySpan<byte> raw, out ParsedPacket packet)
    {
        packet = default;
        packet.Raw = raw;
        if (raw.Length < 20) return false;

        var version = raw[0] >> 4;
        if (version == 4)
        {
            var ihl = (raw[0] & 0x0F) * 4;
            if (ihl < 20 || raw.Length < ihl) return false;
            packet.IsIPv6 = false;
            packet.IpHeaderLength = ihl;
            packet.Protocol = raw[9];
            packet.IpId = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(4, 2));
            packet.Ttl = raw[8];
            packet.SrcIp = new IPAddress(raw.Slice(12, 4));
            packet.DstIp = new IPAddress(raw.Slice(16, 4));
        }
        else if (version == 6)
        {
            if (raw.Length < 40) return false;
            packet.IsIPv6 = true;
            packet.IpHeaderLength = 40;
            packet.Protocol = raw[6];
            packet.Ttl = raw[7];
            packet.SrcIp = new IPAddress(raw.Slice(8, 16));
            packet.DstIp = new IPAddress(raw.Slice(24, 16));
        }
        else return false;

        var transport = packet.IpHeaderLength;
        if (packet.Protocol == (byte)IpProtocol.Tcp)
        {
            if (raw.Length < transport + 20) return false;
            packet.SrcPort = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(transport, 2));
            packet.DstPort = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(transport + 2, 2));
            packet.Seq = BinaryPrimitives.ReadUInt32BigEndian(raw.Slice(transport + 4, 4));
            packet.Ack = BinaryPrimitives.ReadUInt32BigEndian(raw.Slice(transport + 8, 4));
            var dataOff = (raw[transport + 12] >> 4) * 4;
            if (dataOff < 20) return false;
            packet.TransportHeaderLength = dataOff;
            var flags = raw[transport + 13];
            packet.TcpFin = (flags & 0x01) != 0;
            packet.TcpSyn = (flags & 0x02) != 0;
            packet.TcpRst = (flags & 0x04) != 0;
            packet.TcpPsh = (flags & 0x08) != 0;
            packet.TcpAck = (flags & 0x10) != 0;
            packet.PayloadOffset = transport + dataOff;
            packet.PayloadLength = Math.Max(0, raw.Length - packet.PayloadOffset);
            return true;
        }

        if (packet.Protocol == (byte)IpProtocol.Udp)
        {
            if (raw.Length < transport + 8) return false;
            packet.SrcPort = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(transport, 2));
            packet.DstPort = BinaryPrimitives.ReadUInt16BigEndian(raw.Slice(transport + 2, 2));
            packet.TransportHeaderLength = 8;
            packet.PayloadOffset = transport + 8;
            packet.PayloadLength = Math.Max(0, raw.Length - packet.PayloadOffset);
            return true;
        }

        return false;
    }
}

internal static class PacketMutator
{
    public static void SetIpv4Length(Span<byte> packet, int totalLength)
    {
        BinaryPrimitives.WriteUInt16BigEndian(packet.Slice(2, 2), (ushort)totalLength);
    }

    public static void SetIpv6PayloadLength(Span<byte> packet, int payloadLength)
    {
        BinaryPrimitives.WriteUInt16BigEndian(packet.Slice(4, 2), (ushort)payloadLength);
    }

    public static void SetTcpSeq(Span<byte> packet, int ipHeaderLength, uint seq)
    {
        BinaryPrimitives.WriteUInt32BigEndian(packet.Slice(ipHeaderLength + 4, 4), seq);
    }

    public static void SetTtl(Span<byte> packet, bool ipv6, byte ttl)
    {
        if (ipv6) packet[7] = ttl;
        else packet[8] = ttl;
    }

    public static void SetTcpChecksum(Span<byte> packet, int ipHeaderLength, ushort checksum)
    {
        BinaryPrimitives.WriteUInt16BigEndian(packet.Slice(ipHeaderLength + 16, 2), checksum);
    }
}

internal static class TlsSni
{
    public static bool TryGetHost(ReadOnlySpan<byte> payload, out string host, out int hostOffset, out int hostLength)
    {
        host = "";
        hostOffset = 0;
        hostLength = 0;
        if (payload.Length < 12) return false;
        if (payload[0] != 0x16) return false; // handshake record
        var recLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));
        if (payload.Length < 5 + recLen && payload.Length < 40) { /* truncated ok */ }

        var hs = 5;
        if (hs >= payload.Length || payload[hs] != 0x01) return false; // client hello
        if (hs + 4 >= payload.Length) return false;
        hs += 4; // handshake header
        if (hs + 34 >= payload.Length) return false;
        hs += 2; // version
        hs += 32; // random
        if (hs >= payload.Length) return false;
        var sidLen = payload[hs];
        hs += 1 + sidLen;
        if (hs + 2 >= payload.Length) return false;
        var csLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs, 2));
        hs += 2 + csLen;
        if (hs >= payload.Length) return false;
        var compLen = payload[hs];
        hs += 1 + compLen;
        if (hs + 2 > payload.Length) return false;
        var extLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs, 2));
        hs += 2;
        var extEnd = Math.Min(payload.Length, hs + extLen);
        while (hs + 4 <= extEnd)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs, 2));
            var len = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs + 2, 2));
            hs += 4;
            if (hs + len > payload.Length) break;
            if (type == 0 && len >= 5)
            {
                var listLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs, 2));
                if (listLen + 2 > len) break;
                var nameType = payload[hs + 2];
                if (nameType == 0 && hs + 5 <= payload.Length)
                {
                    var nameLen = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(hs + 3, 2));
                    hostOffset = hs + 5;
                    hostLength = nameLen;
                    if (hostOffset + hostLength > payload.Length) break;
                    host = Encoding.ASCII.GetString(payload.Slice(hostOffset, hostLength));
                    return host.Length > 0;
                }
            }
            hs += len;
        }
        return false;
    }

    public static int SniSplitOffset(ReadOnlySpan<byte> payload)
    {
        if (TryGetHost(payload, out _, out var hostOffset, out _))
            return hostOffset;
        return payload.Length >= 3 ? 2 : 1;
    }

    public static void ObfuscateHostname(Span<byte> payload, int hostOffset, int hostLength)
    {
        ReadOnlySpan<byte> decoy = "www.cloudflare.com"u8;
        for (var i = 0; i < hostLength; i++)
            payload[hostOffset + i] = decoy[i % decoy.Length];
    }
}

internal static class HttpHost
{
    public static bool TryGetHost(ReadOnlySpan<byte> payload, out string host, out int hostOffset, out int hostLength)
    {
        host = "";
        hostOffset = 0;
        hostLength = 0;
        var text = Encoding.ASCII.GetString(payload);
        var idx = text.IndexOf("\nHost:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = text.IndexOf("\nhost:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var valueStart = idx + 6;
        while (valueStart < text.Length && (text[valueStart] == ' ' || text[valueStart] == '\t' || text[valueStart] == ':'))
            valueStart++;
        var valueEnd = valueStart;
        while (valueEnd < text.Length && text[valueEnd] != '\r' && text[valueEnd] != '\n')
            valueEnd++;
        if (valueEnd <= valueStart) return false;
        host = text[valueStart..valueEnd].Trim();
        hostOffset = valueStart;
        hostLength = host.Length;
        return host.Length > 0;
    }

    public static void Obfuscate(Span<byte> payload)
    {
        var ascii = Encoding.ASCII.GetString(payload);
        var idx = ascii.IndexOf("\nHost:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var h = idx + 1;
        if (h + 4 < payload.Length)
            payload[h + 1] = (byte)'o'; // HOst / HoSt
        if (h + 3 < payload.Length)
            payload[h + 2] = (byte)'S';
    }
}

internal static class QuicInitial
{
    public static bool LooksLikeQuic(ReadOnlySpan<byte> udpPayload)
    {
        if (udpPayload.Length < 8) return false;
        var b = udpPayload[0];
        return (b & 0xC0) == 0xC0; // long header
    }
}
