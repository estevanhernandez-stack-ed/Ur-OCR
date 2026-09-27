using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Plain-language colour names with the hex for precision, e.g. "green #8BE03A".
/// Copied from Ur Task's ColorNamer so both plugins report colours alike.
/// </summary>
public static class ColorNamer
{
    public static string Hex(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string Describe(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));

        string name;
        if (s < 0.2 || d < 0.08)
        {
            name = l < 0.15 ? "black" : l > 0.85 ? "white" : "grey";
        }
        else
        {
            double h = max == r ? 60 * (((g - b) / d) % 6)
                     : max == g ? 60 * ((b - r) / d + 2)
                     : 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            name = h < 15 || h >= 345 ? "red"
                 : h < 40 ? "orange"
                 : h < 70 ? "yellow"
                 : h < 165 ? "green"
                 : h < 195 ? "cyan"
                 : h < 255 ? "blue"
                 : h < 290 ? "purple"
                 : "pink";
            if (l < 0.3) name = "dark " + name;
        }
        return $"{name} {Hex(c)}";
    }
}
