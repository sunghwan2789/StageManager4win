using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Layout;

public static class MonitorTopology
{
    public static string? IdAt(IReadOnlyList<MonitorDescriptor> monitors, double x, double y)
    {
        foreach (var monitor in monitors)
        {
            var bounds = monitor.Bounds;
            if (x >= bounds.Left && x < bounds.Left + bounds.Width &&
                y >= bounds.Top && y < bounds.Top + bounds.Height)
            {
                return monitor.Id;
            }
        }

        return null;
    }

    public static string? IdForBounds(IReadOnlyList<MonitorDescriptor> monitors, WindowBounds bounds)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var x = bounds.CenterX;
        var y = bounds.CenterY;
        var containing = IdAt(monitors, x, y);
        if (containing is not null)
        {
            return containing;
        }

        var nearest = monitors[0];
        var nearestDistance = double.MaxValue;
        foreach (var monitor in monitors)
        {
            var right = monitor.Bounds.Left + monitor.Bounds.Width;
            var bottom = monitor.Bounds.Top + monitor.Bounds.Height;
            var dx = Math.Max(0, Math.Max(monitor.Bounds.Left - x, x - right));
            var dy = Math.Max(0, Math.Max(monitor.Bounds.Top - y, y - bottom));
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monitor;
            }
        }

        return nearest.Id;
    }
}
