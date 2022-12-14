using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using static Windows.Win32.PInvoke;
using static Windows.Win32.UI.WindowsAndMessaging.OBJECT_IDENTIFIER;

namespace StageManager4win;

public class WindowWatcher
{
    private readonly HWND _handle;
    private readonly WINEVENTPROC _winEventProc;
    private readonly Dictionary<HWND, WindowInfo> _windows = new();

    internal WindowWatcher(HWND handle)
    {
        _handle = handle;

        _winEventProc = new WINEVENTPROC(OnWindowEvent);
        InstallWindowEventHooks();

        EnumWindows((hwnd, _) =>
        {
            RegisterWindow(hwnd, false);
            return true;
        }, 0);

        void InstallWindowEventHooks()
        {
            SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_SHOW, HINSTANCE.Null, _winEventProc, 0, 0, 0);
            SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, HINSTANCE.Null, _winEventProc, 0, 0, 0);
            SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, HINSTANCE.Null, _winEventProc, 0, 0, 0);
            SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, HINSTANCE.Null, _winEventProc, 0, 0, 0);
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, HINSTANCE.Null, _winEventProc, 0, 0, 0);
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, HINSTANCE.Null, _winEventProc, 0, 0, 0);
        }
    }

    public event Action<WindowInfo>? WindowCreated;
    public event Action<WindowInfo>? WindowDestroyed;
    public event Action<WindowInfo>? WindowUpdated;

    public IEnumerable<WindowInfo> Windows => _windows.Values;

    internal void OnWindowEvent(HWINEVENTHOOK hWinEventHook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (!(idChild == CHILDID_SELF && idObject == (int)OBJID_WINDOW && hwnd != 0))
        {
            return;
        }

        WindowInfo? window;
        switch (eventType)
        {
            case EVENT_OBJECT_SHOW:
                RegisterWindow(hwnd);
                break;
            case EVENT_OBJECT_DESTROY:
                UnregisterWindow(hwnd);
                break;
            case EVENT_OBJECT_UNCLOAKED when _windows.TryGetValue(hwnd, out window):
                WindowUpdated?.Invoke(window);
                break;
            case EVENT_OBJECT_UNCLOAKED:
                RegisterWindow(hwnd);
                break;
            case EVENT_OBJECT_CLOAKED when _windows.TryGetValue(hwnd, out window):
                // TODO: unregister if window is not cloaked by Switch
                if (true)
                {
                    UnregisterWindow(hwnd);
                }
                else
                {
                    WindowUpdated?.Invoke(window);
                }
                break;
            case EVENT_OBJECT_CLOAKED:
                UnregisterWindow(hwnd);
                break;
            case EVENT_SYSTEM_MOVESIZESTART:
                //StartWindowMove(hwnd);
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                //EndWindowMove(hwnd);
                break;
            case EVENT_OBJECT_LOCATIONCHANGE:
                //WindowMove(hwnd);
                break;
            default:
                if (_windows.TryGetValue(hwnd, out window))
                {
                    WindowUpdated?.Invoke(window);
                }
                break;
        }
    }

    private void RegisterWindow(HWND hwnd, bool emitEvent = true)
    {
        if (hwnd == _handle)
        {
            return;
        }

        if (!_windows.ContainsKey(hwnd))
        {
            var window = new WindowInfo(hwnd);
            if (window.ProcessId == Environment.ProcessId)
            {
                return;
            }

            if (IsCandidate(window))
            {
                _windows[hwnd] = window;

                if (emitEvent)
                {
                    WindowCreated?.Invoke(window);
                }
            }
        }
    }

    private void UnregisterWindow(HWND hwnd)
    {
        if (_windows.Remove(hwnd, out var window))
        {
            WindowDestroyed?.Invoke(window);
        }
    }

    private static bool IsCandidate(WindowInfo window) =>
        IsAppWindow(window)
        && IsAltTabWindow(window)
        && IsStageWindow(window);

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
