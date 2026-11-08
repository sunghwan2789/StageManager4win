using StageManager4win.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Windows;

internal static class WindowActivation
{
    // A quick Alt tap satisfies the foreground lock when there is no user input.
    private const byte VkMenu = 0x12;

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

        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, out _);
        var currentThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread != 0
            && foregroundThread != currentThread
            && PInvoke.AttachThreadInput(currentThread, foregroundThread, true))
        {
            try
            {
                if (PInvoke.SetForegroundWindow(hwnd))
                {
                    return true;
                }
            }
            finally
            {
                PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        PInvoke.SwitchToThisWindow(hwnd, true);
        if (PInvoke.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        PInvoke.keybd_event(VkMenu, 0, 0, 0);
        PInvoke.keybd_event(VkMenu, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
        return PInvoke.SetForegroundWindow(hwnd);
    }
}
