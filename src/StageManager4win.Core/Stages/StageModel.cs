using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public sealed class StageModel
{
    private readonly Dictionary<WindowId, WindowSnapshot> _windows = new();
    private readonly Dictionary<string, MonitorData> _monitors = new(StringComparer.Ordinal);
    private WindowMode _mode;

    public StageModel(WindowMode mode = WindowMode.OneAtATime) => _mode = mode;

    public WindowMode Mode => _mode;

    public IReadOnlyDictionary<WindowId, WindowSnapshot> Windows => _windows;

    public IReadOnlyDictionary<string, MonitorState> Monitors =>
        _monitors.ToDictionary(
            pair => pair.Key,
            pair => new MonitorState(
                pair.Key,
                pair.Value.Stage.ToArray(),
                pair.Value.Strip.ToArray(),
                pair.Value.Foreground,
                pair.Value.ActiveAppId),
            StringComparer.Ordinal);

    public void Reset(IEnumerable<WindowSnapshot> windows, WindowId? foreground = null)
    {
        _windows.Clear();
        _monitors.Clear();

        foreach (var window in windows)
        {
            _windows[window.Id] = window;

            if (!_monitors.TryGetValue(window.MonitorId, out var monitor))
            {
                monitor = new MonitorData();
                _monitors[window.MonitorId] = monitor;
            }

            monitor.Strip.Add(window.Id);
        }

        if (foreground is { } id)
        {
            SetForeground(id);
        }
    }

    public bool SetForeground(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return false;
        }

        var monitor = _monitors[snapshot.MonitorId];
        if (monitor.Foreground == window)
        {
            return true;
        }

        var previousStage = monitor.Stage.ToArray();
        monitor.Stage.Clear();
        monitor.Stage.Add(window);
        monitor.Strip.Remove(window);

        var previousStageToRestore = previousStage
            .Where(previous => previous != window && !monitor.Strip.Contains(previous))
            .ToArray();
        monitor.Strip.InsertRange(0, previousStageToRestore);

        monitor.Foreground = window;
        monitor.ActiveAppId = snapshot.AppId;
        return true;
    }

    public void SetMode(WindowMode mode) => _mode = mode;

    private sealed class MonitorData
    {
        public List<WindowId> Stage { get; } = new();
        public List<WindowId> Strip { get; } = new();
        public WindowId? Foreground { get; set; }
        public string? ActiveAppId { get; set; }
    }
}
