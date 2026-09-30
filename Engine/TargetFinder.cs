using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>One point in measured-client pixels. Ore marks a point on an ore patch. InHud marks ore on
/// one of the game's buttons (HudMask.Contains): a held drag may pass over it (the ore sweep), a press
/// may not, so whatever presses (ClearAt) leaves it out.</summary>
public sealed record FinderTarget(int X, int Y, bool Ore, string Label, bool InHud = false);

/// <summary>
/// Where to clear on a calm frame (spec "Reach, measured, and the ore finder"). Ore first: samples
/// every Pitch / 4 pixels within reach ((RadiusBlocks + 1) blocks of the character), keeps those
/// within the tolerance of a palette colour and clusters adjacent hits (4-connected; a patch needs
/// MinPatchSamples). Each patch gets the grid points (every Pitch, aligned to the character) inside
/// its bounding box with an ore sample within Pitch / 4, so a big block gets a point on each block;
/// a patch smaller than a block both ways also keeps its centre. Ore points within half a block of
/// the character are dropped (the sprite covers that block and reads as ore), and of two ore points
/// closer than half a block the one nearer the character stays. Then stone: a grid every Pitch out
/// to RadiusBlocks blocks around the character, less the points inside an ore patch; the centre
/// point always stays. Points whose outline box would leave the client, or that lie less than
/// EdgeMarginPx inside it, are dropped, as is any stone point on one of the game's buttons
/// (HudMask.Boxes, shared with SweepPath): the Roblox menu, Go to Top, the left icon column, the
/// hotbar and the update timer. The rest of the top strip and the ground right of the hotbar are
/// game. Ore nearest first, then stone nearest first, at most MaxPoints: every one of these can be
/// pressed. After them come the ore points on a button, flagged InHud, nearest first, at most
/// MaxPoints more: a held drag may pass over a button without firing it (owner, 2026-09-30), so the
/// ore sweep takes them, and ClearAt, which presses on each point, must leave them out. Ore on the
/// Auto Mine button (HudMask.ContainsHeld) is dropped outright, since not even a held pointer may go
/// there, and an InHud point within half a block of a pressable one is dropped too. Every
/// coordinate is in the finder's measured client pixels; the frame may be another size and is sampled
/// scaled.
/// </summary>
public static class TargetFinder
{
    public const int MaxPoints = BridgeContract.MaxClearAtPoints;
    /// <summary>A block at Pitch / 4 is about 16 samples; one lone hit is a sparkle, not ore.</summary>
    public const int MinPatchSamples = 2;
    /// <summary>How far every point stays inside the client, the same as the sweep's.</summary>
    public const int EdgeMarginPx = SweepPath.EdgeMarginPx;

