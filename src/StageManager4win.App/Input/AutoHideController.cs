using StageManager4win.Core.Layout;
using StageManager4win.Windows;

namespace StageManager4win.Input;

public sealed record StripHitArea(RectD BoundsDip, RectD EdgeZoneDip);

internal sealed class AutoHideController : IDisposable
{
    public const double RevealEdgeDip = 2;
    private const double HideMarginDip = 30;
    private const int HideDelayMilliseconds = 500;

    private readonly DisplayManager _displays;
    private readonly Func<string, StripHitArea?> _getStrip;
    private readonly Action<string, bool> _setVisible;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Dictionary<string, long> _outsideSince = new(StringComparer.Ordinal);
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private bool _disposed;

    public AutoHideController(
        DisplayManager displays,
        Func<string, StripHitArea?> getStrip,
        Action<string, bool> setVisible)
    {
        _displays = displays;
        _getStrip = getStrip;
        _setVisible = setVisible;
        _monitors = _displays.GetMonitors();
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public void RefreshMonitors()
    {
        _monitors = _displays.GetMonitors();
        _outsideSince.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var position = Cursor.Position;
        var now = Environment.TickCount64;
        foreach (var monitor in _monitors)
        {
            if (_getStrip(monitor.Id) is not { } area)
            {
                _outsideSince.Remove(monitor.Id);
                continue;
            }

            var scale = 96.0 / monitor.Dpi;
            var x = (position.X - monitor.Bounds.Left) * scale;
            var y = (position.Y - monitor.Bounds.Top) * scale;
            if (Inside(area.EdgeZoneDip, x, y))
            {
                // Only the edge reveals a hidden strip; hovering its old area does not.
                _outsideSince.Remove(monitor.Id);
                _setVisible(monitor.Id, true);
                continue;
            }

            if (Inside(Inflate(area.BoundsDip, HideMarginDip), x, y))
            {
                _outsideSince.Remove(monitor.Id);
                continue;
            }

            if (!_outsideSince.TryGetValue(monitor.Id, out var since))
            {
                _outsideSince[monitor.Id] = now;
            }
            else if (now - since >= HideDelayMilliseconds)
            {
                _setVisible(monitor.Id, false);
            }
        }
    }

    private static RectD Inflate(RectD bounds, double margin) =>
        new(bounds.X - margin, bounds.Y - margin, bounds.Width + margin * 2, bounds.Height + margin * 2);

    private static bool Inside(RectD bounds, double x, double y) =>
        x >= bounds.X && x <= bounds.X + bounds.Width && y >= bounds.Y && y <= bounds.Y + bounds.Height;
}
