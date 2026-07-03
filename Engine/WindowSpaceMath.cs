using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.Engine;

/// <summary>
/// Pure screen↔client region mapping for window-anchored triggers. Mirrors Ur
/// Task's WindowSpaceMath. No Win32 — callers supply origins/sizes from
/// IWindowMetrics so the math is unit-testable.
/// </summary>
public static class WindowSpaceMath
{
    /// <summary>Absolute screen region → client-relative region (subtract origin).</summary>
    public static RegionRect ToClientRegion(RegionRect screen, (int X, int Y) clientOrigin)
        => new(screen.X - clientOrigin.X, screen.Y - clientOrigin.Y, screen.Width, screen.Height);

    /// <summary>
    /// Client-relative region → absolute screen region, scaled by
    /// current/recorded client size then offset by the current client origin.
    /// Ur Task resizes the window to fit; Ur-OCR (a watcher) scales the region.
    /// </summary>
    public static RegionRect ToScreenRegion(
        RegionRect client, (int X, int Y) clientOrigin,
        (int W, int H) recordedClient, (int W, int H) currentClient)
    {
        double sx = recordedClient.W > 0 ? (double)currentClient.W / recordedClient.W : 1.0;
        double sy = recordedClient.H > 0 ? (double)currentClient.H / recordedClient.H : 1.0;
        return new RegionRect(
            clientOrigin.X + (int)System.Math.Round(client.X * sx),
            clientOrigin.Y + (int)System.Math.Round(client.Y * sy),
            (int)System.Math.Round(client.Width * sx),
            (int)System.Math.Round(client.Height * sy));
    }
}
