using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RadialConvert.Conversion;

namespace RadialConvert.UI;

/// <summary>Small non-activating notification in the bottom-right corner. Several stack upward.</summary>
public sealed class ToastWindow : Window
{
    private const double ToastWidth = 340;
    private const double Spacing = 8;
    private static readonly List<ToastWindow> Open = new();

    private readonly TextBlock _icon = new()
    {
        FontFamily = new FontFamily(Glyphs.FontFamily), FontSize = 20, Width = 28,
        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0)
    };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
    private DispatcherTimer? _autoClose;

    public ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false; // never steal focus from Explorer
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = ToastWidth;
        Title = "RadialConvert";

        var close = MakeButton(Glyphs.Cancel, "Dismiss", glyph: true);
        close.Click += (_, _) => Close();
        close.VerticalAlignment = VerticalAlignment.Top;

        var text = new StackPanel();
        text.Children.Add(_title);
        text.Children.Add(_message);
        text.Children.Add(_progress);
        text.Children.Add(_buttons);

        var row = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(_icon);
        row.Children.Add(close);
        row.Children.Add(text);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x20, 0x22, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 8, 12),
            Margin = new Thickness(6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.4 },
            Child = row
        };

        SizeChanged += (_, _) => Layout();
        Closed += (_, _) => { Open.Remove(this); Layout(); };
    }

    public static ToastWindow ShowProgress(string title, string message)
    {
        var t = new ToastWindow();
        t.SetState(Glyphs.Sync, Color.FromRgb(0x5A, 0x9B, 0xFF), title, message, progress: true);
        Open.Add(t);
        t.Show();
        Layout();
        t.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        return t;
    }

    public static void ShowMessage(string title, string message, bool error)
    {
        var t = ShowProgress(title, message);
        if (error) t.SetError(title, message); else t.SetInfo(title, message);
    }

    /// <summary>Switches the bar to determinate (0..1). Must be called on the UI thread.</summary>
    public void SetProgress(double fraction)
    {
        _progress.IsIndeterminate = false;
        _progress.Maximum = 1;
        _progress.Value = Math.Clamp(fraction, 0, 1);
        _title.ToolTip = $"{_title.Text} ({fraction:P0})";
    }

    /// <summary>Shows a Cancel button while the conversion is running.</summary>
    public void AddCancel(Action onCancel)
    {
        var cancel = MakeButton("Cancel", "Cancel this conversion");
        cancel.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x46, 0x4C));
        cancel.Click += (_, _) => { cancel.IsEnabled = false; onCancel(); };
        _buttons.Children.Add(cancel);
        _buttons.Visibility = Visibility.Visible;
    }

    public void SetSuccess(string title, string message, string outputFile)
    {
        SetState(Glyphs.Check, Color.FromRgb(0x4C, 0xC2, 0x6A), title, message, progress: false);
        var open = MakeButton("Open folder", "Open the containing folder and select the new file");
        open.Click += (_, _) =>
        {
            // explorer.exe with an argument list (no shell parsing of the file name).
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add($"/select,{outputFile}");
            try { Process.Start(psi); } catch { /* ignore */ }
            Close();
        };
        _buttons.Children.Add(open);
        StartAutoClose(TimeSpan.FromSeconds(8));
    }

    public void SetError(string title, string message)
    {
        SetState(Glyphs.Error, Color.FromRgb(0xFF, 0x6B, 0x6B), title, message, progress: false);
        StartAutoClose(TimeSpan.FromSeconds(20));
    }

    public void SetInfo(string title, string message)
    {
        SetState(Glyphs.Info, Color.FromRgb(0x5A, 0x9B, 0xFF), title, message, progress: false);
        StartAutoClose(TimeSpan.FromSeconds(6));
    }

    private void SetState(string glyph, Color color, string title, string message, bool progress)
    {
        _icon.Text = glyph;
        _icon.Foreground = new SolidColorBrush(color);
        _title.Text = title;
        _title.ToolTip = title;
        _message.Text = message;
        _progress.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        _buttons.Children.Clear();
        _buttons.Visibility = progress ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartAutoClose(TimeSpan after)
    {
        _autoClose?.Stop();
        _autoClose = new DispatcherTimer { Interval = after };
        _autoClose.Tick += (_, _) => { _autoClose.Stop(); if (!IsMouseOver) Close(); else StartAutoClose(TimeSpan.FromSeconds(3)); };
        _autoClose.Start();
    }

    private static Button MakeButton(string text, string tooltip, bool glyph = false)
    {
        var b = new Button
        {
            Content = text,
            ToolTip = tooltip,
            Padding = glyph ? new Thickness(6, 2, 6, 2) : new Thickness(12, 4, 12, 4),
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = Brushes.White,
            Background = glyph ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xE0)),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = glyph ? 11 : 12
        };
        if (glyph) b.FontFamily = new FontFamily(Glyphs.FontFamily);
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
        return b;
    }

    /// <summary>Stacks open toasts upward from the bottom-right of the primary work area.</summary>
    private static void Layout()
    {
        var work = SystemParameters.WorkArea;
        double bottom = work.Bottom - Spacing;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            double h = t.ActualHeight > 0 ? t.ActualHeight : 110;
            t.Left = work.Right - t.Width - Spacing;
            t.Top = bottom - h;
            bottom -= h + Spacing;
        }
    }
}
