using System;
using System.Runtime.InteropServices;
using System.Threading;
using RadialConvert.Native;
using RadialConvert.Settings;
using static RadialConvert.Native.NativeMethods;

namespace RadialConvert.Hooks;

/// <summary>Arguments for a detected modifier + double-click inside an Explorer/Desktop file view.</summary>
public sealed record DoubleClickTrigger(int X, int Y, IntPtr ShellViewHwnd);

/// <summary>
/// Global low-level mouse hook running on a dedicated thread with its own message loop.
/// The callback only does cheap window-class checks (Windows kills slow LL hooks);
/// resolving the actual file happens on the UI thread after the event is raised.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly LowLevelMouseProc _proc; // kept alive so the GC never collects the delegate
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    private uint _lastDownTime;
    private POINT _lastDownPt;
    private bool _lastDownValid;
    private bool _swallowNextUp;

    private volatile int _modifier = (int)ModifierKey.Shift;

    /// <summary>When false, every event is passed through untouched (e.g. while the menu is open).</summary>
    public volatile bool Enabled = true;

    /// <summary>Raised on the hook thread. Handlers must marshal to the UI thread and return quickly.</summary>
    public event Action<DoubleClickTrigger>? Triggered;

    public MouseHook() => _proc = HookCallback;

    public ModifierKey Modifier
    {
        get => (ModifierKey)_modifier;
        set => _modifier = (int)value;
    }

    public void Start()
    {
        if (_thread != null) return;
        using var ready = new ManualResetEventSlim();
        Exception? error = null;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                error = new InvalidOperationException($"SetWindowsHookEx failed (Win32 error {Marshal.GetLastWin32Error()}).");
            ready.Set();
            if (_hook == IntPtr.Zero) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "RadialConvert.MouseHook",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        ready.Wait();
        if (error != null) throw error;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !Enabled)
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        try
        {
            if (msg == WM_LBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool isDouble = _lastDownValid
                    && unchecked(info.time - _lastDownTime) <= GetDoubleClickTime()
                    && Math.Abs(info.pt.X - _lastDownPt.X) <= GetSystemMetrics(SM_CXDOUBLECLK) / 2
                    && Math.Abs(info.pt.Y - _lastDownPt.Y) <= GetSystemMetrics(SM_CYDOUBLECLK) / 2;

                _lastDownTime = info.time;
                _lastDownPt = info.pt;
                _lastDownValid = true;

                if (isDouble && IsModifierHeld() && TryGetExplorerView(info.pt, out var view))
                {
                    _lastDownValid = false; // a third click must not count as another double click
                    _swallowNextUp = true;
                    Triggered?.Invoke(new DoubleClickTrigger(info.pt.X, info.pt.Y, view));
                    return (IntPtr)1; // swallow: Explorer never sees the second click, so nothing opens
                }
            }
            else if (msg == WM_LBUTTONUP && _swallowNextUp)
            {
                _swallowNextUp = false;
                return (IntPtr)1;
            }
            else if (msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                _lastDownValid = false;
            }
        }
        catch
        {
            // Never let an exception escape into the OS hook chain.
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool IsModifierHeld()
    {
        int vk = (ModifierKey)_modifier switch
        {
            ModifierKey.Ctrl => VK_CONTROL,
            ModifierKey.Alt => VK_MENU,
            _ => VK_SHIFT
        };
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static readonly string[] ExplorerRoots = { "CabinetWClass", "ExploreWClass" };
    private static readonly string[] DesktopRoots = { "Progman", "WorkerW" };

    /// <summary>
    /// True when the point is inside a SHELLDLL_DefView (the file list) whose top-level window is an
    /// Explorer window or the Desktop, and that window is in the foreground.
    /// </summary>
    internal static bool TryGetExplorerView(POINT pt, out IntPtr shellView)
    {
        shellView = IntPtr.Zero;
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;

        for (var h = hwnd; h != IntPtr.Zero; h = GetParent(h))
        {
            if (ClassNameOf(h) == "SHELLDLL_DefView") { shellView = h; break; }
        }
        if (shellView == IntPtr.Zero) return false;

        var root = GetAncestor(shellView, GA_ROOT);
        var rootClass = ClassNameOf(root);
        var fgRoot = GetAncestor(GetForegroundWindow(), GA_ROOT);

        if (Array.IndexOf(ExplorerRoots, rootClass) >= 0)
            return fgRoot == root;

        if (Array.IndexOf(DesktopRoots, rootClass) >= 0)
            return Array.IndexOf(DesktopRoots, ClassNameOf(fgRoot)) >= 0;

        return false;
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _thread = null;
    }
}
