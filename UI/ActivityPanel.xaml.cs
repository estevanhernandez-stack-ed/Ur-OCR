using System.Windows.Controls;
using RoRoRo.UrOcr.Engine;

namespace RoRoRo.UrOcr.UI;

public partial class ActivityPanel : UserControl
{
    private ActivityLog? _log;
    public ActivityPanel() { InitializeComponent(); }

    public void Bind(ActivityLog log)
    {
        _log = log;
        Refresh();
        log.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh() { if (_log is not null) Items.ItemsSource = _log.Snapshot(); }
}
