using System.Runtime.InteropServices;
using System.Windows.Input;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed class KeySender : IKeyPress
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int sz);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

    public int HoldMs { get; set; } = 30;

    public void Press(KeyCombo combo)
    {
        var keys = new List<Key>();
        foreach (var m in combo.Modifiers) keys.Add(ParseModifier(m));
        keys.Add(ParseKey(combo.Key));

        var down = keys.Select(k => MakeInput(k, false));
        var up = keys.AsEnumerable().Reverse().Select(k => MakeInput(k, true));

        SendInput((uint)keys.Count, down.ToArray(), Marshal.SizeOf<INPUT>());
        Thread.Sleep(HoldMs);
        SendInput((uint)keys.Count, up.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeInput(Key k, bool keyUp)
    {
        var vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        var scan = (ushort)MapVirtualKey(vk, 0);
        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = KEYEVENTF_SCANCODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                }
            }
        };
    }

    private static Key ParseKey(string s) => (Key)Enum.Parse(typeof(Key), s, ignoreCase: true);

    private static Key ParseModifier(string m) => m.ToLowerInvariant() switch
    {
        "ctrl" => Key.LeftCtrl,
        "shift" => Key.LeftShift,
        "alt" => Key.LeftAlt,
        "win" => Key.LWin,
        _ => throw new ArgumentException($"Unknown modifier: {m}"),
    };
}
