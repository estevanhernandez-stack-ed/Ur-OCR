// Engine/PreviewEvaluator.cs
using System;
using RoRoRo.UrOcr.PluginHost;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Live match meter for the editor. For screen triggers, samples the absolute
/// region. For client triggers, anchors to the pid the provider hands it — the
/// alt you last focused (the editor window is foreground while you edit, so we
/// can't read "the foreground alt" live; we remember the last one instead). If
/// you haven't focused an alt yet the provider falls back to the first running
/// alt. Pure read — never fires.
/// </summary>
public sealed class PreviewEvaluator(
    ICaptureSource capture, IColorMatchEngine color,
    IWindowMetrics metrics, Func<int> anchorPidProvider)
{
    public ColorMatchResult? EvaluateTrigger(Trigger trig)
    {
        if (trig.Mode != TriggerMode.Color || trig.Color is null) return null;
        var anchorPid = trig.IsClientSpace ? anchorPidProvider() : 0;
        var region = TriggerRegionResolver.Resolve(trig, anchorPid, metrics);
        if (region is null || region.Width < 1 || region.Height < 1) return null;
        using var bmp = capture.Capture(region);
        return color.Evaluate(bmp, trig.Color);
    }
}
