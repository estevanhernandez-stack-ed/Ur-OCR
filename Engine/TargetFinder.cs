using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>One ClearAt point in measured-client pixels. Ore marks an ore patch's centre.</summary>
public sealed record FinderTarget(int X, int Y, bool Ore, string Label);

/// <summary>
/// Where to clear on a calm frame (spec "Reach, measured, and the ore finder"). Ore first: samples
/// every Pitch / 4 pixels within reach ((RadiusBlocks + 1) blocks of the character), keeps those
/// within the tolerance of a palette colour, clusters adjacent hits (4-connected; a patch needs
/// MinPatchSamples) and takes each patch's centre. Then stone: a grid
/// every Pitch out to RadiusBlocks blocks around the character, less the points inside an ore patch.
/// Points whose outline box would leave the client are dropped. Ore nearest first, then stone nearest
/// first, at most MaxPoints. Every coordinate is in the finder's measured client pixels; the frame
/// may be another size and is sampled scaled.
/// </summary>
public static class TargetFinder
{
    public const int MaxPoints = BridgeContract.MaxClearAtPoints;
    /// <summary>A block at Pitch / 4 is about 16 samples; one lone hit is a sparkle, not ore.</summary>
    public const int MinPatchSamples = 2;

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
                ore.Add((Round(patch.Average(p => Centre(p.X))), Round(patch.Average(p => Centre(p.Y)))));
            }

        var stone = new List<(int X, int Y)>();
        var r = f.RadiusBlocks;
        for (var j = -r; j <= r; j++)
            for (var i = -r; i <= r; i++)
            {
                if (i * i + j * j > r * r) continue;
                var x = f.CenterX + i * f.Pitch;
                var y = f.CenterY + j * f.Pitch;
                if (!f.BoxFits(x, y)) continue;
                int cx = x / step, cy = y / step;
                if (cx < cols && cy < rows && inOre[cx, cy]) continue;
                stone.Add((x, y));
            }

        return Nearest(ore.Where(p => f.BoxFits(p.X, p.Y)), f)
            .Select((p, n) => new FinderTarget(p.X, p.Y, true, $"ore {n + 1}"))
            .Concat(Nearest(stone, f).Select((p, n) => new FinderTarget(p.X, p.Y, false, $"stone {n + 1}")))
            .Take(MaxPoints)
            .ToList();
    }

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
