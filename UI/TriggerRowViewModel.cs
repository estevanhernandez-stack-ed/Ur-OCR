using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public sealed class TriggerRowViewModel(Trigger source, PreviewEvaluator preview) : INotifyPropertyChanged
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

    // ── Live match meter ──────────────────────────────────────────────────────

    private Brush _previewTargetBrush = Brushes.Transparent;
    public Brush PreviewTargetBrush
    {
        get => _previewTargetBrush;
        private set { _previewTargetBrush = value; OnChanged(); }
    }

    private Brush _previewSampledBrush = Brushes.Transparent;
    public Brush PreviewSampledBrush
    {
        get => _previewSampledBrush;
        private set { _previewSampledBrush = value; OnChanged(); }
    }

    private string _previewDistanceText = "—";
    public string PreviewDistanceText
    {
        get => _previewDistanceText;
        private set { _previewDistanceText = value; OnChanged(); }
    }

    private bool _previewIsMatch;
    public bool PreviewIsMatch
    {
        get => _previewIsMatch;
        private set { _previewIsMatch = value; OnChanged(); }
    }

    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public void StartPreview()
    {
        if (source.Mode != TriggerMode.Color || source.Color is null) return;
        _previewTimer.Tick -= OnPreviewTick;
        _previewTimer.Tick += OnPreviewTick;

        // Seed target swatch immediately from the stored color.
        var t = source.Color.TargetRgb;
        PreviewTargetBrush = RgbToBrush(t);

        _previewTimer.Start();
    }

    public void StopPreview()
    {
        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewTick;
        // Clear live fields so stale values are not shown.
        PreviewSampledBrush = Brushes.Transparent;
        PreviewDistanceText = "—";
        PreviewIsMatch = false;
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (source.Mode != TriggerMode.Color || source.Color is null) return;

        var region = source.Region;
        var criteria = source.Color;
        ColorMatchResult? result = null;
        try { result = preview.EvaluateOnce(region, criteria); }
        catch { /* screen capture can fail transiently — skip tick */ }

        if (result is null)
        {
            PreviewSampledBrush = Brushes.Transparent;
            PreviewDistanceText = "—";
            PreviewIsMatch = false;
        }
        else
        {
            PreviewSampledBrush = RgbToBrush(result.Sampled);
            PreviewDistanceText = $"d = {result.Distance:F1}";
            PreviewIsMatch = result.Matched;
        }

        // Keep target swatch current in case the user edits the color.
        PreviewTargetBrush = RgbToBrush(source.Color.TargetRgb);
    }

    private static SolidColorBrush RgbToBrush(Rgb rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)rgb.R, (byte)rgb.G, (byte)rgb.B));
        b.Freeze();
        return b;
    }

    // ── Housekeeping ──────────────────────────────────────────────────────────

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
