using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RadialConvert.Conversion;
using RadialConvert.Settings;

namespace RadialConvert.UI;

/// <summary>Modifier key picker, per-conversion toggles, and the tool status list.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ComboBox _modifier = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, CheckBox> _toggles = new();

    public event Action? Saved;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "RadialConvert Settings";
        Width = 460;
        Height = 600;
        MinWidth = 380;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16) };

        // Buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "_Save", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "_Cancel", Width = 90, IsCancel = true };
        save.Click += (_, _) => SaveAndClose();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();

        // Modifier
        body.Children.Add(Header("Trigger"));
        var modLabel = new Label { Content = "Hold this key and double-click a file:", Target = _modifier, Padding = new Thickness(0, 0, 0, 4) };
        body.Children.Add(modLabel);
        foreach (var m in Enum.GetValues<ModifierKey>()) _modifier.Items.Add(m);
        _modifier.SelectedItem = settings.Modifier;
        body.Children.Add(_modifier);
        body.Children.Add(new TextBlock
        {
            Text = "Note: Alt + double-click normally opens Properties in Explorer, and Ctrl/Shift change the selection. RadialConvert blocks the second click only when the menu opens.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 0)
        });

        // Conversions
        body.Children.Add(Header("Conversions"));
        foreach (var group in ConversionRegistry.All.GroupBy(o => o.Group))
        {
            body.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            var wrap = new WrapPanel();
            foreach (var option in group)
            {
                var cb = new CheckBox
                {
                    Content = option.DisplayName,
                    IsChecked = settings.IsEnabled(option.Id),
                    Margin = new Thickness(0, 2, 16, 2),
                    MinWidth = 120
                };
                _toggles[option.Id] = cb;
                wrap.Children.Add(cb);
            }
            body.Children.Add(wrap);
        }

        // Tools
        body.Children.Add(Header("Tools"));
        body.Children.Add(new TextBlock { Text = $"Tools folder: {ToolLocator.ToolsDir}", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var tool in ToolLocator.All)
        {
            var path = ToolLocator.Find(tool.Tool);
            body.Children.Add(new TextBlock
            {
                Text = path != null ? $"\u2714 {tool.Name}: {path}" : $"\u2716 {tool.Name}: not found ({tool.DownloadUrl})",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            });
        }

        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 6)
    };

    private void SaveAndClose()
    {
        _settings.Modifier = (ModifierKey)_modifier.SelectedItem;
        _settings.DisabledConversions.Clear();
        foreach (var (id, cb) in _toggles)
            if (cb.IsChecked != true) _settings.DisabledConversions.Add(id);

        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings:\n{ex.Message}", "RadialConvert", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Saved?.Invoke();
        Close();
    }
}
