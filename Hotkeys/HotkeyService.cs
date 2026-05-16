using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace RoRoRo.UrOcr.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _src;
    private int _nextId = 9000;
    private readonly Dictionary<int, Action> _handlers = new();

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        _src = HwndSource.FromHwnd(_hwnd);
        _src!.AddHook(WndProc);
    }

    public void RegisterPauseAll(Key key, Action onPress)
    {
        var id = _nextId++;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_hwnd, id, 0, vk))
            throw new InvalidOperationException($"RegisterHotKey failed for {key}");
        _handlers[id] = onPress;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            h();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) UnregisterHotKey(_hwnd, id);
        _handlers.Clear();
        _src?.RemoveHook(WndProc);
    }
}
