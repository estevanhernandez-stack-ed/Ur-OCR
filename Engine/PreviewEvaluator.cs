// Engine/PreviewEvaluator.cs
using System.Linq;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Live match meter for the editor. For screen triggers, samples the absolute
/// region. For client triggers, anchors to the FIRST running alt (the editor
/// window is foreground while you edit, so we can't use the foreground alt) so
/// the meter works during setup. Pure read — never fires.
/// </summary>
public sealed class PreviewEvaluator(
    ICaptureSource capture, IColorMatchEngine color,
    IWindowMetrics metrics, AccountRegistry accounts)
{
    public ColorMatchResult? EvaluateTrigger(Trigger trig)
    {
        if (trig.Mode != TriggerMode.Color || trig.Color is null) return null;
        var anchorPid = trig.IsClientSpace ? accounts.Pids.FirstOrDefault() : 0;
        var region = TriggerRegionResolver.Resolve(trig, anchorPid, metrics);
        if (region is null || region.Width < 1 || region.Height < 1) return null;
        using var bmp = capture.Capture(region);
        return color.Evaluate(bmp, trig.Color);
    }
}
