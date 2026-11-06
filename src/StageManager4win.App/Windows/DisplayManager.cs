using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace StageManager4win.Windows;

internal sealed class DisplayManager
{
    public unsafe IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var monitors = new List<MonitorDescriptor>();
        var callback = (MONITORENUMPROC)((monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                return true;
            }

            var dpiX = 96u;
            var dpiY = 96u;
            PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpiX, &dpiY);

            var bounds = ToBounds(info.monitorInfo.rcMonitor);
            var workArea = ToBounds(info.monitorInfo.rcWork);
            var device = info.szDevice.ToString();
            monitors.Add(new MonitorDescriptor(device, bounds, workArea, dpiX));
            return true;
        });

        if (!PInvoke.EnumDisplayMonitors(default, null, callback, default))
        {
            throw new InvalidOperationException("EnumDisplayMonitors failed.");
        }

        return monitors;
    }

    private static WindowBounds ToBounds(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
}