    public static IReadOnlyList<FinderTarget> Find(FramePixels frame, FinderSetup f)
    {
        var step = Math.Max(1, f.Pitch / 4);
        var cols = f.ClientW / step;
        var rows = f.ClientH / step;
        int Centre(int cell) => cell * step + step / 2;

        // Ore is looked for only within reach: a block past the grid's edge would be skipped anyway,
        // and a light palette colour (white quartz) would otherwise match HUD panels across the view.
        var reach = (long)(f.RadiusBlocks + 1) * f.Pitch;
        bool InReach(int x, int y) =>
            (long)(x - f.CenterX) * (x - f.CenterX) + (long)(y - f.CenterY) * (y - f.CenterY) <= reach * reach;

        var hit = new bool[cols, rows];
        for (var gy = 0; gy < rows; gy++)
            for (var gx = 0; gx < cols; gx++)
                hit[gx, gy] = InReach(Centre(gx), Centre(gy)) && IsOre(Sample(frame, f, Centre(gx), Centre(gy)), f);

        var inOre = new bool[cols, rows];
        var seen = new bool[cols, rows];
        var ore = new List<(int X, int Y)>();
        var stack = new Stack<(int X, int Y)>();
        for (var gy = 0; gy < rows; gy++)
            for (var gx = 0; gx < cols; gx++)
            {
                if (!hit[gx, gy] || seen[gx, gy]) continue;
                var patch = new List<(int X, int Y)>();
                seen[gx, gy] = true;
                stack.Push((gx, gy));
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    patch.Add((x, y));
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || seen[nx, ny] || !hit[nx, ny]) continue;
                        seen[nx, ny] = true;
                        stack.Push((nx, ny));
                    }
                }
                if (patch.Count < MinPatchSamples) continue;
                foreach (var (x, y) in patch) inOre[x, y] = true;
                ore.AddRange(PatchPoints(patch.Select(p => (Centre(p.X), Centre(p.Y))).ToList(), f, step));
            }

        var stone = new List<(int X, int Y)>();
        var r = f.RadiusBlocks;
        for (var j = -r; j <= r; j++)
            for (var i = -r; i <= r; i++)
            {
                if (i * i + j * j > r * r) continue;
                var x = f.CenterX + i * f.Pitch;
                var y = f.CenterY + j * f.Pitch;
                if (!Clickable(x, y, f)) continue;
                int cx = x / step, cy = y / step;
                var centre = i == 0 && j == 0;   // the block under the character: ore points skip it, so stone keeps it
                if (!centre && cx < cols && cy < rows && inOre[cx, cy]) continue;
                stone.Add((x, y));
            }

        var halfBlock2 = (long)f.Pitch * f.Pitch / 4;
        var offCentre = ore.Where(p => Dist2(p, f.CenterX, f.CenterY) > halfBlock2).ToList();
        var kept = new List<(int X, int Y)>();
        foreach (var p in Nearest(offCentre.Where(p => Clickable(p.X, p.Y, f)), f))
            if (kept.All(k => Dist2(p, k.X, k.Y) >= halfBlock2)) kept.Add(p);
        var held = new List<(int X, int Y)>();
        foreach (var p in Nearest(offCentre.Where(p => HeldOnly(p.X, p.Y, f)), f))
            if (kept.Concat(held).All(k => Dist2(p, k.X, k.Y) >= halfBlock2)) held.Add(p);

        return kept
            .Select((p, n) => new FinderTarget(p.X, p.Y, true, $"ore {n + 1}"))
            .Concat(Nearest(stone, f).Select((p, n) => new FinderTarget(p.X, p.Y, false, $"stone {n + 1}")))
            .Take(MaxPoints)
            .Concat(held.Take(MaxPoints).Select((p, n) => new FinderTarget(p.X, p.Y, true, $"ore on a button {n + 1}", InHud: true)))
            .ToList();
    }

    /// <summary>A patch's candidate points: the character-aligned grid points inside its bounding box
    /// with one of its samples within Pitch / 4, plus its centre when it is smaller than a block both
    /// ways (a small patch can sit between grid points). Samples are in measured-client pixels.</summary>
    private static IEnumerable<(int X, int Y)> PatchPoints(List<(int X, int Y)> samples, FinderSetup f, int near)
    {
        int minX = samples.Min(s => s.X), maxX = samples.Max(s => s.X);
        int minY = samples.Min(s => s.Y), maxY = samples.Max(s => s.Y);
        var near2 = (long)near * near;

        for (var gy = f.CenterY + CeilDiv(minY - f.CenterY, f.Pitch) * f.Pitch; gy <= maxY; gy += f.Pitch)
            for (var gx = f.CenterX + CeilDiv(minX - f.CenterX, f.Pitch) * f.Pitch; gx <= maxX; gx += f.Pitch)
            {
                int x = gx, y = gy;
                if (samples.Any(s => Dist2((x, y), s.X, s.Y) <= near2)) yield return (x, y);
            }

        if (maxX - minX < f.Pitch && maxY - minY < f.Pitch)
            yield return (Round(samples.Average(s => s.X)), Round(samples.Average(s => s.Y)));
    }

    /// <summary>The outline box fits, the point is EdgeMarginPx inside the client, and it is off the HUD.</summary>
    private static bool Clickable(int x, int y, FinderSetup f) =>
        f.BoxFits(x, y)
        && x >= EdgeMarginPx && y >= EdgeMarginPx && x <= f.ClientW - 1 - EdgeMarginPx && y <= f.ClientH - 1 - EdgeMarginPx
        && !HudMask.Contains(x, y, f.ClientW, f.ClientH);

    /// <summary>EdgeMarginPx inside the client and on a button, but not the Auto Mine button: a held
    /// drag may pass over it, a press may not.</summary>
    private static bool HeldOnly(int x, int y, FinderSetup f) =>
        x >= EdgeMarginPx && y >= EdgeMarginPx && x <= f.ClientW - 1 - EdgeMarginPx && y <= f.ClientH - 1 - EdgeMarginPx
        && HudMask.Contains(x, y, f.ClientW, f.ClientH)
        && !HudMask.ContainsHeld(x, y, f.ClientW, f.ClientH);

    private static int CeilDiv(int a, int b) => (int)Math.Ceiling((double)a / b);

    private static long Dist2((int X, int Y) p, int x, int y) =>
        (long)(p.X - x) * (p.X - x) + (long)(p.Y - y) * (p.Y - y);

    private static IEnumerable<(int X, int Y)> Nearest(IEnumerable<(int X, int Y)> points, FinderSetup f) =>
        points
            .OrderBy(p => (long)(p.X - f.CenterX) * (p.X - f.CenterX) + (long)(p.Y - f.CenterY) * (p.Y - f.CenterY))
            .ThenBy(p => p.Y)
            .ThenBy(p => p.X);

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static Rgb Sample(FramePixels frame, FinderSetup f, int x, int y)
    {
        var fx = Math.Clamp((int)((long)x * frame.Width / f.ClientW), 0, frame.Width - 1);
        var fy = Math.Clamp((int)((long)y * frame.Height / f.ClientH), 0, frame.Height - 1);
        return frame.At(fx, fy);
    }

    private static bool IsOre(Rgb c, FinderSetup f)
    {
        foreach (var o in f.Ore)
            if (ColorMatcher.Distance(c, o.Rgb) <= f.OreToleranceRgb) return true;
        return false;
    }
}
