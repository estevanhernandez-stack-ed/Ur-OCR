namespace RoRoRo.UrOcr.Storage;

public sealed class Settings
{
    public int TickRateHz { get; set; } = 5;
    public string PauseAllHotkey { get; set; } = "F9";
    public int KeyHoldMs { get; set; } = 30;
}
