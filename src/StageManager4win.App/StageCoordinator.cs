using System.Diagnostics;
using Microsoft.Win32;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using StageManager4win.Input;
using StageManager4win.Overlay;
using StageManager4win.Rendering;
using StageManager4win.Windows;
using Windows.Win32.Foundation;

namespace StageManager4win;

internal sealed class StageCoordinator : IDisposable
{
    private const double TileCornerRadiusDip = 12;
    private const double ScrollStepDip = 64;
    private const uint FallbackTileColor = 0xFF3B82F6;

    private readonly SettingsStore _store;
    private readonly DisplayManager _display = new();
    private readonly WindowSource _source = new();
    private readonly WindowEventWatcher _watcher = new();
    private RenderThread _render;
    private readonly StageModel _model;
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _visibleByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StripHitArea> _stripAreas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _scrollOffset = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _scrollTarget = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _maxScroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _dpiByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IconBitmap?> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<WindowId, double> _lastAspect = new();
    private readonly HashSet<WindowId> _hiddenByApp = new();
    private readonly IconBitmap _fallbackIcon = IconBitmap.Solid(1, 1, FallbackTileColor);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _cycleReset = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _workspaceSave = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _hideStrip = new() { Interval = 1500 };
    private readonly System.Windows.Forms.Timer _scrollAnim = new() { Interval = 15 };
    private readonly WorkspaceStateStore _workspace;
    private readonly AutoHideController _autoHide;
    private readonly SynchronizationContext _ui;
    private DesktopClickWatcher? _desktopClick;
    private StripWheelWatcher? _stripWheel;
    private AppSettings _settings;
    private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
    private CycleCursor? _cycle;
    private MovingWindow? _moving;
    private string? _cycleMonitorId;
    private bool _rebuildPending;
    private bool _started;
    private bool _disposed;
    private bool _sessionPaused;
    private bool _fullscreenPaused;
    private bool _desktopShown;
    private Dictionary<string, MonitorState>? _desktopStage;
    private WindowId? _desktopForeground;
    private long _stripHideReadyAt;

