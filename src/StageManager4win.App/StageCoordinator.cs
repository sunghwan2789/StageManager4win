using System.Reflection;
using System.Runtime.InteropServices;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using StageManager4win.Overlay;
using StageManager4win.Rendering;
using StageManager4win.Windows;

namespace StageManager4win;

internal sealed class StageCoordinator : IDisposable
{
    private const double TileCornerRadiusDip = 12;
    private const uint FallbackTileColor = 0xFF3B82F6;

    private readonly SettingsStore _store;
    private readonly DisplayManager _display = new();
    private readonly WindowSource _source = new();
    private readonly WindowEventWatcher _watcher = new();
    private readonly RenderThread _render = new();
    private readonly StageModel _model;
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _visibleByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _firstVisibleIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _dpiByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IconBitmap?> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<WindowId, double> _lastAspect = new();
    private readonly IconBitmap _fallbackIcon = IconBitmap.Solid(1, 1, FallbackTileColor);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _cycleReset = new() { Interval = 2000 };
    private AppSettings _settings;
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private CycleCursor? _cycle;
    private string? _cycleMonitorId;
    private bool _rebuildPending;
    private bool _started;
    private bool _disposed;

    public StageCoordinator(SettingsStore settingsStore)
    {
        _store = settingsStore;
        _settings = _store.Load();
        _model = new StageModel(_settings.WindowMode);
        _debounce.Tick += (_, _) => OnDebounceTick();
        _cycleReset.Tick += (_, _) => OnCycleResetTick();
    }

    public event Action<string>? StatusMessage;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _monitors = _display.GetMonitors();
        _render.Start();
        _render.Post(new RenderCommand.SetPreviewFps(_settings.PreviewFps));
        foreach (var monitor in _monitors)
        {
            var overlay = new OverlayWindow(monitor);
            var monitorId = monitor.Id;
            overlay.TileClicked += ActivateWindow;
            overlay.HoverChanged += window =>
                _render.Post(new RenderCommand.SetOverlayHover(monitorId, window));
            overlay.ScrollRequested += delta => Scroll(monitorId, delta);
            overlay.DpiChanged += dpi => OnDpiChanged(monitorId, dpi);
            overlay.Show();
            _overlays[monitorId] = overlay;
            _dpiByMonitor[monitorId] = monitor.Dpi;
            _render.Post(new RenderCommand.CreateOverlay(monitorId, overlay.Handle, monitor.Dpi));
        }

