using System.Runtime.InteropServices;

namespace RoRoRo.UrOcr.PluginHost;

public sealed class ForegroundWatcher(AccountRegistry registry) : RoRoRo.UrOcr.Engine.IForegroundCheck
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    public int GetForegroundPid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public bool IsForegroundAnAlt() => registry.IsAltPid(GetForegroundPid());
}
