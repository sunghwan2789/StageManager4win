using StageManager4win.Core.Layout;
using StageManager4win.Windows;

namespace StageManager4win.Input;

public enum StripVisibility
{
    Hidden,
    Occluded,
    Revealed
}

public sealed record StripHitArea(
    RectD BoundsDip,
    RectD EdgeZoneDip,
    RectD StripBoundsDip,
    IReadOnlyList<TilePlacement> Placements,
    double ExposureDip);

internal sealed class AutoHideController : IDisposable
{
    public const double RevealEdgeDip = 2;
    public const double HideExposureDip = 20;
    private const double HideMarginDip = 30;
    private const int HideDelayMilliseconds = 500;

    private readonly DisplayManager _displays;
    private readonly Func<string, StripHitArea?> _getStrip;
    private readonly Action<string, StripVisibility> _setVisibility;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Dictionary<string, long> _outsideSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StripVisibility> _visibility = new(StringComparer.Ordinal);
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private bool _suppressed;
    private bool _disposed;

    public AutoHideController(
        DisplayManager displays,
        Func<string, StripHitArea?> getStrip,
        Action<string, StripVisibility> setVisibility)
    {
        _displays = displays;
        _getStrip = getStrip;
        _setVisibility = setVisibility;
        _monitors = _displays.GetMonitors();
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public void RefreshMonitors()
    {
        _monitors = _displays.GetMonitors();
        _outsideSince.Clear();
    }

    public void Suppress(bool suppressed)
    {
        _suppressed = suppressed;
        _outsideSince.Clear();
        if (!suppressed)
        {
            Poll();
        }
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
            var insideEdge = Inside(area.EdgeZoneDip, x, y);
            var insideStrip = Inside(area.StripBoundsDip, x, y);
            var insideMargin = Inside(Inflate(area.BoundsDip, HideMarginDip), x, y);
            var current = _visibility.GetValueOrDefault(monitor.Id);

            if (insideEdge)
            {
                // Only the 2 DIP edge reveals a hidden strip.
                _outsideSince.Remove(monitor.Id);
                Set(monitor.Id, StripVisibility.Revealed);
                continue;
            }

            if (_suppressed)
            {
                if (insideStrip)
                {
                    Set(monitor.Id, StripVisibility.Revealed);
                }

                _outsideSince.Remove(monitor.Id);
                continue;
            }

            if (current == StripVisibility.Revealed && (insideStrip || insideMargin))
            {
                _outsideSince.Remove(monitor.Id);
                continue;
            }

            if (insideStrip || insideMargin)
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
                Set(
                    monitor.Id,
                    area.ExposureDip > HideExposureDip
                        ? StripVisibility.Occluded
                        : StripVisibility.Hidden);
            }
        }
    }

    private void Set(string monitorId, StripVisibility visibility)
    {
        if (_visibility.GetValueOrDefault(monitorId) == visibility)
        {
            return;
        }

        _visibility[monitorId] = visibility;
        _setVisibility(monitorId, visibility);
    }

    private static RectD Inflate(RectD bounds, double margin) =>
        new(bounds.X - margin, bounds.Y - margin, bounds.Width + margin * 2, bounds.Height + margin * 2);

    private static bool Inside(RectD bounds, double x, double y) =>
        x >= bounds.X && x <= bounds.X + bounds.Width && y >= bounds.Y && y <= bounds.Y + bounds.Height;
}
