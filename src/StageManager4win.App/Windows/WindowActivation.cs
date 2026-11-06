using StageManager4win.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Windows;

internal static class WindowActivation
{
    public static void Restore(WindowId window)
    {
        var hwnd = new HWND((nint)window.Value);
        if (PInvoke.IsIconic(hwnd))
        {
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }
    }

    public static bool Activate(WindowId window)
    {
        var hwnd = new HWND((nint)window.Value);
        Restore(window);
        if (PInvoke.SetForegroundWindow(hwnd))
        {
            return true;
        }

        var foregroundThread = PInvoke.GetWindowThreadProcessId(
            PInvoke.GetForegroundWindow(),
            out _);
        var currentThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == currentThread)
        {
            return false;
        }

        if (!PInvoke.AttachThreadInput(currentThread, foregroundThread, true))
        {
            return false;
        }

        try
        {
            return PInvoke.SetForegroundWindow(hwnd);
        }
        finally
        {
            PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
