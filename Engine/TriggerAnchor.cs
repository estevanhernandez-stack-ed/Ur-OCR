using System.Collections.Generic;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed record AnchorResult(string CoordSpace, RegionRect Region, int? RecordedClientW, int? RecordedClientH);

/// <summary>
/// Pick-time: decide whether a picked screen region is window-anchored. If the
/// region's center falls inside a running alt's client rect, store it
/// client-relative to that window (+ recorded client size); else keep it screen.
/// Pure over IWindowMetrics + the alt pid list.
/// </summary>
public static class TriggerAnchor
{
    public static AnchorResult ForPickedRegion(RegionRect picked, IReadOnlyCollection<int> altPids, IWindowMetrics metrics)
    {
        int cx = picked.X + picked.Width / 2;
        int cy = picked.Y + picked.Height / 2;

        foreach (var pid in altPids)
        {
            var hwnd = metrics.HwndForPid(pid);
            if (hwnd == System.IntPtr.Zero) continue;
            var origin = metrics.ClientOrigin(hwnd);
            var size = metrics.ClientSize(hwnd);
            if (origin is null || size is null) continue;
            var (ox, oy) = origin.Value;
            var (w, h) = size.Value;
            if (cx >= ox && cx < ox + w && cy >= oy && cy < oy + h)
            {
                return new AnchorResult(
                    Trigger.CoordSpaceClient,
                    WindowSpaceMath.ToClientRegion(picked, origin.Value),
                    w, h);
            }
        }
        return new AnchorResult(Trigger.CoordSpaceScreen, picked, null, null);
    }
}
