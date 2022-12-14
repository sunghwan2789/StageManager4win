using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE;
using static Windows.Win32.PInvoke;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE;
using static Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE;

namespace StageManager4win;

public class WindowInfo
{
    private readonly HWND _handle;
    private readonly int _processId;
    private WINDOWINFO _raw;

    internal WindowInfo(HWND handle)
    {
        _handle = handle;
        unsafe
        {
            fixed (int* p = &_processId)
            {
                _ = GetWindowThreadProcessId(_handle, (uint*)p);
            }
        }


        _raw = new WINDOWINFO
        {
            cbSize = (uint)Marshal.SizeOf<WINDOWINFO>(),
        };

        Update();
    }

    public bool IsTopLevel => (_raw.dwStyle & (uint)WS_CHILD) == 0;

    public bool IsVisible => (_raw.dwStyle & (uint)WS_VISIBLE) != 0;

    public bool IsIconic => (_raw.dwStyle & (uint)WS_ICONIC) != 0;

    public bool CanActivate => (_raw.dwExStyle & (uint)WS_EX_NOACTIVATE) == 0;

    public bool IsToolWindow => (_raw.dwExStyle & (uint)WS_EX_TOOLWINDOW) != 0;

    public bool IsAppWindow => (_raw.dwExStyle & (uint)WS_EX_APPWINDOW) != 0;

    public bool IsTopMost => (_raw.dwExStyle & (uint)WS_EX_TOPMOST) != 0;

    public bool IsCloaked
    {
        get
        {
            bool cloaked;
            unsafe
            {
                var result = DwmGetWindowAttribute(_handle, DWMWA_CLOAKED, &cloaked, (uint)Marshal.SizeOf<bool>());
                result.ThrowOnFailure();
            }
            return cloaked;
        }
    }

    internal HWND Handle => _handle;

    internal int ProcessId => _processId;

    public void Update()
    {
        GetWindowInfo(_handle, ref _raw);
    }
}
