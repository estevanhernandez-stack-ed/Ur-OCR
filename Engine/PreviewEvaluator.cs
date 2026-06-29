// Engine/PreviewEvaluator.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// On-demand "what does this region look like right now?" evaluator for the
/// trigger editor's live match meter. Captures the draft region and runs the
/// color matcher's structured Evaluate. Never fires anything — pure read.
/// </summary>
public sealed class PreviewEvaluator(ICaptureSource capture, IColorMatchEngine color)
{
    public ColorMatchResult? EvaluateOnce(RegionRect region, ColorCriteria criteria)
    {
        if (region.Width < 1 || region.Height < 1) return null;
        using var bmp = capture.Capture(region);
        return color.Evaluate(bmp, criteria);
    }
}
