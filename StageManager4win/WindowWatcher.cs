using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace StageManager4win;

public class WindowWatcher
{
    private readonly Dictionary<HWND, WindowInfo> _windows;

    public WindowWatcher()
    {
        _windows = GetWindowInfos()
            .Where(window =>
                IsAppWindow(window)
                && IsAltTabWindow(window)
                && IsStageWindow(window))
            .ToDictionary(t => t.Handle, t => t);
    }

    public IEnumerable<WindowInfo> Windows => _windows.Values;

    private static IEnumerable<WindowInfo> GetWindowInfos()
    {
        var handles = new Queue<WindowInfo>();

        EnumWindows((hwnd, _) =>
        {
            handles.Enqueue(new WindowInfo(hwnd));
            return true;
        }, 0);

        return handles;
    }

    private static bool IsAppWindow(WindowInfo window)
    {
        return window.IsTopLevel
            && window.IsVisible
            && window.CanActivate;
    }

    private static bool IsAltTabWindow(WindowInfo window)
    {
        return !window.IsToolWindow
            && !window.IsCloaked;
    }

    private static bool IsStageWindow(WindowInfo window)
    {
        return !window.IsTopMost;
    }
}
