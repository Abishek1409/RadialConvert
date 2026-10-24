using System;
using System.Runtime.InteropServices;

namespace RadialConvert.Native;

// Minimal hand-written Shell COM declarations. Only the vtable slots we call are typed;
// the rest are placeholders that keep the slot order correct.

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindows
{
    int Count { get; }
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object Item([MarshalAs(UnmanagedType.Struct)] object index);
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object _NewEnum();
    void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
    void RegisterPending(int lThreadId, [MarshalAs(UnmanagedType.Struct)] ref object pvarloc,
        [MarshalAs(UnmanagedType.Struct)] ref object pvarlocRoot, int swClass, out int plCookie);
    void Revoke(int lCookie);
    void OnNavigate(int lCookie, [MarshalAs(UnmanagedType.Struct)] ref object pvarLoc);
    void OnActivated(int lCookie, [MarshalAs(UnmanagedType.VariantBool)] bool fActive);
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
        [MarshalAs(UnmanagedType.Struct)] ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions);
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IComServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
    [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
    [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
    [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
    [PreserveSig] int EnableModelessSB(bool fEnable);
    [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
    [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
    [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
    [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
    [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
    [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView ppshv);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    // Remaining methods not needed.
}

[ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView2
{
    // IFolderView
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint ViewMode);
    [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
    [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
    [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
    [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetSelectionMarkedItem(out int piItem);
    [PreserveSig] int GetFocusedItem(out int piItem);
    [PreserveSig] int GetItemPosition(IntPtr pidl, out NativeMethods.POINT ppt);
    [PreserveSig] int GetSpacing(ref NativeMethods.POINT ppt);
    [PreserveSig] int GetDefaultSpacing(out NativeMethods.POINT ppt);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int iItem, uint dwFlags);
    [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    // IFolderView2
    [PreserveSig] int SetGroupBy(IntPtr key, bool fAscending);
    [PreserveSig] int GetGroupBy(IntPtr pkey, out bool pfAscending);
    [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr propvar);
    [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr ppropvar);
    [PreserveSig] int SetTileViewProperties(IntPtr pidl, IntPtr pszPropList);
    [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, IntPtr pszPropList);
    [PreserveSig] int SetText(int iType, IntPtr pwszText);
    [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
    [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
    [PreserveSig] int GetSortColumnCount(out int pcColumns);
    [PreserveSig] int SetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetItem(int iItem, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
    [PreserveSig] int GetVisibleItem(int iStart, bool fPrevious, out int piItem);
    [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
    // Remaining methods not needed.
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

internal static class ShellGuids
{
    public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public const int SWC_DESKTOP = 8;
    public const int SWFO_NEEDDISPATCH = 1;

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_PARENTRELATIVEEDITING = 0x80031001;
    public const uint SIGDN_FILESYSPATH = 0x80058000;
}
