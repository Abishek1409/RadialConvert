using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RadialConvert.Conversion;
using RadialConvert.Native;

namespace RadialConvert.UI;

/// <summary>Borderless, transparent, topmost ring of conversion slices centered on the cursor.</summary>
public sealed class RadialMenuWindow : Window
{
    private const double OuterRadius = 140;
    private const double InnerRadius = 46;
    private const double CancelRadius = 34;
    private const double Margin_ = 12;
    private const double Size = (OuterRadius + Margin_) * 2;
    private const double C = Size / 2; // center

    private static readonly Brush SliceFill = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x20, 0x22, 0x28)));
    private static readonly Brush SliceHover = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x2F, 0x6F, 0xE0)));
    private static readonly Brush SliceStroke = Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush SubTextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));

    private readonly int _cursorX, _cursorY;
    private readonly string _file;
    private readonly IReadOnlyList<ConversionOption> _options;
    private readonly Grid _root = new() { Width = Size, Height = Size, RenderTransformOrigin = new Point(0.5, 0.5) };
    private bool _closing;

    /// <summary>Set when a slice was clicked; null when cancelled.</summary>
    public ConversionOption? Chosen { get; private set; }

    public RadialMenuWindow(string file, IReadOnlyList<ConversionOption> options, int cursorX, int cursorY)
    {
        _file = file;
        _options = options;
        _cursorX = cursorX;
        _cursorY = cursorY;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; // transparent pixels are click-through, so outside clicks reach Explorer
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Size;
        Height = Size;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000; Top = -10000; // real position is set in physical pixels once the handle exists
        Title = "RadialConvert";
        Focusable = true;

        BuildVisuals();
        Content = _root;

        Loaded += (_, _) => { Reposition(); WindowHelper.ForceActivate(this); Keyboard.Focus(this); PlayOpenAnimation(); };
        Deactivated += (_, _) => SafeClose();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; SafeClose(); } };
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    /// <summary>Centers the window on the cursor in physical pixels, clamped to the monitor's work area.</summary>
    private void Reposition()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = MonitorDpi.ScaleAt(_cursorX, _cursorY);
        int px = (int)Math.Round(Size * scale);

        var work = MonitorDpi.BoundsAt(_cursorX, _cursorY, workArea: true);
        int left = Math.Clamp(_cursorX - px / 2, work.Left, Math.Max(work.Left, work.Right - px));
        int top = Math.Clamp(_cursorY - px / 2, work.Top, Math.Max(work.Top, work.Bottom - px));
        WindowHelper.SetPhysicalBounds(this, new NativeMethods.RECT { Left = left, Top = top, Right = left + px, Bottom = top + px });
    }

    private void BuildVisuals()
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        _root.Children.Add(canvas);

        int n = _options.Count;
        double sweep = 360.0 / n;
        // n == 1 would be a full ring; draw it as two half-arcs so ArcSegment geometry stays valid.
        double gap = n == 1 ? 0 : 1.2;

        for (int i = 0; i < n; i++)
        {
            var option = _options[i];
            double start = -90 - sweep / 2 + i * sweep + gap / 2; // first slice at 12 o'clock
            double end = start + sweep - gap;

            var slice = new Path
            {
                Data = n == 1 ? FullRing() : SliceGeometry(start, end),
                Fill = SliceFill,
                Stroke = SliceStroke,
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
                ToolTip = $"Convert {System.IO.Path.GetFileName(_file)} to {option.TargetName}"
            };
            AutomationPropertiesHelper.SetName(slice, option.MenuLabel(System.IO.Path.GetExtension(_file)));

            var label = BuildLabel(option);
            double mid = (start + end) / 2 * Math.PI / 180;
            double r = (InnerRadius + OuterRadius) / 2 + 4;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, C + r * Math.Cos(mid) - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, C + r * Math.Sin(mid) - label.DesiredSize.Height / 2);

            slice.MouseEnter += (_, _) => slice.Fill = SliceHover;
            slice.MouseLeave += (_, _) => slice.Fill = SliceFill;
            slice.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(option); };
            slice.MouseLeftButtonDown += (_, e) => e.Handled = true;

            canvas.Children.Add(slice);
            canvas.Children.Add(label);
        }

        // Center cancel button.
        var cancel = new Grid { Width = CancelRadius * 2, Height = CancelRadius * 2, Cursor = Cursors.Hand, ToolTip = "Cancel (Esc)" };
        var disc = new Ellipse { Fill = SliceFill, Stroke = SliceStroke, StrokeThickness = 1 };
        cancel.Children.Add(disc);
        cancel.Children.Add(new TextBlock
        {
            Text = Glyphs.Cancel,
            FontFamily = new FontFamily(Glyphs.FontFamily),
            FontSize = 18,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });
        AutomationPropertiesHelper.SetName(cancel, "Cancel");
        cancel.MouseEnter += (_, _) => disc.Fill = SliceHover;
        cancel.MouseLeave += (_, _) => disc.Fill = SliceFill;
        cancel.MouseLeftButtonUp += (_, e) => { e.Handled = true; SafeClose(); };
        Canvas.SetLeft(cancel, C - CancelRadius);
        Canvas.SetTop(cancel, C - CancelRadius);
        canvas.Children.Add(cancel);
    }

    private static FrameworkElement BuildLabel(ConversionOption option)
    {
        var panel = new StackPanel { IsHitTestVisible = false, Width = 78 };
        panel.Children.Add(new TextBlock
        {
            Text = option.Glyph,
            FontFamily = new FontFamily(Glyphs.FontFamily),
            FontSize = 20,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = option.TargetName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "to " + option.TargetExtension.TrimStart('.').ToUpperInvariant(),
            FontSize = 10,
            Foreground = SubTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return panel;
    }

    private static Geometry SliceGeometry(double startDeg, double endDeg)
    {
        Point P(double r, double deg) => new(C + r * Math.Cos(deg * Math.PI / 180), C + r * Math.Sin(deg * Math.PI / 180));
        bool large = endDeg - startDeg > 180;

        var fig = new PathFigure { StartPoint = P(InnerRadius, startDeg), IsClosed = true };
        fig.Segments.Add(new LineSegment(P(OuterRadius, startDeg), true));
        fig.Segments.Add(new ArcSegment(P(OuterRadius, endDeg), new Size(OuterRadius, OuterRadius), 0, large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(P(InnerRadius, endDeg), true));
        fig.Segments.Add(new ArcSegment(P(InnerRadius, startDeg), new Size(InnerRadius, InnerRadius), 0, large, SweepDirection.Counterclockwise, true));

        var g = new PathGeometry(new[] { fig });
        g.Freeze();
        return g;
    }

    private static Geometry FullRing()
    {
        var g = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(C, C), OuterRadius, OuterRadius),
            new EllipseGeometry(new Point(C, C), InnerRadius, InnerRadius));
        g.Freeze();
        return g;
    }

    private void PlayOpenAnimation()
    {
        var scale = new ScaleTransform(0.6, 0.6);
        _root.RenderTransform = scale;
        _root.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(170);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, duration) { EasingFunction = ease });
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void Choose(ConversionOption option)
    {
        Chosen = option;
        SafeClose();
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        // Deactivated can fire during Close(); defer so we never re-enter Close.
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);
}
