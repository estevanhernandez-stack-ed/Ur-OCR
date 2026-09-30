namespace RoRoRo.UrOcr.Engine;

/// <summary>One of the game's buttons, a half-open box [X0, X1) x [Y0, Y1) in the measured client.</summary>
public readonly record struct HudBox(string Name, int X0, int Y0, int X1, int Y1);

/// <summary>
/// The game's UI chrome, in the measured 800x599 client and scaled the same way to any width x height
/// capture. <see cref="Boxes"/> are the real buttons, measured on live frames at 100% with about 8 px
/// of margin: TargetFinder and SweepPath drop any point inside one, ore or stone, so a held click never
/// opens a menu instead of mining (a spot on the inventory button once did exactly that and stopped
/// the pulse). Everything outside them is game, so the top strip beside Go to Top and the ground right
/// of the hotbar are clicked. <see cref="ReadBounds"/> keeps the older full-width bands for the reads
/// that take a region rather than points (PitchEstimator, LayerShare): a region cannot step around a
/// button, and the hotbar's evenly spaced slots are the strongest repeat on screen.
/// </summary>
public static class HudMask
{
    public const int ClientW = 800;
    public const int ClientH = 599;

    /// <summary>The buttons. A box that reaches the client's edge covers the edge pixel at any scale.</summary>
    public static readonly IReadOnlyList<HudBox> Boxes = new HudBox[]
    {
        new("Roblox menu", 0, 0, 215, 62),                 // Roblox logo, menu, chat, mic
        new("Go to Top", 330, 18, 470, 80),
        new("icon column", 0, 0, 160, ClientH),            // gifts, pickaxe, Leagues, currencies, boosts
        new("hotbar", 160, 470, 725, ClientH),             // the slots (from about y 482) and the pets button
        new("update timer", 690, 555, ClientW, ClientH),   // "New Update" and its countdown
    };

    /// <summary>The read regions' bands (PitchEstimator, LayerShare): left of this is the icon column.</summary>
    public const int Left = 160;
    /// <summary>Above this: the top bar and Go to Top, the full width.</summary>
    public const int Top = 70;
    /// <summary>At and below this: the hotbar (slots about 78 px apart) and the bottom icons, the full width.</summary>
    public const int Bottom = 470;

    /// <summary>The read regions' bands in a width x height frame, scaled from the measured client: a
    /// read stays right of Left, below Top and above Bottom. Deliberately wider than <see cref="Boxes"/>.</summary>
    public static (int Left, int Top, int Bottom) ReadBounds(int width, int height) =>
        (Left * width / ClientW,
         Top * height / ClientH,
         Math.Min(height - 1, Bottom * height / ClientH));

    /// <summary>True when (x, y) in a width x height frame sits on one of the game's buttons.</summary>
    public static bool Contains(int x, int y, int width, int height)
    {
        foreach (var b in Boxes)
            if (x >= b.X0 * width / ClientW && x < b.X1 * width / ClientW
                && y >= b.Y0 * height / ClientH && y < b.Y1 * height / ClientH)
                return true;
        return false;
    }
}
