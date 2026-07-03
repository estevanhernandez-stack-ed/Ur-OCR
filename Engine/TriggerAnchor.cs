using System.Collections.Generic;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

public sealed record AnchorResult(string CoordSpace, RegionRect Region, int? RecordedClientW, int? RecordedClientH);

/// <summary>
/// Pick-time: decide whether a picked screen region is window-anchored. Anchors
/// to the running alt whose client rect OVERLAPS the picked region the most
/// (any overlap counts) — so a region drawn over or straddling a game window
/// anchors to it. Only when the region overlaps no alt window at all does it
/// stay screen. (Center-only hit-tests missed edge picks.) Pure over
/// IWindowMetrics + the alt pid list.
/// </summary>
public static class TriggerAnchor
{
    public static AnchorResult ForPickedRegion(RegionRect picked, IReadOnlyCollection<int> altPids, IWindowMetrics metrics)
    {
        int bestArea = 0;
        (int X, int Y)? bestOrigin = null;
        (int W, int H)? bestSize = null;

        foreach (var pid in altPids)
        {
            var hwnd = metrics.HwndForPid(pid);
            if (hwnd == System.IntPtr.Zero) continue;
            var origin = metrics.ClientOrigin(hwnd);
            var size = metrics.ClientSize(hwnd);
            if (origin is null || size is null) continue;
            var (ox, oy) = origin.Value;
            var (w, h) = size.Value;

            // Intersection area of the picked region with this window's client rect.
            int ix = System.Math.Max(picked.X, ox);
            int iy = System.Math.Max(picked.Y, oy);
            int ir = System.Math.Min(picked.X + picked.Width, ox + w);
            int ib = System.Math.Min(picked.Y + picked.Height, oy + h);
            int area = System.Math.Max(0, ir - ix) * System.Math.Max(0, ib - iy);

            if (area > bestArea)
            {
                bestArea = area;
                bestOrigin = origin;
                bestSize = size;
            }
        }

        if (bestArea > 0 && bestOrigin is { } bo && bestSize is { } bs)
        {
            return new AnchorResult(
                Trigger.CoordSpaceClient,
                WindowSpaceMath.ToClientRegion(picked, bo),
                bs.W, bs.H);
        }
        return new AnchorResult(Trigger.CoordSpaceScreen, picked, null, null);
    }
}
