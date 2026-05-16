using System.ComponentModel;
using System.Runtime.CompilerServices;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public sealed class TriggerRowViewModel(Trigger source) : INotifyPropertyChanged
{
    public Trigger Source => source;
    public Guid Id => source.Id;
    public string Name => source.Name;
    public bool Enabled
    {
        get => source.Enabled;
        set { source.Enabled = value; OnChanged(); }
    }
    public string HitSummary => source.HitCount == 0
        ? "never fired"
        : $"fired {source.HitCount}× · {RelativeAge(source.LastFiredAt)}";

    public void Refresh()
    {
        OnChanged(nameof(HitSummary));
    }

    private static string RelativeAge(DateTimeOffset? t)
    {
        if (t is null) return "?";
        var d = DateTimeOffset.UtcNow - t.Value;
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds}s ago";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        return $"{(int)d.TotalHours}h ago";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
