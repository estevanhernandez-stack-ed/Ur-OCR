namespace RoRoRo.UrOcr.PluginHost;

/// <summary>
/// Window geometry seam for window-anchored trigger regions. Device pixels,
/// matching the capture/picker coordinate space. Null returns = window gone /
/// call failed; callers skip, never crash. Mirrors Ur Task's IWindowMetrics
/// (subset — Ur-OCR only reads, never resizes).
/// </summary>
public interface IWindowMetrics
{
    System.IntPtr HwndForPid(int pid);
    (int X, int Y)? ClientOrigin(System.IntPtr hwnd);
    (int W, int H)? ClientSize(System.IntPtr hwnd);
}
