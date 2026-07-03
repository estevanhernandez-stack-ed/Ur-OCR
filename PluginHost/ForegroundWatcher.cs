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

    /// <summary>
    /// The alt pid that was most recently in the foreground. The editor's live
    /// meter previews against this so it follows the window you last looked at
    /// (while editing, the Ur-OCR window itself is foreground, so this "sticks"
    /// to the last alt). 0 until an alt has been focused at least once.
    /// </summary>
    public int LastForegroundAltPid { get; private set; }

    /// <summary>Poll the foreground; if it's an alt, remember it. Cheap — one
    /// GetForegroundWindow call. Meant to be ticked a few times a second.</summary>
    public void CaptureForegroundAlt()
    {
        var pid = GetForegroundPid();
        if (registry.IsAltPid(pid)) LastForegroundAltPid = pid;
    }
}
