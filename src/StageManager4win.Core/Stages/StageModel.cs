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

        ApplyForeground(monitor, window);
        return true;
    }

    public bool Activate(WindowId window) => SetForeground(window);

    public void Restore(MonitorState state)
    {
        if (!_monitors.TryGetValue(state.MonitorId, out var monitor))
        {
            monitor = new MonitorData();
            _monitors[state.MonitorId] = monitor;
        }

        monitor.Stage.Clear();
        monitor.Stage.AddRange(state.Stage);
        monitor.Strip.Clear();
        monitor.Strip.AddRange(state.Strip);
        monitor.Foreground = state.Foreground;
        monitor.ActiveAppId = state.ActiveAppId;
    }

    public void SetMode(WindowMode mode)
    {
        _mode = mode;

        foreach (var monitor in _monitors.Values)
        {
            if (monitor.Foreground is { } foreground)
            {
                ApplyForeground(monitor, foreground);
            }
        }
    }

    public void Add(WindowSnapshot window)
    {
        if (_windows.ContainsKey(window.Id))
        {
            Update(window);
            return;
        }

        _windows[window.Id] = window;

        if (!_monitors.TryGetValue(window.MonitorId, out var monitor))
        {
            monitor = new MonitorData();
            _monitors[window.MonitorId] = monitor;
        }

        if (_mode == WindowMode.AllAtOnce
            && monitor.ActiveAppId == window.AppId
            && window.IsVisible
            && !window.IsMinimized)
        {
            monitor.Stage.Insert(0, window.Id);
        }
        else
        {
            monitor.Strip.Insert(0, window.Id);
        }
    }

    public void Update(WindowSnapshot window)
    {
        if (!_windows.TryGetValue(window.Id, out var existing))
        {
            Add(window);
            return;
        }

        _windows[window.Id] = window;

        if (existing.MonitorId == window.MonitorId)
        {
            return;
        }

        var oldMonitor = _monitors[existing.MonitorId];
        oldMonitor.Stage.Remove(window.Id);
        oldMonitor.Strip.Remove(window.Id);

        if (oldMonitor.Foreground == window.Id)
        {
            oldMonitor.Foreground = null;
        }

        if (oldMonitor.Stage.Count == 0 && oldMonitor.Strip.Count > 0)
        {
            var promoted = oldMonitor.Strip[0];
            oldMonitor.Strip.RemoveAt(0);
            oldMonitor.Stage.Add(promoted);
            oldMonitor.ActiveAppId = _windows[promoted].AppId;
        }
        else if (oldMonitor.ActiveAppId is { } activeAppId
                 && !HasWindowsOnMonitor(existing.MonitorId, activeAppId))
        {
            oldMonitor.ActiveAppId = null;
        }

        if (oldMonitor.Stage.Count == 0 && oldMonitor.Strip.Count == 0)
        {
            _monitors.Remove(existing.MonitorId);
        }

        if (!_monitors.TryGetValue(window.MonitorId, out var newMonitor))
        {
            newMonitor = new MonitorData();
            _monitors[window.MonitorId] = newMonitor;
        }

        newMonitor.Strip.Insert(0, window.Id);
    }

    public void Minimize(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return;
        }

        _windows[window] = snapshot with { IsMinimized = true };

        var monitor = _monitors[snapshot.MonitorId];
        if (monitor.Stage.Remove(window))
        {
            monitor.Strip.Insert(0, window);

            if (monitor.Foreground == window)
            {
                monitor.Foreground = null;
            }
        }
    }

    public void Restore(WindowId window)
    {
        if (!_windows.TryGetValue(window, out var snapshot))
        {
            return;
        }

        _windows[window] = snapshot with { IsMinimized = false };
    }

    public void ReorderStrip(string monitorId, IReadOnlyList<WindowId> ordered)
    {
        if (!_monitors.TryGetValue(monitorId, out var monitor))
        {
            return;
        }

        var reordered = ordered.Where(monitor.Strip.Contains).Distinct().ToList();

        foreach (var id in monitor.Strip)
        {
            if (!reordered.Contains(id))
            {
                reordered.Add(id);
            }
        }

        monitor.Strip.Clear();
        monitor.Strip.AddRange(reordered);
    }

    public void Remove(WindowId window)
    {
        if (!_windows.Remove(window, out var snapshot))
        {
            return;
        }

        var monitor = _monitors[snapshot.MonitorId];
        monitor.Stage.Remove(window);
        monitor.Strip.Remove(window);

        if (monitor.Foreground == window)
        {
            monitor.Foreground = null;
        }

        if (monitor.ActiveAppId is { } activeAppId
            && !HasWindowsOnMonitor(snapshot.MonitorId, activeAppId))
        {
            monitor.ActiveAppId = null;
        }

        if (monitor.Stage.Count == 0 && monitor.Strip.Count == 0)
        {
            _monitors.Remove(snapshot.MonitorId);
        }
    }

    private void ApplyForeground(MonitorData monitor, WindowId foreground)
    {
        var snapshot = _windows[foreground];
        var ordered = monitor.Stage.Concat(monitor.Strip).Distinct().ToArray();

        var stage = new List<WindowId> { foreground };

        if (_mode == WindowMode.AllAtOnce)
        {
            foreach (var id in ordered)
            {
                if (id == foreground)
                {
                    continue;
                }

                var candidate = _windows[id];
                if (candidate.AppId == snapshot.AppId
                    && candidate.IsVisible
                    && !candidate.IsMinimized)
                {
                    stage.Add(id);
                }
            }
        }

        var stageSet = stage.ToHashSet();
        var strip = new List<WindowId>();

        foreach (var id in monitor.Stage)
        {
            if (!stageSet.Contains(id))
            {
                strip.Add(id);
            }
        }

        foreach (var id in ordered)
        {
            if (!stageSet.Contains(id) && !strip.Contains(id))
            {
                strip.Add(id);
            }
        }

        monitor.Stage.Clear();
        monitor.Stage.AddRange(stage);
        monitor.Strip.Clear();
        monitor.Strip.AddRange(strip);
        monitor.Foreground = foreground;
        monitor.ActiveAppId = snapshot.AppId;
    }

    private bool HasWindowsOnMonitor(string monitorId, string appId) =>
        _windows.Values.Any(window => window.MonitorId == monitorId && window.AppId == appId);

    private sealed class MonitorData
    {
        public List<WindowId> Stage { get; } = new();
        public List<WindowId> Strip { get; } = new();
        public WindowId? Foreground { get; set; }
        public string? ActiveAppId { get; set; }
    }
}
