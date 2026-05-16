using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RoRoRo.UrOcr.Engine;
using RoRoRo.UrOcr.Storage;

namespace RoRoRo.UrOcr.UI;

public partial class RegionPickerOverlay : Window
{
    public RegionRect? Picked { get; private set; }
    private Point? _start;
    private readonly CaptureEngine _capture = new();

    public RegionPickerOverlay()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Loaded += (_, _) => { Root.Focus(); Keyboard.Focus(Root); };
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, _start.Value.X);
        Canvas.SetTop(Selection, _start.Value.Y);
        Selection.Width = 0; Selection.Height = 0;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        LoupeCtl.Visibility = Visibility.Visible;
        Canvas.SetLeft(LoupeCtl, Math.Min(p.X + 20, ActualWidth - 170));
        Canvas.SetTop(LoupeCtl, Math.Min(p.Y + 20, ActualHeight - 170));
        var sx = (int)(SystemParameters.VirtualScreenLeft + p.X);
        var sy = (int)(SystemParameters.VirtualScreenTop + p.Y);
        try { LoupeCtl.UpdateAt(sx, sy, _capture); } catch { }

        if (_start is null) return;
        var x = Math.Min(p.X, _start.Value.X);
        var y = Math.Min(p.Y, _start.Value.Y);
        var w = Math.Abs(p.X - _start.Value.X);
        var h = Math.Abs(p.Y - _start.Value.Y);
        Canvas.SetLeft(Selection, x); Canvas.SetTop(Selection, y);
        Selection.Width = w; Selection.Height = h;
        ReadOut.Text = $"({(int)(SystemParameters.VirtualScreenLeft + x)}, {(int)(SystemParameters.VirtualScreenTop + y)}) — {(int)w}×{(int)h}";
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is null) return;
        var p = e.GetPosition(Root);
        var x = (int)(SystemParameters.VirtualScreenLeft + Math.Min(p.X, _start.Value.X));
        var y = (int)(SystemParameters.VirtualScreenTop + Math.Min(p.Y, _start.Value.Y));
        var w = (int)Math.Abs(p.X - _start.Value.X);
        var h = (int)Math.Abs(p.Y - _start.Value.Y);
        if (w < 8 || h < 8) { _start = null; Selection.Visibility = Visibility.Collapsed; return; }
        Picked = new RegionRect(x, y, w, h);
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        else if (e.Key == Key.Enter && Picked is not null) { DialogResult = true; Close(); }
    }
}
