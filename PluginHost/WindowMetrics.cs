using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoRoRo.UrOcr.PluginHost;

/// <summary>Thin Win32 IWindowMetrics — marshalling only, no logic.</summary>
public sealed class WindowMetrics : IWindowMetrics
{
    public IntPtr HwndForPid(int pid)
    {
        try { return Process.GetProcessById(pid).MainWindowHandle; }
        catch { return IntPtr.Zero; }
    }

    public (int X, int Y)? ClientOrigin(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        var pt = new POINT { x = 0, y = 0 };
        return ClientToScreen(hwnd, ref pt) ? (pt.x, pt.y) : null;
    }

    public (int W, int H)? ClientSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        return GetClientRect(hwnd, out var r) ? (r.right - r.left, r.bottom - r.top) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
}
