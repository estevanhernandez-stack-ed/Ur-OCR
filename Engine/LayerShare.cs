using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>A layer read by colour share: the winning layer (null for none), each layer's share of the
/// pixels read, and how many pixels were read.</summary>
public sealed record LayerRead(string? Layer, IReadOnlyDictionary<string, double> Shares, int PixelsRead);

/// <summary>
/// The pulse's layer read (spec "The layer is read by colour share, not 8 spots"). Layers can share a
/// dark base colour (Mine #8's top and bottom sit within 5 RGB of each other there), so a spot on base
/// rock votes wrong; the mix is what differs. On a disc of 3 blocks around the character, HUD and
/// character left out, every 2nd pixel each way goes to its nearest listed colour: a layer's rock
/// colour within tolerance counts for that layer; an ore colour, a colour two layers both list, or
/// nothing near counts for nobody. The largest share wins when it is at least minShare and at least
/// lead times the runner-up. Pure and deterministic.
/// </summary>
public static class LayerShare
{
    public const double DefaultMinShare = 0.04;
    public const double DefaultLead = 1.5;
    /// <summary>The disc's radius, in blocks.</summary>
    public const int RadiusBlocks = 3;
    /// <summary>Every Step-th pixel in x and y is read.</summary>
    public const int Step = 2;

    private const int Nobody = -1;

    public static LayerRead Read(FramePixels frame, int centerX, int centerY, int pitch,
        IReadOnlyList<LayerDefinition> layers, IReadOnlyList<Rgb> ore, int toleranceRgb, double minShare, double lead)
    {
        // Every listed colour with its owner: a layer index, or Nobody for ore and shared colours.
        var palette = new List<(Rgb Colour, int Owner)>();
        for (var i = 0; i < layers.Count; i++)
            foreach (var c in layers[i].Rock)
            {
                var at = palette.FindIndex(p => p.Colour == c);
                if (at < 0) palette.Add((c, i));
                else if (palette[at].Owner != i) palette[at] = (c, Nobody);
            }
        foreach (var c in ore) palette.Add((c, Nobody));

        var counts = new int[layers.Count];
        var read = 0;
        var (left, top, bottom) = PitchEstimator.HudBounds(frame);
        var radius = (long)RadiusBlocks * pitch;
        long r2 = radius * radius, character = (long)PitchEstimator.CharacterRadius * PitchEstimator.CharacterRadius;
        long tol2 = (long)toleranceRgb * toleranceRgb;
        int x0 = Math.Max(left + 1, centerX - (int)radius), x1 = Math.Min(frame.Width - 1, centerX + (int)radius);
        int y0 = Math.Max(top + 1, centerY - (int)radius), y1 = Math.Min(bottom - 1, centerY + (int)radius);

        for (var y = y0; y <= y1; y += Step)
            for (var x = x0; x <= x1; x += Step)
            {
                long dx = x - centerX, dy = y - centerY, d2 = dx * dx + dy * dy;
                if (d2 > r2 || d2 < character) continue;
                read++;
                var owner = Nearest(frame.At(x, y), palette, tol2);
                if (owner != Nobody) counts[owner]++;
            }

        var shares = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < layers.Count; i++) shares[layers[i].Name] = read == 0 ? 0 : (double)counts[i] / read;

        var ranked = layers.Select(l => shares[l.Name]).OrderByDescending(s => s).ToList();
        var best = ranked.Count > 0 ? ranked[0] : 0;
        var next = ranked.Count > 1 ? ranked[1] : 0;
        string? layer = null;
        if (best > 0 && best >= minShare && best >= lead * next)
            layer = layers.First(l => shares[l.Name] == best).Name;
        return new LayerRead(layer, shares, read);
    }

    /// <summary>The owner of the nearest palette colour within tolerance, else Nobody.</summary>
    private static int Nearest(Rgb c, List<(Rgb Colour, int Owner)> palette, long tol2)
    {
        var owner = Nobody;
        var best = long.MaxValue;
        foreach (var (p, o) in palette)
        {
            long dr = c.R - p.R, dg = c.G - p.G, db = c.B - p.B;
            var d = dr * dr + dg * dg + db * db;
            if (d < best)
            {
                best = d;
                owner = o;
            }
        }
        return best <= tol2 ? owner : Nobody;
    }
}
