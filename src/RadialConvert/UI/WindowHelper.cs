using System;
using System.Windows;
using System.Windows.Interop;
using RadialConvert.Native;

namespace RadialConvert.UI;

internal static class WindowHelper
{
    /// <summary>Moves/resizes a window using physical pixel coordinates (bypasses WPF DIP conversion).</summary>
    public static void SetPhysicalBounds(Window window, NativeMethods.RECT r)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Brings our window to the foreground even though Explorer currently owns it. Windows blocks plain
    /// SetForegroundWindow from background processes, so we briefly attach to the foreground thread's input.
    /// </summary>
    public static void ForceActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var fg = NativeMethods.GetForegroundWindow();
        uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, IntPtr.Zero);
        uint ourThread = NativeMethods.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != ourThread && NativeMethods.AttachThreadInput(ourThread, fgThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
            window.Activate();
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(ourThread, fgThread, false);
        }
    }
}
