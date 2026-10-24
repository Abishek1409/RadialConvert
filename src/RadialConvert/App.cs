using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using RadialConvert.Conversion;
using RadialConvert.Hooks;
using RadialConvert.Settings;
using RadialConvert.UI;

namespace RadialConvert;

/// <summary>Tray-only WPF application: no main window, lives until Exit is chosen.</summary>
public sealed class App : Application
{
    private Mutex? _singleInstance;
    private MouseHook? _hook;
    private TrayIcon? _tray;
    private AppSettings _settings = new();
    private RadialMenuWindow? _menu;
    private SettingsWindow? _settingsWindow;

    [STAThread]
    public static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\RadialConvert.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("RadialConvert is already running (see the notification area).", "RadialConvert",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        _settings = AppSettings.Load();
        _tray = new TrayIcon(OpenSettings, () => Shutdown());

        _hook = new MouseHook { Modifier = _settings.Modifier };
        _hook.Triggered += t => Dispatcher.BeginInvoke(() => OnTrigger(t));
        try
        {
            _hook.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not install the mouse hook:\n{ex.Message}", "RadialConvert", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _tray.ShowBalloon("RadialConvert is running",
            $"Hold {_settings.Modifier} and double-click a file in Explorer or on the Desktop.");
    }

    private void OnTrigger(DoubleClickTrigger trigger)
    {
        if (_menu != null) return;

        var file = ShellSelection.GetClickedFile(trigger.ShellViewHwnd, trigger.X, trigger.Y);
        if (file == null) return; // folder, virtual item, or empty space: the click was already swallowed, nothing else to do

        var options = ConversionRegistry.ForFile(file, _settings);
        if (options.Count == 0)
        {
            ToastWindow.ShowMessage("No conversions available",
                $"RadialConvert has no enabled conversions for {Path.GetExtension(file)} files.", error: false);
            return;
        }

        _hook!.Enabled = false; // let every click reach the menu / other apps while it is open
        _menu = new RadialMenuWindow(file, options, trigger.X, trigger.Y);
        _menu.Closed += (_, _) =>
        {
            var chosen = _menu?.Chosen;
            _menu = null;
            _hook.Enabled = true;
            if (chosen != null) ConversionService.Start(file, chosen);
        };
        _menu.Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Saved += () => { if (_hook != null) _hook.Modifier = _settings.Modifier; };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // keep the tray app (and the hook) alive
        try { ToastWindow.ShowMessage("RadialConvert error", e.Exception.Message, error: true); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
