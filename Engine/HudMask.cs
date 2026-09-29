namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// The game's UI chrome (the bottom hotbar, the left icon column, the top bar), in the measured
/// 800x599 client and scaled the same way to any width x height capture. PitchEstimator aims its
/// read away from it so a button never reads as a block edge; TargetFinder drops any point that
/// lands inside it, ore or stone, so a held click never opens a menu instead of mining (a spot on
/// the inventory button once did exactly that and stopped the pulse).
/// </summary>
public static class HudMask
{
    public const int ClientW = 800;
    public const int ClientH = 599;
    /// <summary>Left of this: the icon column (gifts, pickaxe, Leagues, currencies).</summary>
    public const int Left = 160;
    /// <summary>Above this: the top bar and the "Go to Top" button.</summary>
    public const int Top = 70;
    /// <summary>Below this: the hotbar (slots about 78 px apart, the strongest repeat on screen) and the bottom icons.</summary>
    public const int Bottom = 470;

    /// <summary>The mask in a width x height frame, scaled from the measured client.</summary>
    public static (int Left, int Top, int Bottom) Bounds(int width, int height) =>
        (Left * width / ClientW,
         Top * height / ClientH,
         Math.Min(height - 1, Bottom * height / ClientH));

    /// <summary>True when (x, y) in a width x height frame sits in the HUD: left of the icon column,
    /// above the top bar, or at or below the hotbar line.</summary>
    public static bool Contains(int x, int y, int width, int height)
    {
        var (left, top, bottom) = Bounds(width, height);
        return x < left || y < top || y > bottom;
    }
}