        _model.Reset(Enumerate(), _source.GetForegroundWindow());
        _watcher.Raised += OnWindowEvent;
        _watcher.Start();
        Refresh();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Normalize();
        _model.SetMode(_settings.WindowMode);
        _render.Post(new RenderCommand.SetPreviewFps(_settings.PreviewFps));
        if (_settings.Enabled)
        {
            Reconcile();
        }
        else
        {
            Refresh();
        }
    }

    public void ToggleStripUnderPointer()
    {
        if (MonitorUnderPointer() is not { } monitorId)
        {
            return;
        }

        _visibleByMonitor[monitorId] = !_visibleByMonitor.GetValueOrDefault(monitorId, true);
        Refresh();
    }

    public void Cycle(int delta)
    {
        if (MonitorUnderPointer() is not { } monitorId
            || !_model.Monitors.TryGetValue(monitorId, out var state)
            || state.Strip.Count == 0)
        {
            return;
        }

        if (_cycle is null || _cycleMonitorId != monitorId)
        {
            _cycle = new CycleCursor(state.Strip);
            _cycleMonitorId = monitorId;
        }

        if (_cycle.Advance(delta, id => _model.Windows.ContainsKey(id)) is not { } window)
        {
            return;
        }

        _cycleReset.Stop();
        _cycleReset.Start();
        ActivateWindow(window);
    }

    public void ShowDesktop()
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Shell.Application is not available.");
            shell = Activator.CreateInstance(type);
            type.InvokeMember("ToggleDesktop", BindingFlags.InvokeMethod, null, shell, null);
        }
        catch (Exception exception)
        {
            StatusMessage?.Invoke($"Could not show the desktop: {exception.Message}");
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Raised -= OnWindowEvent;
        _watcher.Dispose();
        _debounce.Stop();
        _debounce.Dispose();
        _cycleReset.Stop();
        _cycleReset.Dispose();
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
        _render.Dispose();
    }

    private void OnWindowEvent(WindowEventKind kind, nint hwnd)
    {
        if (kind == WindowEventKind.DesktopSwitch)
        {
            _rebuildPending = true;
        }

        // Throttle instead of restarting so a steady event stream cannot starve the timer.
        if (!_debounce.Enabled)
        {
            _debounce.Start();
        }
    }

    private void OnDebounceTick()
    {
        _debounce.Stop();
        if (_rebuildPending)
        {
            _rebuildPending = false;
            Rebuild();
            return;
        }

        Reconcile();
    }

    private void OnCycleResetTick()
    {
        _cycleReset.Stop();
        _cycle = null;
        _cycleMonitorId = null;
    }

    private void Rebuild()
    {
        _lastAspect.Clear();
        _model.Reset(Enumerate(), _source.GetForegroundWindow());
        Refresh();
    }

    private void Reconcile()
    {
        var snapshots = Enumerate();
        var present = snapshots.Select(snapshot => snapshot.Id).ToHashSet();
        foreach (var id in _model.Windows.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            _model.Remove(id);
            _lastAspect.Remove(id);
        }

        foreach (var snapshot in snapshots)
        {
            if (!_model.Windows.TryGetValue(snapshot.Id, out var existing))
            {
                _model.Add(snapshot);
                continue;
            }

            var wasMinimized = existing.IsMinimized;
            _model.Update(snapshot);
            if (snapshot.IsMinimized != wasMinimized)
            {
                if (snapshot.IsMinimized)
                {
                    _model.Minimize(snapshot.Id);
                }
                else
                {
                    _model.Restore(snapshot.Id);
                }
            }
        }

        if (_source.GetForegroundWindow() is { } foreground && _model.Windows.ContainsKey(foreground))
        {
            _model.SetForeground(foreground);
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_disposed || _overlays.Count == 0)
        {
            return;
        }

        var states = _model.Monitors;
        var maxItems = Math.Max(1, _settings.MaxVisibleStripItems);
        foreach (var monitor in _monitors)
        {
            if (!_overlays.TryGetValue(monitor.Id, out var overlay))
            {
                continue;
            }

            var dpi = _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
            var toDip = dpi > 0 ? 96.0 / dpi : 1.0;
            var state = states.GetValueOrDefault(monitor.Id);
            var strip = state?.Strip ?? Array.Empty<WindowId>();
            var first = Math.Clamp(
                _firstVisibleIndex.GetValueOrDefault(monitor.Id),
                0,
                Math.Max(0, strip.Count - 1));
            _firstVisibleIndex[monitor.Id] = first;

            var items = new List<StripItem>(Math.Min(maxItems, strip.Count));
            foreach (var id in strip.Skip(first).Take(maxItems))
            {
                if (!_model.Windows.TryGetValue(id, out var snapshot))
                {
                    continue;
                }

                var boundsAspect = snapshot.Bounds.Height > 0
                    ? (double)snapshot.Bounds.Width / snapshot.Bounds.Height
                    : 4.0 / 3.0;
                // A minimized window reports its icon-rail rect; keep its last real
                // aspect so the tile keeps the last captured frame.
                var aspect = snapshot.IsMinimized
                    ? _lastAspect.GetValueOrDefault(id, boundsAspect)
                    : boundsAspect;
                if (!snapshot.IsMinimized)
                {
                    _lastAspect[id] = boundsAspect;
                }

                items.Add(new StripItem(id, aspect));
            }

            var options = new StripLayoutOptions(
                monitor.Bounds.Width * toDip,
                monitor.Bounds.Height * toDip,
                (monitor.WorkArea.Top - monitor.Bounds.Top) * toDip,
                monitor.WorkArea.Height * toDip,
                _settings.StripEdge);
            var layout = StripLayoutCalculator.Calculate(items, options);
            var tiles = new List<TileVisual>(layout.Tiles.Count);
            foreach (var placement in layout.Tiles)
            {
                if (!_model.Windows.TryGetValue(placement.Window, out var snapshot))
                {
                    continue;
                }

                tiles.Add(new TileVisual(
                    placement.Window,
                    placement.Bounds,
                    TileCornerRadiusDip,
                    GetIcon(snapshot),
                    snapshot.IsMinimized,
                    (nint)placement.Window.Value));
            }

            var visible = _settings.Enabled
                && _visibleByMonitor.GetValueOrDefault(monitor.Id, true);
            overlay.SetTiles(tiles);
            overlay.SetVisible(visible);
            _render.Post(new RenderCommand.UpdateOverlay(monitor.Id, tiles, visible));
        }
    }

    private void ActivateWindow(WindowId window)
    {
        if (!_model.Windows.TryGetValue(window, out var snapshot) || !_model.Activate(window))
        {
            Reconcile();
            StatusMessage?.Invoke("Windows refused to activate the window.");
            return;
        }

        if (_model.Mode == WindowMode.AllAtOnce
            && _model.Monitors.TryGetValue(snapshot.MonitorId, out var state))
        {
            foreach (var id in state.Stage)
            {
                WindowActivation.Restore(id);
            }
        }

        var activated = WindowActivation.Activate(window);
        Refresh();
        if (!activated)
        {
            StatusMessage?.Invoke("Windows refused to activate the window.");
            Reconcile();
        }
    }

    private void Scroll(string monitorId, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var count = _model.Monitors.TryGetValue(monitorId, out var state)
            ? state.Strip.Count
            : 0;
        var first = _firstVisibleIndex.GetValueOrDefault(monitorId) + Math.Sign(delta);
        _firstVisibleIndex[monitorId] = Math.Clamp(first, 0, Math.Max(0, count - 1));
        Refresh();
    }

    private void OnDpiChanged(string monitorId, uint dpi)
    {
        _dpiByMonitor[monitorId] = dpi;
        if (_overlays.TryGetValue(monitorId, out var overlay))
        {
            _render.Post(new RenderCommand.CreateOverlay(monitorId, overlay.Handle, dpi));
        }

        Refresh();
    }

    private IReadOnlyList<WindowSnapshot> Enumerate() =>
        _source.Enumerate(
            _monitors,
            new HashSet<string>(_settings.ExcludedAppIds, StringComparer.OrdinalIgnoreCase));

    private string? MonitorUnderPointer()
    {
        var position = Cursor.Position;
        if (MonitorTopology.IdAt(_monitors, position.X, position.Y) is { } monitorId)
        {
            return monitorId;
        }

        if (_source.GetForegroundWindow() is { } foreground
            && _model.Windows.TryGetValue(foreground, out var snapshot))
        {
            return snapshot.MonitorId;
        }

        return _monitors.Count > 0 ? _monitors[0].Id : null;
    }

    private IconBitmap GetIcon(WindowSnapshot snapshot)
    {
        if (!_icons.TryGetValue(snapshot.AppId, out var icon))
        {
            icon = IconLoader.Load(snapshot.ExecutablePath);
            _icons[snapshot.AppId] = icon;
        }

        return icon ?? _fallbackIcon;
    }
}
