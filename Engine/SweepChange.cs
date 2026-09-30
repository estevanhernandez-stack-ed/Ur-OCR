using RoRoRo.UrOcr.Ipc;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Whether a sweep broke anything (ore-stop sweep spec: "A sweep that broke nothing (the frame barely
/// changed) counts as an empty pass"): the calm frame the pass was planned on against the next calm
/// frame, at every block the path held the button over except the start block, where the pointer
/// rests after the release and its hover outline shows. A block changed when the average of a box
/// around its point moved by more than <see cref="MinChangeSum"/>, the three channel differences
/// summed (0 to 765, the scale of the 2026-09-29 drag run: broken blocks moved 45 to 101, noise and
/// blocks the pointer never passed 0). Points are in measured-client pixels; either frame may be
/// another size and is sampled scaled. Power balls from a special pickaxe also break blocks, so a
/// change is not proof the pointer did it (spec "Later inputs" 5). Pure.
/// </summary>
public static class SweepChange
{
    public const int MinChangeSum = 30;

    /// <summary>How many swept blocks changed between the two frames.</summary>
    public static int Count(FramePixels before, FramePixels after, IReadOnlyList<SweepPoint> path, int clientW, int clientH, int pitch)
    {
        var half = Math.Max(1, pitch / 8);    // a box a quarter block wide around each point
        return Swept(path).Count(p =>
        {
            var (r0, g0, b0) = Average(before, p, half, clientW, clientH);
            var (r1, g1, b1) = Average(after, p, half, clientW, clientH);
            return Math.Abs(r0 - r1) + Math.Abs(g0 - g1) + Math.Abs(b0 - b1) > MinChangeSum;
        });
    }

    /// <summary>How many blocks <see cref="Count"/> looks at: each swept block once, the start left out.</summary>
    public static int Points(IReadOnlyList<SweepPoint> path) => Swept(path).Count();

    private static IEnumerable<SweepPoint> Swept(IReadOnlyList<SweepPoint> path) =>
        path.Count == 0 ? Enumerable.Empty<SweepPoint>() : path.Where(p => p != path[0]).Distinct();

    private static (double R, double G, double B) Average(FramePixels f, SweepPoint p, int half, int clientW, int clientH)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (var dy = -half; dy <= half; dy++)
            for (var dx = -half; dx <= half; dx++)
            {
                var x = Math.Clamp((int)((long)(p.X + dx) * f.Width / clientW), 0, f.Width - 1);
                var y = Math.Clamp((int)((long)(p.Y + dy) * f.Height / clientH), 0, f.Height - 1);
                var c = f.At(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
                n++;
            }
        return ((double)r / n, (double)g / n, (double)b / n);
    }
}
