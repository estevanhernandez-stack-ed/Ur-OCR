using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Pure: the absolute screen region to capture for a trigger. screen triggers
/// return their stored rect; client triggers resolve against the anchor pid's
/// window (origin + size) and scale. Null = a client trigger whose anchor
/// window can't be resolved (no alt / window gone) — callers skip.
/// </summary>
public static class TriggerRegionResolver
{
    public static RegionRect? Resolve(Trigger trig, int anchorPid, IWindowMetrics metrics)
    {
        if (!trig.IsClientSpace) return trig.Region;
        if (anchorPid == 0) return null;
        if (trig.RecordedClientW is not int rw || trig.RecordedClientH is not int rh) return null;

        var hwnd = metrics.HwndForPid(anchorPid);
        if (hwnd == System.IntPtr.Zero) return null;
        var origin = metrics.ClientOrigin(hwnd);
        var size = metrics.ClientSize(hwnd);
        if (origin is null || size is null) return null;

        return WindowSpaceMath.ToScreenRegion(trig.Region, origin.Value, (rw, rh), size.Value);
    }
}
