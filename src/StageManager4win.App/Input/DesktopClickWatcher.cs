using System.Runtime.InteropServices;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Input;

internal sealed class DesktopClickWatcher : IDisposable
{
    private readonly Func<Point, bool> _isOverlayPoint;
    private readonly SynchronizationContext _context;
    private readonly HOOKPROC _callback;
    private UnhookWindowsHookExSafeHandle? _hook;
    private Point _downPoint;
    private long _downTick;
    private bool _tracking;
    private bool _disposed;

    public DesktopClickWatcher(Func<Point, bool> isOverlayPoint)
    {
        _isOverlayPoint = isOverlayPoint;
        _context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _callback = OnMouse;
    }

    public event Action? WallpaperClicked;

    public void Start()
    {
        if (_hook is not null || _disposed)
        {
            return;
        }

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _callback, default, 0);
        if (!_hook.IsInvalid)
        {
            return;
        }

        FileLogger.Default.Write(
            "desktop-click",
            $"hook install failed {Marshal.GetLastWin32Error()}");
        _hook.Dispose();
        _hook = null;
    }

    public void Stop()
    {
        _tracking = false;
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private LRESULT OnMouse(int nCode, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var message = (uint)wParam.Value;
                if (message == PInvoke.WM_LBUTTONDOWN)
                {
                    HandleDown(lParam);
                }
                else if (message == PInvoke.WM_LBUTTONUP)
                {
                    HandleUp(lParam);
                }
            }
        }
        catch (Exception)
        {
            _tracking = false;
        }

        return PInvoke.CallNextHookEx(default, nCode, wParam, lParam);
    }

    private void HandleDown(LPARAM lParam)
    {
        _downPoint = PointOf(lParam);
        _downTick = Environment.TickCount64;
        _tracking = true;
    }

    private void HandleUp(LPARAM lParam)
    {
        if (!_tracking)
        {
            return;
        }

        _tracking = false;
        var point = PointOf(lParam);
        var elapsed = Environment.TickCount64 - _downTick;
        var dx = point.X - _downPoint.X;
        var dy = point.Y - _downPoint.Y;
        if (!DesktopClick.IsToggle(dx, dy, elapsed))
        {
            return;
        }

        // The hook must stay cheap: MSAA runs on the UI thread after the callback returns.
        var downPoint = _downPoint;
        _context.Post(
            _ =>
            {
                if (_disposed || _hook is null)
                {
                    return;
                }

                if (IsWallpaper(downPoint))
                {
                    WallpaperClicked?.Invoke();
                }
            },
            null);
    }

    private static Point PointOf(LPARAM lParam)
    {
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam.Value);
        return data.pt;
    }

    private bool IsWallpaper(Point point)
    {
        try
        {
            return !_isOverlayPoint(point) && IsDesktopWindow(point) && !IsIconAt(point);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDesktopWindow(Point point)
    {
        var hwnd = PInvoke.WindowFromPoint(point);
        if (hwnd == HWND.Null)
        {
            return false;
        }

        for (var current = hwnd; current != HWND.Null; current = PInvoke.GetParent(current))
        {
            if (DesktopClick.IsDesktopClass(GetClassName(current)))
            {
                return true;
            }
        }

        for (var current = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
             current != HWND.Null;
             current = PInvoke.GetParent(current))
        {
            if (DesktopClick.IsDesktopClass(GetClassName(current)))
            {
                return true;
            }
        }

        return false;
    }

    private static unsafe string GetClassName(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer);
        return length > 0 ? buffer[..length].ToString() : string.Empty;
    }

    private static bool IsIconAt(Point point)
    {
        IAccessible? accessible = null;
        object? child = null;
        try
        {
            if (PInvoke.AccessibleObjectFromPoint(point, out accessible, out child).Failed
                || accessible is null)
            {
                return true;
            }

            if (child is int childId && childId != 0)
            {
                return true;
            }

            var role = accessible.get_accRole(0);
            return role is int roleValue && (uint)roleValue == PInvoke.ROLE_SYSTEM_LISTITEM;
        }
        catch (Exception)
        {
            return true;
        }
        finally
        {
            ReleaseCom(child);
            ReleaseCom(accessible);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
