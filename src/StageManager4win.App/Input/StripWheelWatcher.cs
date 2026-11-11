using System.Runtime.InteropServices;
using StageManager4win.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Input;

internal sealed class StripWheelWatcher : IDisposable
{
    private const int WmMouseWheel = 0x020A;

    private readonly Action<Point, int> _wheeled;
    private readonly SynchronizationContext _context;
    private readonly HOOKPROC _callback;
    private UnhookWindowsHookExSafeHandle? _hook;
    private bool _disposed;

    public StripWheelWatcher(Action<Point, int> wheeled)
    {
        _wheeled = wheeled;
        _context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _callback = OnMouse;
    }

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
            "strip-wheel",
            $"hook install failed {Marshal.GetLastWin32Error()}");
        _hook.Dispose();
        _hook = null;
    }

    public void Stop()
    {
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
            if (nCode >= 0 && (uint)wParam.Value == WmMouseWheel)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam.Value);
                var delta = (short)((data.mouseData >> 16) & 0xFFFF);
                if (delta != 0)
                {
                    // The hook must stay cheap: resolve the strip on the UI thread.
                    var point = data.pt;
                    _context.Post(_ => _wheeled(point, delta), null);
                }
            }
        }
        catch (Exception)
        {
        }

        return PInvoke.CallNextHookEx(default, nCode, wParam, lParam);
    }
}
