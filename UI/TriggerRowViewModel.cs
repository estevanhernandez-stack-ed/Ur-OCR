using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public sealed class TriggerRowViewModel(Trigger source, PreviewEvaluator preview, PluginRuntime runtime) : INotifyPropertyChanged
{
    public Trigger Source => source;
    public Guid Id => source.Id;
    public string Name => source.Name;
    public bool Enabled
    {
        get => source.Enabled;
        set { source.Enabled = value; OnChanged(); }
    }

    // ── Region re-pick ───────────────────────────────────────────────────────

    public System.Windows.Input.ICommand RepickRegionCommand => _repick ??= new RelayCommand(_ =>
    {
        var pick = new RegionPickerOverlay();
        if (pick.ShowDialog() != true || pick.Picked is null) return;
        var anchor = Engine.TriggerAnchor.ForPickedRegion(pick.Picked, runtime.Accounts.Pids, runtime.WindowMetrics);
        source.Region = anchor.Region;
        source.CoordSpace = anchor.CoordSpace;
        source.RecordedClientW = anchor.RecordedClientW;
        source.RecordedClientH = anchor.RecordedClientH;
        runtime.Triggers.Update(source);
        OnChanged(nameof(RegionModeText));
        OnChanged(nameof(IsWindowAnchored));
    });
    private System.Windows.Input.ICommand? _repick;

    /// <summary>Human-readable region anchor mode for the REGION line.</summary>
    public string RegionModeText => source.IsClientSpace
        ? $"Window-anchored (recorded {source.RecordedClientW}×{source.RecordedClientH})"
        : $"Screen: {source.Region.X}, {source.Region.Y}  {source.Region.Width}×{source.Region.Height}";

    /// <summary>True when the trigger is window-anchored. Read-only surface for
    /// the mode indicator; switching modes is done via Re-pick (window) — a plain
    /// setter can't re-derive the client offset without a fresh pick.</summary>
    public bool IsWindowAnchored => source.IsClientSpace;

    // ── Action selector ───────────────────────────────────────────────────────

    /// <summary>True when the trigger fires by pressing a keybind (default).</summary>
    public bool IsKeyChord
    {
        get => source.Action == TriggerAction.KeyChord;
        set
        {
            if (value) { source.Action = TriggerAction.KeyChord; OnChanged(); OnChanged(nameof(IsRunMacro)); }
        }
    }

    /// <summary>True when the trigger fires by running a Ur Task macro.</summary>
    public bool IsRunMacro
    {
        get => source.Action == TriggerAction.RunMacro;
        set
        {
            if (value) { source.Action = TriggerAction.RunMacro; ReloadMacros(); OnChanged(); OnChanged(nameof(IsKeyChord)); }
        }
    }

    /// <summary>The macro ID stored on the trigger (null when action is KeyChord).</summary>
    public string? MacroId
    {
        get => source.MacroId;
        set { source.MacroId = value; OnChanged(); }
    }

    private IReadOnlyList<UrTaskMacro> _urTaskMacros = Array.Empty<UrTaskMacro>();
    /// <summary>Available Ur Task macros. Re-read from disk every time the edit
    /// panel opens (StartPreview) and when the action switches to RunMacro — a
    /// macro recorded in Ur Task while this window is open must appear without a
    /// restart. (The old load-once cache went stale exactly there.)</summary>
    public IReadOnlyList<UrTaskMacro> UrTaskMacros => _urTaskMacros;

    /// <summary>Diagnostic: the folder Ur-OCR reads Ur Task macros from, plus the
    /// live count — surfaced in the picker's empty state so a mismatch is visible
    /// instead of a silent blank dropdown.</summary>
    public string MacroSourceHint =>
        $"Looked in {Storage.UrTaskMacros.MacrosDir} — found {_urTaskMacros.Count}.";

    /// <summary>Re-read the Ur Task macro library from disk and refresh the picker.</summary>
    public void ReloadMacros()
    {
        _urTaskMacros = Storage.UrTaskMacros.Load();
        OnChanged(nameof(UrTaskMacros));
        OnChanged(nameof(MacroSourceHint));
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
        // Re-read the macro list every time the panel opens so newly-recorded
        // Ur Task macros show up without an Ur-OCR restart.
        ReloadMacros();

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

        ColorMatchResult? result = null;
        try { result = preview.EvaluateTrigger(source); }
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
