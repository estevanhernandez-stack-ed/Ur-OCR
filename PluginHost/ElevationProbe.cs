using System.Runtime.InteropServices;

namespace RoRoRo.UrOcr.PluginHost;

public sealed class ElevationProbe : RoRoRo.UrOcr.Engine.IElevationCheck
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_ACCESS_DENIED = 5;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    public bool IsForegroundProcessLikelyElevated(int pid)
    {
        if (pid <= 0) return false;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero)
            return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;
        CloseHandle(h);
        return false;
    }
}