    public StageCoordinator(SettingsStore settingsStore)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _store = settingsStore;
        _settings = _store.Load();
        _workspace = new WorkspaceStateStore(
            Path.GetDirectoryName(settingsStore.FilePath) ?? Environment.CurrentDirectory);
        _autoHide = new AutoHideController(_display, GetStripArea, SetAutoHideVisible);
        _model = new StageModel(_settings.WindowMode);
        _render = new RenderThread();
        _render.DeviceLost += OnDeviceLost;
        _debounce.Tick += (_, _) => OnDebounceTick();
        _cycleReset.Tick += (_, _) => OnCycleResetTick();
        _workspaceSave.Tick += (_, _) => OnWorkspaceSaveTick();
        _hideStrip.Tick += (_, _) => OnHideStripTick();
        _scrollAnim.Tick += (_, _) => OnScrollTick();
    }

    public event Action<string>? StatusMessage;

    public IReadOnlyList<MonitorDescriptor> Monitors => _monitors;

    private bool Suspended => _sessionPaused || _fullscreenPaused;

    private IReadOnlyList<MonitorDescriptor> ManagedMonitors(AppSettings settings) =>
        _monitors.Where(monitor => settings.IsManaged(monitor.Id)).ToArray();

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _monitors = _display.GetMonitors();
        foreach (var monitor in ManagedMonitors(_settings))
        {
            CreateOverlay(monitor);
        }

        _autoHide.RefreshMonitors();
        var foreground = _source.GetForegroundWindow();
        _model.Reset(Enumerate(), foreground);
        LoadWorkspace();
        _fullscreenPaused = IsFullscreen(foreground);
        StartRenderThread();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _watcher.Raised += OnWindowEvent;
        _watcher.Start();
        _desktopClick = new DesktopClickWatcher(
            IsOverStrip,
            point => MonitorTopology.IdAt(_monitors, point.X, point.Y) is { } id
                && _settings.IsManaged(id));
        _desktopClick.WallpaperClicked += ShowDesktop;
        _stripWheel = new StripWheelWatcher(OnStripWheel);
        UpdateDesktopClickWatcher();
        Refresh();
        // Let the first captures land before minimizing the strip, or every
        // off-stage tile would show the icon fallback instead of its last frame.
        _stripHideReadyAt = Environment.TickCount64 + 1200;
        _hideStrip.Start();
    }

    private void CreateOverlay(MonitorDescriptor monitor)
    {
        var overlay = new OverlayWindow(monitor);
        var monitorId = monitor.Id;
        overlay.TileClicked += ActivateWindow;
        overlay.TileDropped += AddWindowToStage;
        overlay.TileInvoked += window => ActivateFromUia(monitorId, window);
        overlay.HoverChanged += window =>
            _render.Post(new RenderCommand.SetOverlayHover(monitorId, window));
        overlay.ScrollRequested += delta => Scroll(monitorId, delta);
        overlay.DragStarted += () => _autoHide.Suppress(true);
        overlay.DragMoved += (window, dx, dy) =>
            _render.Post(new RenderCommand.UpdateDragGhost(monitorId, window, dx, dy));
        overlay.DragEnded += () =>
        {
            _autoHide.Suppress(false);
            _render.Post(new RenderCommand.UpdateDragGhost(monitorId, null, 0, 0));
        };
        overlay.DpiChanged += dpi => OnDpiChanged(monitorId, dpi);
        overlay.Show();
        _overlays[monitorId] = overlay;
        _dpiByMonitor[monitorId] = monitor.Dpi;
    }

    private void StartRenderThread()
    {
        _render.Start();
        _render.Post(new RenderCommand.SetPreviewFps(_settings.PreviewFps));
        if (Suspended)
        {
            _render.Post(new RenderCommand.SetCapturePaused(true));
        }

        foreach (var monitor in _monitors)
        {
            if (_overlays.TryGetValue(monitor.Id, out var overlay))
            {
                _render.Post(new RenderCommand.CreateOverlay(
                    monitor.Id,
                    overlay.Handle,
                    _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi)));
            }
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        var previous = _settings;
        var next = settings.Normalize();
        if (_monitors.Any(monitor => previous.AutoHideFor(monitor.Id) != next.AutoHideFor(monitor.Id)))
        {
            _visibleByMonitor.Clear();
        }

        var previousManaged = ManagedMonitors(previous)
            .Select(monitor => monitor.Id)
            .ToHashSet(StringComparer.Ordinal);
        var nextManaged = ManagedMonitors(next)
            .Select(monitor => monitor.Id)
            .ToHashSet(StringComparer.Ordinal);
        var managedChanged = !previousManaged.SetEquals(nextManaged);
        if (managedChanged)
        {
            ExcludeMonitors(previousManaged.Except(nextManaged));
        }

        _settings = next;
        UpdateDesktopClickWatcher();
        _model.SetMode(_settings.WindowMode);
        _render.Post(new RenderCommand.SetPreviewFps(_settings.PreviewFps));

        if (managedChanged)
        {
            RecreateOverlays(loadWorkspace: false);
        }
        else
        {
            Reconcile();
        }
    }

    private void ExcludeMonitors(IEnumerable<string> monitorIds)
    {
        var excluded = monitorIds.ToHashSet(StringComparer.Ordinal);
        foreach (var monitorId in excluded)
        {
            var windows = _model.Windows.Values
                .Where(snapshot => snapshot.MonitorId == monitorId)
                .ToArray();
            foreach (var id in windows
                .Where(snapshot => _hiddenByApp.Contains(snapshot.Id))
                .Select(snapshot => snapshot.Id))
            {
                WindowActivation.RestoreNoActivate(id);
                _hiddenByApp.Remove(id);
            }

            foreach (var snapshot in windows)
            {
                _model.Remove(snapshot.Id);
                _lastAspect.Remove(snapshot.Id);
            }
        }

        if (_desktopShown && _desktopStage is not null)
        {
            foreach (var monitorId in excluded)
            {
                _desktopStage.Remove(monitorId);
            }

            if (_desktopStage.Count == 0)
            {
                _desktopShown = false;
                _desktopForeground = null;
            }
            else
            {
                _desktopForeground = _desktopStage.Values
                    .Select(state => state.Foreground)
                    .FirstOrDefault(id => id is not null);
            }
        }

        foreach (var monitorId in excluded)
        {
            _stripAreas.Remove(monitorId);
            _visibleByMonitor.Remove(monitorId);
            _scrollOffset.Remove(monitorId);
            _scrollTarget.Remove(monitorId);
            _maxScroll.Remove(monitorId);
            _dpiByMonitor.Remove(monitorId);
        }

        if (_cycleMonitorId is { } cycleMonitor && excluded.Contains(cycleMonitor))
        {
            _cycle = null;
            _cycleMonitorId = null;
            _cycleReset.Stop();
        }

        ScheduleWorkspaceSave();
    }

    private void OnHideStripTick()
    {
        _hideStrip.Stop();
        if (Environment.TickCount64 < _stripHideReadyAt)
        {
            _hideStrip.Start();
            return;
        }

        HideOffStageWindows();
        Refresh();
    }

    // The strip is the off-stage set; minimize it so off-stage windows do not
    // sit behind the stage. Minimized tiles keep their last captured frame.
    private void HideOffStageWindows()
    {
        if (!_started || !_settings.Enabled || Suspended
            || Environment.TickCount64 < _stripHideReadyAt)
        {
            return;
        }

        var foreground = _source.GetForegroundWindow();
        foreach (var state in _model.Monitors.Values)
        {
            foreach (var id in state.Strip)
            {
                if (id == foreground
                    || !_model.Windows.TryGetValue(id, out var snapshot)
                    || snapshot.IsMinimized)
                {
                    continue;
                }

                if (snapshot.Bounds.Height > 0)
                {
                    _lastAspect[id] = (double)snapshot.Bounds.Width / snapshot.Bounds.Height;
                }

                _model.Minimize(id);
                WindowActivation.Minimize(id);
                _hiddenByApp.Add(id);
            }
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

    // The shell's ToggleDesktop is a no-op on some Windows 11 installs, so the
    // stage is hidden and restored here instead.
    public void ShowDesktop()
    {
        if (_desktopShown)
        {
            _desktopShown = false;
            var saved = _desktopStage;
            _desktopStage = null;
            if (saved is not null)
            {
                foreach (var state in saved.Values.Where(state => _settings.IsManaged(state.MonitorId)))
                {
                    var stage = state.Stage.Where(_model.Windows.ContainsKey).ToList();
                    _model.Restore(state with { Stage = stage });
                    foreach (var id in stage)
                    {
                        _model.Restore(id);
                        WindowActivation.Restore(id);
                        _hiddenByApp.Remove(id);
                    }
                }
            }

            if (_desktopForeground is { } foreground && _model.Windows.ContainsKey(foreground))
            {
                WindowActivation.Activate(foreground);
            }

            _desktopForeground = null;
            Refresh();
            return;
        }

        var hidden = new Dictionary<string, MonitorState>(StringComparer.Ordinal);
        foreach (var state in _model.Monitors.Values.Where(state => _settings.IsManaged(state.MonitorId)))
        {
            hidden[state.MonitorId] = state;
        }

        if (hidden.Values.All(state => state.Stage.Count == 0))
        {
            return;
        }

        _desktopShown = true;
        _desktopStage = hidden;
        _desktopForeground = hidden.Values
            .Select(state => state.Foreground)
            .FirstOrDefault(id => id is not null);
        foreach (var state in hidden.Values)
        {
            foreach (var id in state.Stage)
            {
                _model.Minimize(id);
                WindowActivation.Minimize(id);
                _hiddenByApp.Add(id);
            }
        }

        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _autoHide.Dispose();
        _watcher.Raised -= OnWindowEvent;
        _watcher.Dispose();
        if (_desktopClick is not null)
        {
            _desktopClick.WallpaperClicked -= ShowDesktop;
            _desktopClick.Dispose();
        }

        _desktopClick = null;
        _stripWheel?.Dispose();
        _stripWheel = null;
        _debounce.Stop();
        _debounce.Dispose();
        _cycleReset.Stop();
        _cycleReset.Dispose();
        _workspaceSave.Stop();
        _workspaceSave.Dispose();
        _hideStrip.Stop();
        _hideStrip.Dispose();
        _scrollAnim.Stop();
        _scrollAnim.Dispose();
        // Don't leave the windows this app minimized hidden after it exits.
        foreach (var id in _hiddenByApp)
        {
            if (_model.Windows.TryGetValue(id, out var snapshot) && snapshot.IsMinimized)
            {
                WindowActivation.RestoreNoActivate(id);
            }
        }

        _hiddenByApp.Clear();
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
        _render.Dispose();
    }

    private void OnWindowEvent(WindowEventKind kind, nint hwnd)
    {
        if (_moving is { } moving
            && kind == WindowEventKind.Changed
            && hwnd == (nint)moving.Window.Value
            && !_source.IsVisible(hwnd))
        {
            _moving = null;
            _autoHide.Suppress(false);
        }

        switch (kind)
        {
            case WindowEventKind.MoveSizeStart:
                OnMoveSizeStart(hwnd);
                return;
            case WindowEventKind.MoveSizeEnd:
                OnMoveSizeEnd(hwnd);
                return;
        }

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

    private void OnMoveSizeStart(nint hwnd)
    {
        var id = new WindowId(hwnd);
        var known = _model.Windows.TryGetValue(id, out var snapshot);
        var staged = known
            && _model.Monitors.TryGetValue(snapshot!.MonitorId, out var state)
            && state.Stage.Contains(id);
        if (!_settings.Enabled || Suspended || !known || !staged)
        {
            return;
        }

        var bounds = _source.GetBounds(hwnd);
        _moving = new MovingWindow(id, snapshot!.MonitorId, bounds.Width, bounds.Height);
        _autoHide.Suppress(true);
    }

    private void OnMoveSizeEnd(nint hwnd)
    {
        var moving = _moving;
        if (moving is null || moving.Window != new WindowId(hwnd))
        {
            return;
        }

        _moving = null;
        _autoHide.Suppress(false);
        if (!_settings.Enabled || Suspended)
        {
            return;
        }

        if (!_model.Windows.TryGetValue(moving.Window, out var snapshot))
        {
            return;
        }

        var monitor = _monitors.FirstOrDefault(candidate => candidate.Id == moving.MonitorId);
        if (monitor is null || !_stripAreas.TryGetValue(monitor.Id, out var area))
        {
            return;
        }

        var bounds = _source.GetBounds(hwnd);
        var dpi = _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
        var scale = dpi > 0 ? 96.0 / dpi : 1.0;
        var cursor = Cursor.Position;
        var x = (cursor.X - monitor.Bounds.Left) * scale;
        var y = (cursor.Y - monitor.Bounds.Top) * scale;
        var onTile = area.TilesDip.Any(tile => !DragGesture.Outside(tile, x, y));
        var remove = StageRemoval.ShouldRemove(
            wasOnStage: true,
            sameMonitor: snapshot.MonitorId == moving.MonitorId,
            startWidth: moving.Width,
            startHeight: moving.Height,
            endWidth: bounds.Width,
            endHeight: bounds.Height,
            cursorOnTile: onTile);
        if (!remove)
        {
            return;
        }

        _model.RemoveFromStage(moving.Window);
        WindowActivation.Minimize(moving.Window);
        _hiddenByApp.Add(moving.Window);
        Refresh();
        ScheduleWorkspaceSave();
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
        LoadWorkspace();
        ScheduleWorkspaceSave();
        Refresh();
        _stripHideReadyAt = Environment.TickCount64 + 1200;
        _hideStrip.Stop();
        _hideStrip.Start();
    }

    private void LoadWorkspace()
    {
        var saved = _workspace.Load();
        foreach (var monitor in saved.Monitors)
        {
            if (!_model.Monitors.TryGetValue(monitor.MonitorId, out var state))
            {
                continue;
            }

            var used = new HashSet<WindowId>();
            var ordered = new List<WindowId>();
            foreach (var identity in monitor.StripOrder)
            {
                foreach (var id in state.Strip)
                {
                    if (used.Contains(id) || !_model.Windows.TryGetValue(id, out var snapshot))
                    {
                        continue;
                    }

                    if (snapshot.AppId == identity.AppId && snapshot.Title == identity.Title)
                    {
                        ordered.Add(id);
                        used.Add(id);
                        break;
                    }
                }
            }

            _model.ReorderStrip(monitor.MonitorId, ordered);

            var current = _model.Monitors[monitor.MonitorId];
            if (WorkspaceRestore.Plan(current, monitor, _model.Windows) is { } restored)
            {
                _model.Restore(restored);
            }
            else if (monitor.Foreground is { } foreground)
            {
                foreach (var snapshot in _model.Windows.Values)
                {
                    if (snapshot.MonitorId == monitor.MonitorId
                        && snapshot.AppId == foreground.AppId
                        && snapshot.Title == foreground.Title)
                    {
                        _model.SetForeground(snapshot.Id);
                        break;
                    }
                }
            }
        }
    }

    private void ScheduleWorkspaceSave()
    {
        // Throttle, not restart: a steady event stream must not starve the save.
        if (!_workspaceSave.Enabled)
        {
            _workspaceSave.Start();
        }
    }

    private void OnWorkspaceSaveTick()
    {
        _workspaceSave.Stop();
        if (_disposed)
        {
            return;
        }

        var monitors = new List<MonitorWorkspace>();
        foreach (var (monitorId, state) in _model.Monitors)
        {
            var strip = new List<WindowIdentity>(state.Strip.Count);
            foreach (var id in state.Strip)
            {
                if (_model.Windows.TryGetValue(id, out var snapshot))
                {
                    strip.Add(new WindowIdentity(snapshot.AppId, snapshot.Title));
                }
            }

            var stage = new List<WindowIdentity>(state.Stage.Count);
            foreach (var id in state.Stage)
            {
                if (_model.Windows.TryGetValue(id, out var snapshot))
                {
                    stage.Add(new WindowIdentity(snapshot.AppId, snapshot.Title));
                }
            }

            WindowIdentity? foreground = null;
            if (state.Foreground is { } foregroundId
                && _model.Windows.TryGetValue(foregroundId, out var foregroundSnapshot))
            {
                foreground = new WindowIdentity(foregroundSnapshot.AppId, foregroundSnapshot.Title);
            }

            monitors.Add(new MonitorWorkspace(monitorId, strip, foreground, stage));
        }

        try
        {
            _workspace.Save(new WorkspaceState(monitors));
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            FileLogger.Default.Write(
                "workspace",
                $"save hr=0x{exception.HResult:X8} {exception.GetType().Name}");
        }
    }

    private void Reconcile()
    {
        var snapshots = Enumerate();
        var present = snapshots.Select(snapshot => snapshot.Id).ToHashSet();
        foreach (var id in _model.Windows.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            _model.Remove(id);
            _lastAspect.Remove(id);
            if (_hiddenByApp.Remove(id))
            {
                WindowActivation.RestoreNoActivate(id);
            }
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
                    _hiddenByApp.Remove(snapshot.Id);
                }
            }
        }

        var foreground = _source.GetForegroundWindow();
        // A window being minimized can still report as foreground; don't stage it again.
        if (foreground is { } window
            && _model.Windows.TryGetValue(window, out var foregroundSnapshot)
            && !foregroundSnapshot.IsMinimized)
        {
            _model.SetForeground(window);
        }

        HideOffStageWindows();

        var fullscreen = IsFullscreen(foreground);
        if (fullscreen != _fullscreenPaused)
        {
            _fullscreenPaused = fullscreen;
            UpdateDesktopClickWatcher();
            _render.Post(new RenderCommand.SetCapturePaused(Suspended));
        }

        ScheduleWorkspaceSave();
        Refresh();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var paused = e.Reason switch
        {
            SessionSwitchReason.SessionLock
                or SessionSwitchReason.ConsoleDisconnect
                or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock
                or SessionSwitchReason.ConsoleConnect
                or SessionSwitchReason.RemoteConnect => false,
            _ => (bool?)null
        };
        if (paused is not { } value)
        {
            return;
        }

        RunOnUiThread(() => SetSessionPaused(value));
    }

    private void SetSessionPaused(bool paused)
    {
        if (_disposed || _sessionPaused == paused)
        {
            return;
        }

        _sessionPaused = paused;
        UpdateDesktopClickWatcher();
        _render.Post(new RenderCommand.SetCapturePaused(Suspended));
        if (paused)
        {
            Refresh();
        }
        else
        {
            Reconcile();
        }
    }

    private void OnDeviceLost() => RunOnUiThread(RecoverRenderThread);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        RunOnUiThread(() => RecreateOverlays());

    private void RecreateOverlays(bool loadWorkspace = true)
    {
        if (_disposed)
        {
            return;
        }

        _render.Dispose();
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
        _dpiByMonitor.Clear();
        _scrollAnim.Stop();
        _scrollOffset.Clear();
        _scrollTarget.Clear();
        _maxScroll.Clear();
        _visibleByMonitor.Clear();
        _stripAreas.Clear();
        _monitors = _display.GetMonitors();
        foreach (var monitor in ManagedMonitors(_settings))
        {
            CreateOverlay(monitor);
        }

        _autoHide.RefreshMonitors();
        _render = new RenderThread();
        _render.DeviceLost += OnDeviceLost;
        _fullscreenPaused = IsFullscreen(_source.GetForegroundWindow());
        StartRenderThread();
        if (loadWorkspace)
        {
            Rebuild();
            return;
        }

        _stripHideReadyAt = Environment.TickCount64 + 1200;
        Reconcile();
        _hideStrip.Stop();
        _hideStrip.Start();
    }

    private StripHitArea? GetStripArea(string monitorId)
    {
        if (!_settings.Enabled
            || Suspended
            || !_settings.IsManaged(monitorId)
            || !_settings.AutoHideFor(monitorId))
        {
            return null;
        }

        return _stripAreas.GetValueOrDefault(monitorId);
    }

    private bool IsOverStrip(Point point) => MonitorOverStrip(point) is not null;

    private void OnStripWheel(Point point, int delta)
    {
        if (_disposed || !_settings.Enabled || Suspended)
        {
            return;
        }

        if (MonitorOverStrip(point) is { } monitorId
            && _maxScroll.GetValueOrDefault(monitorId) > 0)
        {
            Scroll(monitorId, delta);
        }
    }

    private string? MonitorOverStrip(Point point)
    {
        foreach (var monitor in _monitors)
        {
            if (!_stripAreas.TryGetValue(monitor.Id, out var area))
            {
                continue;
            }

            var scale = 96.0 / _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
            var x = (point.X - monitor.Bounds.Left) * scale;
            var y = (point.Y - monitor.Bounds.Top) * scale;
            foreach (var tile in area.TilesDip)
            {
                if (!DragGesture.Outside(TileStack.HitBounds(tile), x, y))
                {
                    return monitor.Id;
                }
            }
        }

        return null;
    }

    private void UpdateDesktopClickWatcher()
    {
        if (_desktopClick is null)
        {
            return;
        }

        if (_settings.Enabled && !Suspended && !_disposed)
        {
            _desktopClick.Start();
            _stripWheel?.Start();
        }
        else
        {
            _desktopClick.Stop();
            _stripWheel?.Stop();
        }
    }

    private void SetAutoHideVisible(string monitorId, bool visible)
    {
        if (_disposed || _visibleByMonitor.GetValueOrDefault(monitorId, true) == visible)
        {
            return;
        }

        _visibleByMonitor[monitorId] = visible;
        Refresh();
    }

    private void RecoverRenderThread()
    {
        if (_disposed)
        {
            return;
        }

        _render.Dispose();
        _render = new RenderThread();
        _render.DeviceLost += OnDeviceLost;
        StartRenderThread();
        Reconcile();
        StatusMessage?.Invoke("The graphics device was reset; live previews restarted.");
    }

    private void RunOnUiThread(Action action)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ui.Post(_ =>
            {
                if (!_disposed)
                {
                    action();
                }
            }, null);
        }
        catch (Exception)
        {
            // The UI thread can be gone while the app is shutting down.
        }
    }

    private bool IsFullscreen(WindowId? window)
    {
        if (window is not { } value || !_source.IsFullscreen((nint)value.Value))
        {
            return false;
        }

        var monitorId = MonitorTopology.IdForBounds(
            _monitors,
            _source.GetBounds((nint)value.Value));
        return monitorId is not null && _settings.IsManaged(monitorId);
    }

    private void Refresh()
    {
        if (_disposed || _overlays.Count == 0)
        {
            return;
        }

        var states = _model.Monitors;
        var highContrast = SystemInformation.HighContrast;
        var borderBgra = highContrast ? ToBgra(SystemColors.WindowText) : 0;
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
            var groups = StripGrouping.Group(strip, _model.Windows);
            var items = new List<StripItem>(groups.Count);
            var emitted = new List<(StripGroup Group, double Aspect)>(groups.Count);
            foreach (var group in groups)
            {
                if (!_model.Windows.TryGetValue(group.Front, out var snapshot))
                {
                    continue;
                }

                var boundsAspect = snapshot.Bounds.Height > 0
                    ? (double)snapshot.Bounds.Width / snapshot.Bounds.Height
                    : 4.0 / 3.0;
                // A minimized window reports its icon-rail rect. Remember the last real
                // aspect, and fall back to the restore rect after an app restart when
                // that memory is gone but the windows are still minimized.
                double aspect;
                if (snapshot.IsMinimized)
                {
                    aspect = _lastAspect.TryGetValue(group.Front, out var remembered)
                        ? remembered
                        : RestoreAspect(group.Front);
                    _lastAspect[group.Front] = aspect;
                }
                else
                {
                    aspect = boundsAspect;
                    _lastAspect[group.Front] = boundsAspect;
                }

                items.Add(new StripItem(group.Front, aspect));
                emitted.Add((group, aspect));
            }

            var options = new StripLayoutOptions(
                monitor.Bounds.Width * toDip,
                monitor.Bounds.Height * toDip,
                (monitor.WorkArea.Top - monitor.Bounds.Top) * toDip,
                monitor.WorkArea.Height * toDip,
                _settings.EdgeFor(monitor.Id));
            var layout = StripLayoutCalculator.Calculate(
                items,
                options,
                _scrollOffset.GetValueOrDefault(monitor.Id));
            _maxScroll[monitor.Id] = layout.MaxScroll;
            _scrollOffset[monitor.Id] = Math.Clamp(
                _scrollOffset.GetValueOrDefault(monitor.Id), 0, layout.MaxScroll);
            _scrollTarget[monitor.Id] = Math.Clamp(
                _scrollTarget.GetValueOrDefault(monitor.Id), 0, layout.MaxScroll);
            overlay.SetStripBounds(layout.StripBounds);
            var monitorWidth = monitor.Bounds.Width * toDip;
            var workTop = (monitor.WorkArea.Top - monitor.Bounds.Top) * toDip;
            var workHeight = monitor.WorkArea.Height * toDip;
            var edgeX = _settings.EdgeFor(monitor.Id) == StripEdge.Left
                ? 0
                : Math.Max(0, monitorWidth - AutoHideController.RevealEdgeDip);
            var edgeZone = new RectD(edgeX, workTop, AutoHideController.RevealEdgeDip, workHeight);
            var tileBounds = layout.Tiles.Select(placement => placement.Bounds).ToArray();
            if (layout.Tiles.Count == 0)
            {
                _stripAreas[monitor.Id] = new StripHitArea(
                    layout.StripBounds,
                    edgeZone,
                    layout.StripBounds,
                    Array.Empty<RectD>());
            }
            else
            {
                var firstBounds = layout.Tiles[0].Bounds;
                var left = firstBounds.X;
                var top = firstBounds.Y;
                var right = firstBounds.X + firstBounds.Width;
                var bottom = firstBounds.Y + firstBounds.Height;
                foreach (var placement in layout.Tiles)
                {
                    left = Math.Min(left, placement.Bounds.X);
                    top = Math.Min(top, placement.Bounds.Y);
                    right = Math.Max(right, placement.Bounds.X + placement.Bounds.Width);
                    bottom = Math.Max(bottom, placement.Bounds.Y + placement.Bounds.Height);
                }

                _stripAreas[monitor.Id] = new StripHitArea(
                    new RectD(left, top, right - left, bottom - top),
                    edgeZone,
                    layout.StripBounds,
                    tileBounds);
            }

            var tiles = new List<TileVisual>(layout.Tiles.Count);
            var groupsByFront = emitted.ToDictionary(item => item.Group.Front);
            foreach (var placement in layout.Tiles)
            {
                if (!groupsByFront.TryGetValue(placement.Window, out var item))
                {
                    continue;
                }

                var group = item.Group;
                var count = group.Windows.Count;
                // Back-to-front for z-order, while the first window keeps StackIndex 0 (the front card).
                for (var index = count - 1; index >= 0; index--)
                {
                    var id = group.Windows[index];
                    if (!_model.Windows.TryGetValue(id, out var snapshot))
                    {
                        continue;
                    }

                    tiles.Add(new TileVisual(
                        id,
                        placement.Bounds,
                        TileCornerRadiusDip,
                        GetIcon(snapshot),
                        snapshot.IsMinimized,
                        (nint)id.Value,
                        snapshot.AppId,
                        snapshot.AppName,
                        snapshot.Title,
                        highContrast,
                        borderBgra,
                        index,
                        count,
                        group.Front));
                }
            }

            var autoHideVisible = !_settings.AutoHideFor(monitor.Id)
                || _visibleByMonitor.GetValueOrDefault(monitor.Id, true);
            var visible = _settings.Enabled
                && !Suspended
                && !_desktopShown
                && autoHideVisible;
            overlay.SetTiles(tiles);
            overlay.SetVisible(visible);
            _render.Post(new RenderCommand.UpdateOverlay(
                monitor.Id,
                tiles,
                visible,
                layout.StripBounds));
        }
    }

    private void ActivateFromUia(string monitorId, WindowId window)
    {
        if (_settings.AutoHideFor(monitorId)
            && _settings.Enabled
            && !Suspended
            && !_visibleByMonitor.GetValueOrDefault(monitorId, true))
        {
            _visibleByMonitor[monitorId] = true;
            Refresh();
        }

        ActivateWindow(window);
    }

    private void ActivateWindow(WindowId window)
    {
        if (!_model.Windows.TryGetValue(window, out var snapshot))
        {
            FileLogger.Default.Write(
                "activate",
                $"window={window.Value} app=? reason=model");
            Reconcile();
            StatusMessage?.Invoke("Windows refused to activate the window.");
            return;
        }

        _model.Monitors.TryGetValue(snapshot.MonitorId, out var previous);
        if (!_model.Activate(window))
        {
            FileLogger.Default.Write(
                "activate",
                $"window={window.Value} app={snapshot.AppId} reason=model");
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
                _hiddenByApp.Remove(id);
            }
        }

        var activated = WindowActivation.Activate(window);
        if (activated)
        {
            _hiddenByApp.Remove(window);
            HideOffStageWindows();
        }

        Refresh();
        if (!activated)
        {
            if (previous is not null)
            {
                _model.Restore(previous);
            }

            FileLogger.Default.Write(
                "activate",
                $"window={window.Value} app={snapshot.AppId} reason=foreground");
            StatusMessage?.Invoke("Windows refused to activate the window.");
            Reconcile();
        }
    }

    private void AddWindowToStage(WindowId window)
    {
        if (!_model.Windows.TryGetValue(window, out var snapshot))
        {
            Reconcile();
            return;
        }

        _model.Monitors.TryGetValue(snapshot.MonitorId, out var previous);
        if (!_model.AddToStage(window))
        {
            Reconcile();
            return;
        }

        if (_model.Monitors.TryGetValue(snapshot.MonitorId, out var state))
        {
            foreach (var id in state.Stage)
            {
                WindowActivation.Restore(id);
                _hiddenByApp.Remove(id);
            }
        }

        var activated = WindowActivation.Activate(window);
        if (activated)
        {
            _hiddenByApp.Remove(window);
            HideOffStageWindows();
        }

        Refresh();
        ScheduleWorkspaceSave();
        if (!activated)
        {
            if (previous is not null)
            {
                _model.Restore(previous);
            }

            FileLogger.Default.Write(
                "activate",
                $"window={window.Value} app={snapshot.AppId} reason=foreground");
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

        var max = _maxScroll.GetValueOrDefault(monitorId);
        if (max <= 0)
        {
            _scrollTarget[monitorId] = 0;
            return;
        }

        // A wheel notch is 120; touchpads send smaller deltas, so scroll by distance.
        var target = _scrollTarget.GetValueOrDefault(monitorId) - delta / 120.0 * ScrollStepDip;
        _scrollTarget[monitorId] = Math.Clamp(target, 0, max);
        if (!_scrollAnim.Enabled)
        {
            _scrollAnim.Start();
        }
    }

    private void OnScrollTick()
    {
        var moving = false;
        foreach (var monitorId in _scrollTarget.Keys.ToArray())
        {
            var target = _scrollTarget[monitorId];
            var current = _scrollOffset.GetValueOrDefault(monitorId);
            var difference = target - current;
            if (Math.Abs(difference) < 0.5)
            {
                _scrollOffset[monitorId] = target;
                continue;
            }

            _scrollOffset[monitorId] = current + difference * 0.35;
            moving = true;
        }

        Refresh();
        if (!moving)
        {
            _scrollAnim.Stop();
        }
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
                new HashSet<string>(_settings.ExcludedAppIds, StringComparer.OrdinalIgnoreCase))
            .Where(snapshot => _settings.IsManaged(snapshot.MonitorId))
            .ToArray();

    private string? MonitorUnderPointer()
    {
        var position = Cursor.Position;
        if (MonitorTopology.IdAt(_monitors, position.X, position.Y) is { } monitorId)
        {
            return _settings.IsManaged(monitorId) ? monitorId : null;
        }

        if (_source.GetForegroundWindow() is { } foreground
            && _model.Windows.TryGetValue(foreground, out var snapshot)
            && _settings.IsManaged(snapshot.MonitorId))
        {
            return snapshot.MonitorId;
        }

        return null;
    }

    private static uint ToBgra(Color color) =>
        (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));

    private double RestoreAspect(WindowId window)
    {
        var bounds = _source.GetRestoreBounds((nint)window.Value);
        return bounds.Height > 0
            ? (double)bounds.Width / bounds.Height
            : 4.0 / 3.0;
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

    private sealed record MovingWindow(WindowId Window, string MonitorId, int Width, int Height);
}
