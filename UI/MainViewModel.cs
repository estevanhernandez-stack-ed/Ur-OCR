using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PluginRuntime _runtime;
    public ObservableCollection<TriggerRowViewModel> Triggers { get; } = new();
    public TriggerRowViewModel? Selected
    {
        get => _selected;
        set { _selected = value; OnChanged(); }
    }
    private TriggerRowViewModel? _selected;

    public string StatusText { get; private set; } = "Watching";
    public ICommand AddTriggerCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public MainViewModel(PluginRuntime runtime)
    {
        _runtime = runtime;
        foreach (var t in runtime.Triggers.All) Triggers.Add(new TriggerRowViewModel(t));
        AddTriggerCommand = new RelayCommand(_ =>
        {
            var pick = new RegionPickerOverlay();
            if (pick.ShowDialog() != true || pick.Picked is null) return;

            var t = new Trigger
            {
                Id = Guid.NewGuid(),
                Name = "New trigger",
                Region = pick.Picked,
                Mode = TriggerMode.Color,
                Color = new ColorCriteria(new Rgb(255, 255, 255), 10, ColorSamplingMode.RegionAverage),
                Keybind = new KeyCombo("F", Array.Empty<string>()),
            };
            _runtime.Triggers.Add(t);
            var row = new TriggerRowViewModel(t);
            Triggers.Add(row);
            Selected = row;
        });
        TogglePauseCommand = new RelayCommand(_ =>
        {
            if (runtime.Coordinator is not null)
                runtime.Coordinator.Paused = !runtime.Coordinator.Paused;
            StatusText = runtime.Coordinator?.Paused == true ? "Paused" : "Watching";
            OnChanged(nameof(StatusText));
        });
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { foreach (var row in Triggers) row.Refresh(); };
        timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
