namespace RoRoRo.UrOcr.Storage;

/// <summary>An ore colour the finder looks for, named for the measured file and the log.</summary>
public sealed record OreColour(string Name, Rgb Rgb);

/// <summary>The outline check Ur Task runs at each point: a W x H box centred on the point, passing
/// at MinCount or more pixels whose every channel is at least WhiteMin. Spec values: 60 and 225.</summary>
public sealed record OutlineBox(int W, int H, int MinCount = 60, int WhiteMin = 225);

/// <summary>
/// The ore finder for one layer of a ring (spec "Reach, measured, and the ore finder"), in pixels of
/// the client it was measured in (ClientW x ClientH, the measured file's recorded size, which is what
/// ClearAt sends as its client). Pitch is one block at that layer; the grid reaches RadiusBlocks
/// blocks around the character's centre. Stored in triggers.json on the ring; imported from the
/// measured file's "finders".
/// </summary>
public sealed record FinderSetup(string Layer, int ClientW, int ClientH, int Pitch, int CenterX, int CenterY,
    int RadiusBlocks, OutlineBox Outline, IReadOnlyList<OreColour> Ore, int OreToleranceRgb)
{
    public const int DefaultRadiusBlocks = 5;
    /// <summary>Samples are taken every Pitch / 4 pixels: 4 is the smallest pitch with a 1 px step.</summary>
    public const int MinPitch = 4;
    public const int MaxRadiusBlocks = 20;
    /// <summary>Ur Task refuses a larger outline box (its OutlineCheck.MaxSide, 240 so a block in a
    /// one-block shaft, about 170 px, still fits).</summary>
    public const int MaxOutlineSide = 240;

    /// <summary>Null when ClearAt can use it, else one sentence naming the first problem. Covers every
    /// refusal Ur Task makes on the outline, so a stored finder never sends one.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Layer)) return "layer is missing.";
        if (ClientW < 1 || ClientH < 1) return "the client size must be positive.";
        var maxPitch = Math.Min(ClientW, ClientH);
        if (Pitch < MinPitch || Pitch > maxPitch) return $"pitch must be {MinPitch} to {maxPitch} pixels, not {Pitch}.";
        if (RadiusBlocks < 1 || RadiusBlocks > MaxRadiusBlocks)
            return $"radiusBlocks must be 1 to {MaxRadiusBlocks}, not {RadiusBlocks}.";
        if (Outline is null) return "outline is missing.";
        if (Outline.W < 1 || Outline.H < 1 || Outline.W > MaxOutlineSide || Outline.H > MaxOutlineSide)
            return $"outline must be 1 to {MaxOutlineSide} pixels a side, not {Outline.W}x{Outline.H}.";
        if (Outline.MinCount < 1 || Outline.MinCount > Outline.W * Outline.H)
            return $"outline.minCount must be 1 to {Outline.W * Outline.H} (the box's pixels), not {Outline.MinCount}.";
        if (Outline.WhiteMin < 1 || Outline.WhiteMin > 255)
            return $"outline.whiteMin must be 1 to 255, not {Outline.WhiteMin}.";
        if (!BoxFits(CenterX, CenterY))
            return $"The centre ({CenterX}, {CenterY}) and its {Outline.W}x{Outline.H} outline box must sit inside the {ClientW}x{ClientH} game area.";
        if (Ore is null) return "ore is missing (use [] for none).";
        foreach (var c in Ore)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Name)) return "Every ore colour needs a name.";
            if (c.Rgb is null || !ColorCriteria.InRange(c.Rgb)) return $"Ore colour {c.Name} has a channel outside 0 to 255.";
        }
        if (OreToleranceRgb < 1 || OreToleranceRgb > ColorCriteria.MaxTolerance)
            return $"oreToleranceRgb must be 1 to {ColorCriteria.MaxTolerance}, not {OreToleranceRgb}.";
        return null;
    }

    /// <summary>True when the outline box centred on (x, y) lies wholly inside the client. Ur Task
    /// refuses the whole ClearAt otherwise (its PointMath.InsideClient over CheckBox(-(W / 2), -(H / 2), W, H)).</summary>
    public bool BoxFits(int x, int y)
    {
        var left = x - Outline.W / 2;
        var top = y - Outline.H / 2;
        return left >= 0 && top >= 0 && left + Outline.W <= ClientW && top + Outline.H <= ClientH;
    }
}
