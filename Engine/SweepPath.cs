using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// The sweep's path (ore-stop sweep spec, "The path"): square rings around the character, one block
/// apart, from ring 1 out to <see cref="Rings"/>, in spiral order, starting and ending on the start
/// block east of the character (beside it, never under it). Cells are block units around the centre,
/// j growing down the screen. The spiral goes right 1, up 1, left 2, down 2, right 3, ... so ring k
/// starts at (k, k - 1), ends at (k, k), and the next ring starts one block right, outward.
/// <para>A cell is swept when it is not the centre, its point lies <see cref="EdgeMarginPx"/> inside
/// the client, and it is not in the HUD; the path jumps over the rest with the button held. With
/// nearSide, rows continue below ring 4 (the camera side) across the ring's width, back and forth,
/// down to the last row inside the bottom margin; the other three sides stay at ring 4 (owner input
/// 6). When skipped cells leave a straight move that would pass through the centre block, one detour
/// cell goes in first (the shortest that clears it both ways), or the point is dropped. At most
/// <see cref="MaxPoints"/>, the closing return to the start block included. Every point is in the
/// measured client's pixels, like the finder's. Pure.</para>
/// </summary>
public static class SweepPath
{
    /// <summary>Owner decision 3: out to 4 blocks, slightly tighter than reach (about 5 to 6).</summary>
    public const int Rings = 4;
    /// <summary>How far every point stays inside the client (owner input 6: the near-side rows stop
    /// this far short of the bottom edge).</summary>
    public const int EdgeMarginPx = 12;
    public const int MaxPoints = BridgeContract.MaxSweepPoints;
    /// <summary>The start block, one other block, and the start block again.</summary>
    public const int MinPoints = 3;

    /// <summary>A pass's path: its block size and centre, the finder's client, the game's HUD skipped.</summary>
    public static IReadOnlyList<SweepPoint> Build(FinderSetup pass, bool nearSide) =>
        Build(pass.CenterX, pass.CenterY, pass.Pitch, pass.ClientW, pass.ClientH, nearSide,
            (x, y) => HudMask.Contains(x, y, pass.ClientW, pass.ClientH));

    /// <summary>The path, or empty when the start block is outside the game area or fewer than
    /// <see cref="MinPoints"/> points fit.</summary>
    public static IReadOnlyList<SweepPoint> Build(int centerX, int centerY, int pitch, int clientW, int clientH,
        bool nearSide, Func<int, int, bool> masked)
    {
        if (pitch < 1) return Array.Empty<SweepPoint>();
        (int X, int Y) At((int I, int J) c) => (centerX + c.I * pitch, centerY + c.J * pitch);
        bool Valid((int I, int J) c)
        {
            if (c == (0, 0)) return false;
            var (x, y) = At(c);
            return x >= EdgeMarginPx && y >= EdgeMarginPx && x <= clientW - 1 - EdgeMarginPx && y <= clientH - 1 - EdgeMarginPx
                   && !masked(x, y);
        }

        (int I, int J) start = (1, 0);
        if (!Valid(start)) return Array.Empty<SweepPoint>();

        var order = Spiral(Rings);
        if (nearSide) order.AddRange(NearSideRows(Rings, (clientH - 1 - EdgeMarginPx - centerY) / pitch));
        var cells = order.Where(Valid).ToList();     // cells[0] is the start: the spiral begins on it

        var path = new List<(int I, int J)> { start };
        foreach (var c in cells.Skip(1))
        {
            if (path.Count > MaxPoints - 4) break;    // room for a detour here and one on the way home
            Append(path, c, cells);
        }
        while (path.Count > 1 && !Append(path, start, cells)) path.RemoveAt(path.Count - 1);
        if (path.Count < MinPoints) return Array.Empty<SweepPoint>();

        return path.Select(c => { var (x, y) = At(c); return new SweepPoint(x, y); }).ToList();
    }

    /// <summary>True when the straight move from a to b passes through the centre block's inside (the
    /// square of half a block around (0, 0)). Touching a corner is not passing through. Liang-Barsky
    /// clipping of the segment against that square.</summary>
    internal static bool CrossesCentre((int I, int J) a, (int I, int J) b)
    {
        double t0 = 0, t1 = 1;
        double dx = b.I - a.I, dy = b.J - a.J;
        foreach (var (p, q) in new[] { (-dx, a.I + 0.5), (dx, 0.5 - a.I), (-dy, a.J + 0.5), (dy, 0.5 - a.J) })
        {
            if (p == 0)
            {
                if (q < 0) return false;
                continue;
            }
            var t = q / p;
            if (p < 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 > t1) return false;
        }
        return t1 - t0 > 1e-9;
    }

    /// <summary>Rings 1 to <paramref name="rings"/> in spiral order from (1, 0).</summary>
    private static List<(int I, int J)> Spiral(int rings)
    {
        var cells = new List<(int I, int J)>();
        var total = (2 * rings + 1) * (2 * rings + 1) - 1;
        var dirs = new (int DI, int DJ)[] { (1, 0), (0, -1), (-1, 0), (0, 1) };   // right, up, left, down
        int i = 0, j = 0, leg = 1;
        for (var d = 0; cells.Count < total; d++)
        {
            var (di, dj) = dirs[d % 4];
            for (var s = 0; s < leg && cells.Count < total; s++)
            {
                i += di;
                j += dj;
                cells.Add((i, j));
            }
            if (d % 2 == 1) leg++;
        }
        return cells;
    }

    /// <summary>Rows below the last ring, rings + 1 to <paramref name="lastRow"/>, across the ring's
    /// width: the first right to left (it starts under ring 4's last block, (rings, rings)), then back
    /// and forth.</summary>
    private static IEnumerable<(int I, int J)> NearSideRows(int rings, int lastRow)
    {
        for (int j = rings + 1, n = 0; j <= lastRow; j++, n++)
            for (var s = 0; s <= 2 * rings; s++)
                yield return (n % 2 == 0 ? rings - s : -rings + s, j);
    }

    /// <summary>Appends next, after one detour cell when the straight move from the last point would
    /// pass through the centre block. False, appending nothing, when no single detour clears it.</summary>
    private static bool Append(List<(int I, int J)> path, (int I, int J) next, IReadOnlyList<(int I, int J)> cells)
    {
        var last = path[^1];
        if (next == last) return false;
        if (!CrossesCentre(last, next))
        {
            path.Add(next);
            return true;
        }
        (int I, int J)? detour = null;
        var best = double.MaxValue;
        foreach (var w in cells)
        {
            if (w == last || w == next || CrossesCentre(last, w) || CrossesCentre(w, next)) continue;
            var length = Distance(last, w) + Distance(w, next);
            if (length < best)
            {
                best = length;
                detour = w;
            }
        }
        if (detour is not { } via) return false;
        path.Add(via);
        path.Add(next);
        return true;
    }

    private static double Distance((int I, int J) a, (int I, int J) b) =>
        Math.Sqrt((double)(a.I - b.I) * (a.I - b.I) + (double)(a.J - b.J) * (a.J - b.J));
}
