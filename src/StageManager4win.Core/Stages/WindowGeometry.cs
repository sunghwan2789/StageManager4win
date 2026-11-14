using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public sealed record WindowGeometry(
    RectD BoundsDip,
    string MonitorId,
    uint Dpi,
    bool IsMaximized,
    RectD? RestoreBoundsDip)
{
    public static WindowGeometry FromPixels(
        WindowBounds bounds,
        string monitorId,
        uint dpi,
        bool isMaximized,
        WindowBounds? restoreBounds = null)
    {
        var normalized = NormalizeDpi(dpi);
        var scale = 96.0 / normalized;
        return new WindowGeometry(
            new RectD(
                bounds.Left * scale,
                bounds.Top * scale,
                bounds.Width * scale,
                bounds.Height * scale),
            monitorId,
            normalized,
            isMaximized,
            restoreBounds is { } restore
                ? new RectD(
                    restore.Left * scale,
                    restore.Top * scale,
                    restore.Width * scale,
                    restore.Height * scale)
                : null);
    }

    public WindowBounds ToPixels(uint dpi) =>
        ToPixels(BoundsDip, dpi);

    public WindowBounds RestoreBoundsToPixels(uint dpi) =>
        ToPixels(RestoreBoundsDip ?? BoundsDip, dpi);

    public WindowGeometry ClampInto(RectD workArea)
    {
        var maxX = workArea.X + Math.Max(0, workArea.Width - BoundsDip.Width);
        var maxY = workArea.Y + Math.Max(0, workArea.Height - BoundsDip.Height);
        return this with
        {
            BoundsDip = BoundsDip with
            {
                X = Math.Clamp(BoundsDip.X, workArea.X, maxX),
                Y = Math.Clamp(BoundsDip.Y, workArea.Y, maxY)
            }
        };
    }

    public static MonitorDescriptor? SelectMonitor(
        IReadOnlyList<MonitorDescriptor> monitors,
        RectD bounds)
    {
        MonitorDescriptor? best = null;
        var bestArea = 0.0;
        foreach (var monitor in monitors)
        {
            var area = IntersectionArea(bounds, ToRect(monitor.Bounds));
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        if (best is not null)
        {
            return best;
        }

        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        var nearest = double.MaxValue;
        foreach (var monitor in monitors)
        {
            var monitorCenterX = monitor.Bounds.Left + monitor.Bounds.Width / 2.0;
            var monitorCenterY = monitor.Bounds.Top + monitor.Bounds.Height / 2.0;
            var distance = Math.Pow(centerX - monitorCenterX, 2)
                + Math.Pow(centerY - monitorCenterY, 2);
            if (distance < nearest)
            {
                nearest = distance;
                best = monitor;
            }
        }

        return best;
    }

    private static WindowBounds ToPixels(RectD bounds, uint dpi)
    {
        var scale = NormalizeDpi(dpi) / 96.0;
        return new WindowBounds(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    private static uint NormalizeDpi(uint dpi) => dpi == 0 ? 96u : dpi;

    private static RectD ToRect(WindowBounds bounds) =>
        new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);

    private static double IntersectionArea(RectD first, RectD second)
    {
        var width = Math.Min(
            first.X + first.Width,
            second.X + second.Width) - Math.Max(first.X, second.X);
        var height = Math.Min(
            first.Y + first.Height,
            second.Y + second.Height) - Math.Max(first.Y, second.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
