using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RadialConvert.Native;
using RadialConvert.Settings;

namespace RadialConvert.UI;

/// <summary>Notification-area icon with Settings / Start with Windows / Exit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ToolStripMenuItem _startup;

    public TrayIcon(Action openSettings, Action exit)
    {
        _image = CreateIcon();
        _startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _startup.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => openSettings());
        menu.Items.Add(_startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        menu.Opening += (_, _) => _startup.Checked = StartupManager.IsEnabled;

        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "RadialConvert",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    private void ToggleStartup()
    {
        try
        {
            StartupManager.Set(!StartupManager.IsEnabled);
            _startup.Checked = StartupManager.IsEnabled;
        }
        catch (Exception ex)
        {
            ToastWindow.ShowMessage("Could not change startup setting", ex.Message, error: true);
        }
    }

    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    /// <summary>Draws a simple ring icon at runtime so the repo needs no binary .ico file.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var ring = new Pen(Color.FromArgb(0x2F, 0x6F, 0xE0), 7);
            g.DrawEllipse(ring, 5, 5, 22, 22);
            using var gap = new Pen(Color.White, 2);
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 4 + i * Math.PI / 2;
                g.DrawLine(gap, (float)(16 + 7 * Math.Cos(a)), (float)(16 + 7 * Math.Sin(a)),
                    (float)(16 + 15 * Math.Cos(a)), (float)(16 + 15 * Math.Sin(a)));
            }
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 13, 13, 6, 6);
        }
        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { NativeMethods.DestroyIcon(hIcon); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
