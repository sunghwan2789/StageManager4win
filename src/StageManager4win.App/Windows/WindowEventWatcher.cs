using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace StageManager4win.Windows;

internal enum WindowEventKind
{
    Changed,
    Foreground,
    DesktopSwitch,
}

internal sealed class WindowEventWatcher : IDisposable
{
    private readonly WINEVENTPROC _callback;
    private readonly List<UnhookWinEventSafeHandle> _hooks = new();
    private bool _started;

    public WindowEventWatcher() => _callback = OnWinEvent;

    public event Action<WindowEventKind, nint>? Raised;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Hook(PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_HIDE);
        Hook(PInvoke.EVENT_OBJECT_CLOAKED, PInvoke.EVENT_OBJECT_UNCLOAKED);
        Hook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND);
        Hook(PInvoke.EVENT_SYSTEM_MINIMIZESTART, PInvoke.EVENT_SYSTEM_MINIMIZEEND);
        Hook(PInvoke.EVENT_SYSTEM_DESKTOPSWITCH, PInvoke.EVENT_SYSTEM_DESKTOPSWITCH);
        Hook(PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE);
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            hook.Dispose();
        }

        _hooks.Clear();
        _started = false;
    }

    private void Hook(uint eventMin, uint eventMax)
    {
        var hook = PInvoke.SetWinEventHook(
            eventMin,
            eventMax,
            default,
            _callback,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT);
        if (!hook.IsInvalid)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(
        HWINEVENTHOOK hook,
        uint eventId,
        HWND hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time)
    {
        switch (eventId)
        {
            case PInvoke.EVENT_SYSTEM_DESKTOPSWITCH:
                Raised?.Invoke(WindowEventKind.DesktopSwitch, 0);
                return;
            case PInvoke.EVENT_SYSTEM_FOREGROUND:
                if (hwnd != HWND.Null && !IsOwnProcess(hwnd))
                {
                    Raised?.Invoke(WindowEventKind.Foreground, (nint)hwnd);
                }

                return;
            default:
                if (hwnd == HWND.Null || IsOwnProcess(hwnd))
                {
                    return;
                }

                Raised?.Invoke(WindowEventKind.Changed, (nint)hwnd);
                return;
        }
    }

    private static bool IsOwnProcess(HWND hwnd)
    {
        PInvoke.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }
}
