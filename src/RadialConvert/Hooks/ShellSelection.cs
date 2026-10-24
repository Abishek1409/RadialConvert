using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using RadialConvert.Native;

namespace RadialConvert.Hooks;

/// <summary>
/// Resolves the file that was just double-clicked. Explorer's focused item (IShellWindows → IShellBrowser →
/// IFolderView2) is always the item that received the first click; UI Automation then confirms the cursor is
/// actually over that item and not over empty space. Must run on an STA thread.
/// </summary>
internal static class ShellSelection
{
    public static string? GetClickedFile(IntPtr shellViewHwnd, int x, int y)
    {
        var focused = GetFocusedItem(shellViewHwnd);
        if (focused == null) return null;

        var (path, displayName, isSelected) = focused.Value;
        bool? overItem = IsPointOverItem(x, y, displayName);

        // If UI Automation is unavailable, fall back to "the focused item is selected".
        bool accept = overItem ?? isSelected;
        return accept ? path : null;
    }

    private static (string Path, string DisplayName, bool IsSelected)? GetFocusedItem(IntPtr shellViewHwnd)
    {
        object? windowsObj = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellGuids.CLSID_ShellWindows, throwOnError: true)!;
            windowsObj = Activator.CreateInstance(type);
            var windows = (IShellWindows)windowsObj!;

            // Desktop first: it is not part of the normal enumeration.
            object loc = 0, root = 0;
            object? desktop = null;
            try { desktop = windows.FindWindowSW(ref loc, ref root, ShellGuids.SWC_DESKTOP, out _, ShellGuids.SWFO_NEEDDISPATCH); }
            catch { /* no desktop view (e.g. Explorer restarting) */ }
            var result = TryBrowser(desktop, shellViewHwnd);
            if (result != null) return result;

            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null;
                try { item = windows.Item(i); } catch { }
                result = TryBrowser(item, shellViewHwnd);
                if (result != null) return result;
            }
        }
        catch
        {
            // COM unavailable: caller treats null as "no file".
        }
        finally
        {
            if (windowsObj != null && Marshal.IsComObject(windowsObj)) Marshal.ReleaseComObject(windowsObj);
        }
        return null;
    }

    private static (string, string, bool)? TryBrowser(object? webBrowser, IntPtr shellViewHwnd)
    {
        if (webBrowser is not IComServiceProvider sp) return null;
        IntPtr pBrowser = IntPtr.Zero;
        try
        {
            var sid = ShellGuids.SID_STopLevelBrowser;
            var iid = ShellGuids.IID_IShellBrowser;
            if (sp.QueryService(ref sid, ref iid, out pBrowser) != 0 || pBrowser == IntPtr.Zero) return null;

            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
            if (browser.QueryActiveShellView(out var view) != 0 || view == null) return null;

            // Match the exact view (Windows 11 tabs share one top-level window).
            if (view.GetWindow(out var viewHwnd) != 0 || viewHwnd != shellViewHwnd) return null;

            if (view is not IFolderView2 folderView) return null;
            if (folderView.GetFocusedItem(out int index) != 0 || index < 0) return null;

            bool isSelected = folderView.GetSelectedItem(index, out int sel) == 0 && sel == index;

            var iidItem = ShellGuids.IID_IShellItem;
            if (folderView.GetItem(index, ref iidItem, out var shellItem) != 0 || shellItem == null) return null;

            // SIGDN_FILESYSPATH fails for virtual items (Control Panel, etc.).
            var path = GetName(shellItem, ShellGuids.SIGDN_FILESYSPATH);
            if (path == null || !System.IO.File.Exists(path)) return null; // folders are not convertible
            var display = GetName(shellItem, ShellGuids.SIGDN_NORMALDISPLAY) ?? System.IO.Path.GetFileName(path);
            return (path, display, isSelected);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pBrowser != IntPtr.Zero) Marshal.Release(pBrowser);
        }
    }

    private static string? GetName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>True/false if UI Automation could tell whether the point is on the named list item; null if unknown.</summary>
    private static bool? IsPointOverItem(int x, int y, string displayName)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            var walker = TreeWalker.ControlViewWalker;
            for (int depth = 0; element != null && depth < 6; depth++)
            {
                var type = element.Current.ControlType;
                if (type == ControlType.ListItem || type == ControlType.DataItem)
                    return string.Equals(element.Current.Name, displayName, StringComparison.OrdinalIgnoreCase);
                if (type == ControlType.List || type == ControlType.Window || type == ControlType.Pane)
                    return false; // reached the container: the click was on empty space
                element = walker.GetParent(element);
            }
            return false;
        }
        catch
        {
            return null;
        }
    }
}
