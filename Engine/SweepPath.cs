using RoRoRo.UrOcr.Ipc;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>An ore sweep's path (empty when none) and the indexes, into the ore points it was given,
/// of the points on it; the rest go to ClearAt.</summary>
public sealed record OreSweep(IReadOnlyList<SweepPoint> Path, IReadOnlyList<int> Swept)
{
    public static readonly OreSweep None = new(Array.Empty<SweepPoint>(), Array.Empty<int>());
}

/// <summary>
/// The sweep's path (ore-stop sweep spec, "The path"): square rings around the character, one block
/// apart, from ring 1 out to <see cref="Rings"/>, in spiral order, starting and ending on the start
/// block east of the character (beside it, never under it). Cells are block units around the centre,
/// j growing down the screen. The spiral goes right 1, up 1, left 2, down 2, right 3, ... so ring k
/// starts at (k, k - 1), ends at (k, k), and the next ring starts one block right, outward.
/// <para>A cell is swept when it is not the centre, its point lies <see cref="EdgeMarginPx"/> inside
/// the client, and it passes the HUD rule for its kind of point (HudMask): the start block, where the
/// button goes down and comes back up, is off every button; every other point is held, so it may sit
/// on a button (the hotbar, the icon column) but not on the Auto Mine button, whose dot is the guard.
/// The path jumps over the rest with the button held. With nearSide, rows continue below ring 4 (the
/// camera side) across the ring's width, back and forth, down to the last row inside the bottom
/// margin, under the hotbar too; the other three sides stay at ring 4 (owner input 6). When skipped cells leave a straight move that would pass through the centre block, one detour
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

    /// <summary>A pass's path: its block size and centre, the finder's client, the game's HUD skipped
    /// (HudMask.Contains where the button goes down and up, HudMask.ContainsHeld everywhere else).</summary>
    public static IReadOnlyList<SweepPoint> Build(FinderSetup pass, bool nearSide) =>
        Build(pass.CenterX, pass.CenterY, pass.Pitch, pass.ClientW, pass.ClientH, nearSide,
            (x, y) => HudMask.Contains(x, y, pass.ClientW, pass.ClientH),
            (x, y) => HudMask.ContainsHeld(x, y, pass.ClientW, pass.ClientH));

    /// <summary>The path, or empty when the start block is outside the game area or fewer than
    /// <see cref="MinPoints"/> points fit. <paramref name="pressMasked"/> rules the start block (the
    /// first and last point, where the button goes down and up); <paramref name="heldMasked"/> every
    /// other point.</summary>
    public static IReadOnlyList<SweepPoint> Build(int centerX, int centerY, int pitch, int clientW, int clientH,
        bool nearSide, Func<int, int, bool> pressMasked, Func<int, int, bool> heldMasked)
    {
        if (pitch < 1) return Array.Empty<SweepPoint>();
        (int X, int Y) At((int I, int J) c) => (centerX + c.I * pitch, centerY + c.J * pitch);
        bool Valid((int I, int J) c)
        {
            if (c == (0, 0)) return false;
            var (x, y) = At(c);
            return x >= EdgeMarginPx && y >= EdgeMarginPx && x <= clientW - 1 - EdgeMarginPx && y <= clientH - 1 - EdgeMarginPx
                   && !heldMasked(x, y);
        }

        (int I, int J) start = (1, 0);
        var home = At(start);
        if (!Valid(start) || pressMasked(home.X, home.Y)) return Array.Empty<SweepPoint>();

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
    internal static bool CrossesCentre((int I, int J) a, (int I, int J) b) => CrossesCentre(((double)a.I, (double)a.J), ((double)b.I, (double)b.J));

    /// <summary>The same test for points anywhere, in block units (fractions of a block) from the centre.</summary>
    internal static bool CrossesCentre((double I, double J) a, (double I, double J) b)
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

    /// <summary>
    /// The ore sweep (live 2026-09-30: 24 ore points through ClearAt took 35 s, most breaking in one
    /// 0.3 s hold): the ore points as one held drag, a free path Ur Task takes off the block lattice.
    /// It starts on the ore point nearest <paramref name="startBlock"/> (the stone sweep's start) that
    /// is off <paramref name="pressMasked"/>, since the button goes down and comes back up there; goes
    /// to the nearest point not yet visited each time; and closes on its first point. Every other point
    /// is held, so ore on a button (the hotbar, the icon column) is swept, but not ore that is
    /// <paramref name="heldMasked"/> (the Auto Mine button, whose dot is the guard). A point on the
    /// centre block (within half a block both ways) is never swept. A straight move that would pass
    /// through the centre block goes round it by the shortest ring-1 block that clears it both ways
    /// (inside the client's margin, off <paramref name="heldMasked"/>); with none, the point is
    /// dropped, or on the way home the last point is dropped until the move home clears it. Fewer than
    /// 2 ore points on the path, or none that can start it: no path. Every point stays in the measured
    /// client's pixels. Pure.
    /// </summary>
    public static OreSweep Ore(IReadOnlyList<(int X, int Y)> ore, SweepPoint startBlock, int centerX, int centerY,
        int pitch, int clientW, int clientH, Func<int, int, bool> pressMasked, Func<int, int, bool> heldMasked)
    {
        if (pitch < 1 || ore.Count < 2) return OreSweep.None;
        (double I, double J) Block((int X, int Y) p) => ((p.X - centerX) / (double)pitch, (p.Y - centerY) / (double)pitch);
        bool OnCentre((int X, int Y) p) { var (i, j) = Block(p); return Math.Abs(i) <= 0.5 && Math.Abs(j) <= 0.5; }
        double Px((int X, int Y) a, (int X, int Y) b) => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));

        // Ring-1 blocks a detour may use: inside the margin and off the Auto Mine button (a detour is held).
        var ring1 = new List<(int X, int Y)>();
        for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
            {
                if (i == 0 && j == 0) continue;
                var (x, y) = (centerX + i * pitch, centerY + j * pitch);
                if (x >= EdgeMarginPx && y >= EdgeMarginPx && x <= clientW - 1 - EdgeMarginPx && y <= clientH - 1 - EdgeMarginPx
                    && !heldMasked(x, y))
                    ring1.Add((x, y));
            }

        // Nearest neighbour from the pressable ore point nearest the start block.
        var left = Enumerable.Range(0, ore.Count).Where(k => !OnCentre(ore[k]) && !heldMasked(ore[k].X, ore[k].Y)).ToList();
        if (left.Count < 2) return OreSweep.None;
        var start = (startBlock.X, startBlock.Y);
        var pressable = left.Where(k => !pressMasked(ore[k].X, ore[k].Y)).ToList();
        if (pressable.Count == 0) return OreSweep.None;
        var first = pressable.MinBy(k => Px(start, ore[k]));
        left.Remove(first);
        var order = new List<int> { first };
        var at = ore[first];
        while (left.Count > 0)
        {
            var next = left.MinBy(k => Px(at, ore[k]));
            left.Remove(next);
            order.Add(next);
            at = ore[next];
        }

        // Each step is a point and whether it is ore (its index) or a detour (-1).
        var path = new List<((int X, int Y) P, int Ore)> { (ore[order[0]], order[0]) };
        bool Append((int X, int Y) next, int index)
        {
            var last = path[^1].P;
            if (next == last) return false;
            if (!CrossesCentre(Block(last), Block(next)))
            {
                path.Add((next, index));
                return true;
            }
            (int X, int Y)? via = null;
            var best = double.MaxValue;
            foreach (var w in ring1)
            {
                if (w == last || w == next || CrossesCentre(Block(last), Block(w)) || CrossesCentre(Block(w), Block(next))) continue;
                var length = Px(last, w) + Px(w, next);
                if (length < best)
                {
                    best = length;
                    via = w;
                }
            }
            if (via is not { } v) return false;
            path.Add((v, -1));
            path.Add((next, index));
            return true;
        }

        foreach (var k in order.Skip(1))
        {
            if (path.Count > MaxPoints - 4) break;     // room for a detour here and one on the way home
            Append(ore[k], k);                          // false: dropped, it goes to ClearAt
        }
        var home = path[0].P;
        while (path.Count > 1 && !Append(home, order[0])) path.RemoveAt(path.Count - 1);

        var swept = path.Take(path.Count - 1).Where(s => s.Ore >= 0).Select(s => s.Ore).Distinct().ToList();
        if (path.Count < MinPoints || swept.Count < 2) return OreSweep.None;
        return new OreSweep(path.Select(s => new SweepPoint(s.P.X, s.P.Y)).ToList(), swept);
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
