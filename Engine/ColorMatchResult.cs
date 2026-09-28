// Engine/ColorMatchResult.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// One colour check. Target check: Distance is to the target. None-of check:
/// Distance is to the nearest listed colour, which is <see cref="Nearest"/>
/// (null when the list was empty).
/// </summary>
public sealed record ColorMatchResult(Rgb Sampled, double Distance, bool Matched,
    double? DistanceToOther = null, Rgb? Nearest = null);
