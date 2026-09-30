using RoRoRo.UrOcr.Engine;
using Xunit;

namespace RoRoRo.UrOcr.Tests.Engine;

/// <summary>
/// The HUD is the game's real buttons, not full-width bands: the owner wants the highest click just
/// above Go to Top, and blocks in the top strip and beside the hotbar were being thrown away
/// (2026-09-30). Boxes measured on live 800x599 frames at 100%.
/// </summary>
public class HudMaskTests
{
    private static bool Hud(int x, int y) => HudMask.Contains(x, y, 800, 599);

    [Theory]
    [InlineData(40, 40)]      // the Roblox menu cluster, top left
    [InlineData(210, 58)]     // its right end (the mic button)
    [InlineData(400, 40)]     // Go to Top
    [InlineData(335, 75)]     // Go to Top's box, lower left corner
    [InlineData(465, 20)]     // Go to Top's box, upper right corner
    [InlineData(80, 300)]     // the left icon column, full height
    [InlineData(159, 590)]    // the left column's bottom right
    [InlineData(400, 470)]    // the hotbar line itself is HUD (a held button 12 px above the slots)
    [InlineData(400, 520)]    // a hotbar slot
    [InlineData(700, 520)]    // the pets button right of the slots
    [InlineData(724, 470)]    // the hotbar box's upper right pixel
    [InlineData(760, 570)]    // the "New Update" timer, bottom right
    [InlineData(799, 598)]    // the client's last pixel is under the timer
    public void The_games_buttons_are_hud(int x, int y) => Assert.True(Hud(x, y));

    [Theory]
    [InlineData(600, 40)]     // the top strip right of Go to Top
    [InlineData(280, 40)]     // the top strip left of Go to Top, right of the menu cluster
    [InlineData(400, 10)]     // above Go to Top
    [InlineData(400, 85)]     // just below Go to Top: the highest click in the middle
    [InlineData(220, 40)]     // just right of the menu cluster
    [InlineData(760, 500)]    // right of the hotbar, above the timer
    [InlineData(725, 520)]    // just right of the pets button
    [InlineData(400, 469)]    // just above the hotbar line
    [InlineData(160, 300)]    // just right of the left column
    public void Everything_else_is_game(int x, int y) => Assert.False(Hud(x, y));

    [Fact]
    public void The_boxes_scale_with_the_frame()
    {
        // Twice the measured client: every box doubles.
        Assert.True(HudMask.Contains(800, 80, 1600, 1198));      // Go to Top at (400, 40)
        Assert.False(HudMask.Contains(1200, 80, 1600, 1198));    // the top strip at (600, 40)
        Assert.False(HudMask.Contains(1520, 1000, 1600, 1198));  // (760, 500), right of the hotbar
        Assert.True(HudMask.Contains(1599, 1197, 1600, 1198));   // the last pixel, under the timer
    }

    [Fact]
    public void The_pitch_read_keeps_the_conservative_bands()
    {
        // PitchEstimator and LayerShare read a region, not points: it stays right of 160, below 70 and
        // above 470, the full width, so the hotbar's 78 px slots and Go to Top never enter the read.
        Assert.Equal((160, 70, 470), HudMask.ReadBounds(800, 599));
        Assert.Equal((320, 140, 940), HudMask.ReadBounds(1600, 1198));
    }
}
