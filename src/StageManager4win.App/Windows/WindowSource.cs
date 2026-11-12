using System.Runtime.InteropServices;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Windows;

internal sealed class WindowSource
{
    public WindowId? GetForegroundWindow()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        return hwnd == HWND.Null ? null : new WindowId((nint)hwnd);
    }

    public WindowSnapshot? Describe(nint handle, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var hwnd = new HWND(handle);
        var processId = GetProcessId(hwnd);
        var bounds = GetBounds(hwnd);
        var minimized = PInvoke.IsIconic(hwnd);
        var cloaked = IsCloaked(hwnd);
        var fullscreen = IsFullscreen(handle);
        var traits = new WindowTraits(
            IsTopLevel: true,
            IsVisible: PInvoke.IsWindowVisible(hwnd),
            IsToolWindow: HasExStyle(hwnd, WsExToolWindow),
            IsNoActivate: HasExStyle(hwnd, WsExNoActivate),
            IsTopmost: HasExStyle(hwnd, WsExTopmost),
            IsCloaked: cloaked,
            HasOwner: PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER) != HWND.Null,
            IsShellWindow: hwnd == PInvoke.GetShellWindow(),
            IsOwnProcess: processId == (uint)Environment.ProcessId,
            HasNonZeroSize: bounds.Width > 0 && bounds.Height > 0,
            IsFullscreen: fullscreen);

        if (!WindowFilter.IsCandidate(traits))
        {
            return null;
        }

        var restoreBounds = minimized ? GetRestoreBounds(handle) : bounds;
        var monitorId = MonitorTopology.IdForWindow(monitors, bounds, restoreBounds, minimized);
        if (monitorId is null)
        {
            return null;
        }

        var (appId, appName, executablePath) = GetIdentity(hwnd, processId);
        return new WindowSnapshot(
            new WindowId(handle),
            (int)processId,
            appId,
            appName,
            GetTitle(hwnd),
            executablePath,
            bounds,
            monitorId,
            traits.IsVisible,
            minimized,
            cloaked,
            fullscreen);
    }

    public unsafe bool IsFullscreen(nint handle)
    {
        var hwnd = new HWND(handle);
        // The desktop covers the monitor but is not an app. Counting it as
        // fullscreen would suspend the strip on every wallpaper click.
        if (hwnd == HWND.Null || hwnd == PInvoke.GetShellWindow())
        {
            return false;
        }

        var bounds = GetBounds(hwnd);
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO();
        info.cbSize = (uint)Marshal.SizeOf<MONITORINFO>();
        if (!PInvoke.GetMonitorInfo(monitor, &info))
        {
            return false;
        }

        var monitorBounds = ToBounds(info.rcMonitor);
        return Math.Abs(bounds.Left - monitorBounds.Left) <= 2
            && Math.Abs(bounds.Top - monitorBounds.Top) <= 2
            && Math.Abs(bounds.Width - monitorBounds.Width) <= 2
            && Math.Abs(bounds.Height - monitorBounds.Height) <= 2;
    }

    public IReadOnlyList<WindowSnapshot> Enumerate(
        IReadOnlyList<MonitorDescriptor> monitors,
        IReadOnlySet<string> excludedAppIds)
    {
        var snapshots = new List<WindowSnapshot>();
        var callback = (WNDENUMPROC)((hwnd, _) =>
        {
            var snapshot = Describe((nint)hwnd, monitors);
            if (snapshot is not null && !excludedAppIds.Contains(snapshot.AppId))
            {
                snapshots.Add(snapshot);
            }

            return true;
        });

        if (!PInvoke.EnumWindows(callback, default))
        {
            throw new InvalidOperationException("EnumWindows failed.");
        }

        return snapshots;
    }

    public WindowBounds GetBounds(nint handle) => GetBounds(new HWND(handle));

    public WindowBounds GetRestoreBounds(nint handle)
    {
        var placement = new WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>()
        };
        return PInvoke.GetWindowPlacement(new HWND(handle), ref placement)
            ? ToBounds(placement.rcNormalPosition)
            : GetBounds(handle);
    }

    public bool IsVisible(nint handle) => PInvoke.IsWindowVisible(new HWND(handle));

    private static uint GetProcessId(HWND hwnd)
    {
        PInvoke.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    }

    private static WindowBounds GetBounds(HWND hwnd)
    {
        if (!PInvoke.GetWindowRect(hwnd, out var rect))
        {
            return default;
        }

        return ToBounds(rect);
    }

    private static WindowBounds ToBounds(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;

    private static bool HasExStyle(HWND hwnd, uint style) =>
        ((uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & style) != 0;

    private static unsafe bool IsCloaked(HWND hwnd)
    {
        var cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked,
            sizeof(int)).Succeeded && cloaked != 0;
    }

    private static unsafe string GetTitle(HWND hwnd)
    {
        Span<char> buffer = stackalloc char[512];
        var length = PInvoke.GetWindowText(hwnd, buffer);
        return length > 0 ? buffer[..length].ToString() : string.Empty;
    }

    private static unsafe string? GetAppUserModelId(HWND hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            PInvoke.SHGetPropertyStoreForWindow(hwnd, out store).ThrowOnFailure();
            var key = PInvoke.PKEY_AppUserModel_ID;
            store.GetValue(key, out var value);
            try
            {
                if (!PInvoke.PropVariantToStringAlloc(value, out var text).Succeeded || text.Value is null)
                {
                    return null;
                }

                try
                {
                    var result = text.ToString();
                    return string.IsNullOrWhiteSpace(result) ? null : result;
                }
                finally
                {
                    PInvoke.CoTaskMemFree(text.Value);
                }
            }
            finally
            {
                PInvoke.PropVariantClear(ref value);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static unsafe string? GetExecutablePath(uint processId)
    {
        var process = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            fixed (char* path = buffer)
            {
                if (!PInvoke.QueryFullProcessImageName(
                        process,
                        PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32,
                        path,
                        &size))
                {
                    return null;
                }
            }

            return new string(buffer[..(int)size]);
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    private static (string AppId, string AppName, string? ExecutablePath) GetIdentity(HWND hwnd, uint processId)
    {
        var aumid = GetAppUserModelId(hwnd);
        var executablePath = GetExecutablePath(processId);
        var fileName = executablePath is null ? null : Path.GetFileNameWithoutExtension(executablePath);
        var appId = !string.IsNullOrEmpty(aumid)
            ? aumid
            : executablePath ?? fileName ?? string.Empty;
        var appName = !string.IsNullOrEmpty(aumid)
            ? AumidTail(aumid)
            : fileName ?? appId;
        return (appId, appName, executablePath);
    }

    private static string AumidTail(string aumid)
    {
        var separator = aumid.LastIndexOf('!');
        if (separator < 0)
        {
            separator = aumid.LastIndexOf('.');
        }

        return separator >= 0 && separator < aumid.Length - 1
            ? aumid[(separator + 1)..]
            : aumid;
    }
}
