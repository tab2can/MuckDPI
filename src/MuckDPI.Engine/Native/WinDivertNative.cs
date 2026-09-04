using System.Runtime.InteropServices;

namespace MuckDPI.Engine.Native;

internal static class WinDivertNative
{
    public const int AddressSize = 80;
    public const int LayerNetwork = 0;
    public const ulong HelperNoIpChecksum = 1;
    public const ulong HelperNoTcpChecksum = 8;
    public const ulong HelperNoUdpChecksum = 16;

    [StructLayout(LayoutKind.Sequential, Size = AddressSize)]
    public struct Address
    {
        public long Timestamp;
        public uint Packed;
        public uint Reserved2;
        public uint IfIdx;
        public uint SubIfIdx;
        private unsafe fixed byte _pad[56];

        public byte Layer
        {
            get => (byte)(Packed & 0xFF);
            set => Packed = (Packed & 0xFFFFFF00) | value;
        }

        public bool Outbound
        {
            get => ((Packed >> 17) & 1) != 0;
            set => Packed = value ? Packed | (1u << 17) : Packed & ~(1u << 17);
        }

        public bool IPv6
        {
            get => ((Packed >> 20) & 1) != 0;
            set => Packed = value ? Packed | (1u << 20) : Packed & ~(1u << 20);
        }

        public bool Impostor
        {
            get => ((Packed >> 19) & 1) != 0;
            set => Packed = value ? Packed | (1u << 19) : Packed & ~(1u << 19);
        }
    }

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern nint WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertRecv(nint handle, nint packet, uint packetLen, out uint recvLen, ref Address addr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertSend(nint handle, nint packet, uint packetLen, out uint sendLen, ref Address addr);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertClose(nint handle);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertShutdown(nint handle, int how);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertSetParam(nint handle, int param, ulong value);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern bool WinDivertHelperCalcChecksums(nint packet, uint packetLen, ref Address addr, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern bool WinDivertHelperCompileFilter(string filter, int layer, nint obj, uint objLen, out nint errorStr, out uint errorPos);
}
