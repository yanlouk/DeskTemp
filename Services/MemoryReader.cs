using System.Runtime.InteropServices;

namespace DeskTemp.Services;

/// <summary>
/// Reads system memory through the Win32 API without pulling in WMI.
/// </summary>
public static class MemoryReader
{
    private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;

    public static (double? Load, double? UsedGb, double? TotalGb) Read()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return (null, null, null);

        double total = status.ullTotalPhys / BytesPerGb;
        double available = status.ullAvailPhys / BytesPerGb;
        return (status.dwMemoryLoad, Math.Max(0, total - available), total);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
