using System;
using System.Runtime.InteropServices;

namespace RadialConvert.Native;

internal static class MonitorDpi
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public NativeMethods.RECT rcMonitor;
        public NativeMethods.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativeMethods.POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Scale factor (1.0 = 96 DPI) of the monitor containing the given physical point.</summary>
    public static double ScaleAt(int x, int y)
    {
        try
        {
            var mon = MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0)
                return dx / 96.0;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return 1.0;
    }

    /// <summary>Physical-pixel bounds of the monitor (or its work area) containing the point.</summary>
    public static NativeMethods.RECT BoundsAt(int x, int y, bool workArea)
    {
        var mon = MonitorFromPoint(new NativeMethods.POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref mi)) return workArea ? mi.rcWork : mi.rcMonitor;
        return new NativeMethods.RECT { Left = x - 400, Top = y - 400, Right = x + 400, Bottom = y + 400 };
    }
}
