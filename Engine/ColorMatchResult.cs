// Engine/ColorMatchResult.cs
using RoRoRo.UrOcr.Storage;
namespace RoRoRo.UrOcr.Engine;

public sealed record ColorMatchResult(Rgb Sampled, double Distance, bool Matched);
