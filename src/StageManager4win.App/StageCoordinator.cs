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
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win;

internal sealed class StageCoordinator : IDisposable
{
    private const double TileCornerRadiusDip = 12;
    private const double ScrollStepDip = 64;
    private const double ListingRowDip = 32;
    private const double ListingPaddingDip = 6;
    private const double ListingMaxWidthDip = 320;
    private const uint FallbackTileColor = 0xFF3B82F6;

    private readonly SettingsStore _store;
    private readonly DisplayManager _display = new();
    private readonly WindowSource _source = new();
    private readonly WindowEventWatcher _watcher = new();
    private RenderThread _render;
    private readonly StageModel _model;
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StripHitArea> _stripAreas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StripVisibility> _visibilityByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _scrollOffset = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _scrollTarget = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _maxScroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _dpiByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IconBitmap?> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<WindowId, double> _lastAspect = new();
    private readonly Dictionary<string, int> _listingScroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _listingMaxScroll = new(StringComparer.Ordinal);
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
    private string? _openListingSetId;
    private bool _rebuildPending;
    private bool _started;
    private bool _disposed;
    private bool _sessionPaused;
    private bool _fullscreenPaused;
    private bool _desktopShown;
    private int _suppressActivation;
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
        _autoHide = new AutoHideController(_display, GetStripArea, SetVisibility);
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
        overlay.TileClicked += key => ActivateWindow(key.Window);
        overlay.ShiftAddRequested += key => ShiftAddWindow(key.Window);
        overlay.TileDropped += key => AddWindowToStage(key);
        overlay.BadgeClicked += ToggleListing;
        overlay.ListingRowClicked += key => ActivateWindow(key.Window);
        overlay.ListingDismissed += CloseListing;
        overlay.HoverChanged += target =>
            _render.Post(new RenderCommand.SetOverlayHover(monitorId, target));
        overlay.ScrollRequested += delta => Scroll(monitorId, delta);
        overlay.ListingScrollRequested += ScrollListing;
        overlay.DragStarted += () => _autoHide.Suppress(true);
        overlay.DragMoved += (setId, dx, dy) =>
            _render.Post(new RenderCommand.UpdateDragGhost(monitorId, setId, dx, dy));
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
        _autoHide.RefreshMonitors();

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
                    .Select(state => state.ActiveSet?.Foreground.Window)
                    .FirstOrDefault(id => id is not null);
            }
        }

        foreach (var monitorId in excluded)
        {
            _stripAreas.Remove(monitorId);
            _visibilityByMonitor.Remove(monitorId);
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
            foreach (var set in state.StripSets)
            {
                foreach (var key in set.Members)
                {
                    if (key.Window == foreground
                        || !_model.Windows.TryGetValue(key.Window, out var snapshot)
                        || snapshot.IsMinimized)
                    {
                        continue;
                    }

                    if (snapshot.Bounds.Height > 0)
                    {
                        _lastAspect[key.Window] =
                            (double)snapshot.Bounds.Width / snapshot.Bounds.Height;
                    }

                    _model.Minimize(key.Window);
                    WindowActivation.Minimize(key.Window);
                    _hiddenByApp.Add(key.Window);
                }
            }
        }
    }

    public void Cycle(int delta)
    {
        if (MonitorUnderPointer() is not { } monitorId)
        {
            return;
        }

        var order = _model.CycleOrder(monitorId);
        if (order.Count == 0)
        {
            return;
        }

        if (_cycle is null || _cycleMonitorId != monitorId)
        {
            _cycle = new CycleCursor(order);
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
                    _model.Restore(state);
                    if (state.ActiveSet is not { } active)
                    {
                        continue;
                    }

                    foreach (var key in active.Members)
                    {
                        _model.Restore(key.Window);
                        WindowActivation.Restore(key.Window);
                        _hiddenByApp.Remove(key.Window);
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

        if (hidden.Values.All(state => state.ActiveSet is null))
        {
            return;
        }

        _desktopShown = true;
        _desktopStage = hidden;
        _desktopForeground = hidden.Values
            .Select(state => state.ActiveSet?.Foreground.Window)
            .FirstOrDefault(id => id is not null);
        foreach (var state in hidden.Values)
        {
            if (state.ActiveSet is not { } active)
            {
                continue;
            }

            foreach (var key in active.Members)
            {
                _model.Minimize(key.Window);
                WindowActivation.Minimize(key.Window);
                _hiddenByApp.Add(key.Window);
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
            && state.ActiveSet?.Contains(WindowKey.For(snapshot)) == true;
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
        var onTile = area.Placements.Any(tile => !DragGesture.Outside(tile.UnionBoundsDip, x, y));
        var remove = StageRemoval.ShouldRemove(
            wasOnStage: true,
            sameMonitor: snapshot.MonitorId == moving.MonitorId,
            startWidth: moving.Width,
            startHeight: moving.Height,
            endWidth: bounds.Width,
            endHeight: bounds.Height,
            cursorOnTile: onTile);
        if (remove)
        {
            _model.Minimize(moving.Window);
            WindowActivation.Minimize(moving.Window);
            _hiddenByApp.Add(moving.Window);
            Refresh();
            ScheduleWorkspaceSave();
            return;
        }

        if (snapshot.MonitorId != moving.MonitorId)
        {
            Reconcile();
            return;
        }

        CaptureGeometry(moving.Window);
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

            if (WorkspaceRestore.Plan(state, monitor, _model.Windows) is { } plan)
            {
                _model.ApplyWorkspace(
                    plan.MonitorId,
                    plan.Stage,
                    plan.Strip,
                    plan.Foreground);
                continue;
            }

            var candidates = Candidates(state);
            var used = new HashSet<WindowId>();
            var strip = new List<WindowId>();
            foreach (var identity in monitor.StripOrder)
            {
                if (MatchIdentity(identity, candidates, used) is { } id)
                {
                    strip.Add(id);
                    used.Add(id);
                }
            }

            WindowId? foreground = null;
            if (monitor.Foreground is { } foregroundIdentity
                && MatchIdentity(foregroundIdentity, candidates, used) is { } foregroundId)
            {
                foreground = foregroundId;
            }

            if (strip.Count > 0 || foreground is not null)
            {
                _model.ApplyWorkspace(monitor.MonitorId, Array.Empty<WindowId>(), strip, foreground);
            }
        }
    }

    private static IReadOnlyList<WindowId> Candidates(MonitorState state)
    {
        var candidates = new List<WindowId>();
        if (state.ActiveSet is { } active)
        {
            foreach (var key in active.Members.Reverse())
            {
                candidates.Add(key.Window);
            }
        }

        foreach (var set in state.StripSets)
        {
            foreach (var key in set.Members.Reverse())
            {
                candidates.Add(key.Window);
            }
        }

        return candidates.Distinct().ToArray();
    }

    private WindowId? MatchIdentity(
        WindowIdentity identity,
        IEnumerable<WindowId> candidates,
        IReadOnlySet<WindowId> used)
    {
        foreach (var id in candidates)
        {
            if (used.Contains(id) || !_model.Windows.TryGetValue(id, out var snapshot))
            {
                continue;
            }

            if (snapshot.AppId == identity.AppId && snapshot.Title == identity.Title)
            {
                return id;
            }
        }

        return null;
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
            var stage = new List<WindowIdentity>();
            var foreground = (WindowIdentity?)null;
            if (state.ActiveSet is { } active)
            {
                foreach (var key in active.Members.Reverse())
                {
                    if (_model.Windows.TryGetValue(key.Window, out var snapshot))
                    {
                        stage.Add(new WindowIdentity(snapshot.AppId, snapshot.Title));
                    }
                }

                if (_model.Windows.TryGetValue(active.Foreground.Window, out var foregroundSnapshot))
                {
                    foreground = new WindowIdentity(foregroundSnapshot.AppId, foregroundSnapshot.Title);
                }
            }

            var strip = new List<WindowIdentity>();
            foreach (var set in state.StripSets)
            {
                foreach (var key in set.Members.Reverse())
                {
                    if (_model.Windows.TryGetValue(key.Window, out var snapshot))
                    {
                        strip.Add(new WindowIdentity(snapshot.AppId, snapshot.Title));
                    }
                }
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
            var processChanged = existing.ProcessId != snapshot.ProcessId;
            _model.Update(snapshot);
            if (processChanged)
            {
                _lastAspect.Remove(snapshot.Id);
                _hiddenByApp.Remove(snapshot.Id);
            }

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
            var foregroundKey = WindowKey.For(foregroundSnapshot);
            var foregroundState = _model.Monitors.GetValueOrDefault(foregroundSnapshot.MonitorId);
            var inActive = foregroundState?.ActiveSet?.Contains(foregroundKey) == true;
            if (!inActive
                && _suppressActivation == 0
                && !_desktopShown
                && !Suspended)
            {
                _suppressActivation++;
                try
                {
                    ActivateWindow(window);
                }
                finally
                {
                    _suppressActivation--;
                }
            }
            else
            {
                _model.SetForeground(window);
            }
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
            _suppressActivation++;
            try
            {
                Reconcile();
            }
            finally
            {
                _suppressActivation--;
            }
        }
    }

    private void OnDeviceLost() => RunOnUiThread(RecoverRenderThread);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        RunOnUiThread(() =>
        {
            RecreateOverlays(loadWorkspace: false);
            ClampGeometries();
        });

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
        _visibilityByMonitor.Clear();
        _stripAreas.Clear();
        ClearListing();
        _monitors = _display.GetMonitors();
        foreach (var monitor in ManagedMonitors(_settings))
        {
            CreateOverlay(monitor);
        }

        _autoHide.Suppress(false);
        _autoHide.RefreshMonitors();
        _render = new RenderThread();
        _render.DeviceLost += OnDeviceLost;
        _fullscreenPaused = IsFullscreen(_source.GetForegroundWindow());
        StartRenderThread();
        _visibilityByMonitor.Clear();
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
            || !_settings.IsManaged(monitorId))
        {
            return null;
        }

        return _stripAreas.GetValueOrDefault(monitorId);
    }

    private bool IsOverStrip(Point point) =>
        _openListingSetId is not null || MonitorOverStrip(point) is not null;

    private void OnStripWheel(Point point, int delta)
    {
        if (_disposed || !_settings.Enabled || Suspended || _openListingSetId is not null)
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
            if (!DragGesture.Outside(area.BoundsDip, x, y))
            {
                return monitor.Id;
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

    private void SetVisibility(string monitorId, StripVisibility visibility)
    {
        if (_disposed)
        {
            return;
        }

        if (_visibilityByMonitor.TryGetValue(monitorId, out var current) && current == visibility)
        {
            return;
        }

        _visibilityByMonitor[monitorId] = visibility;
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
        if (_openListingSetId is { } openId
            && !states.Values.Any(state => state.StripSets.Any(set => set.Id == openId)))
        {
            ClearListing();
        }

        var highContrast = SystemInformation.HighContrast;
        var borderBgra = highContrast ? ToBgra(SystemColors.WindowText) : 0;
        foreach (var monitor in _monitors)
        {
            if (!_overlays.TryGetValue(monitor.Id, out var overlay))
            {
                continue;
            }

            var state = states.GetValueOrDefault(monitor.Id);
            var dpi = _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
            var toDip = dpi > 0 ? 96.0 / dpi : 1.0;
            var groups = new List<StripGroup>();
            var items = new List<StripItem>(state?.StripSets.Count ?? 0);
            foreach (var set in state?.StripSets ?? Array.Empty<StageSet>())
            {
                var members = set.Members
                    .Where(key => Resolve(key) is not null)
                    .ToArray();
                if (members.Length == 0)
                {
                    continue;
                }

                if (Resolve(set.Front) is not { } front)
                {
                    continue;
                }

                var aspect = AspectOf(front);
                var group = new StripGroup(set.Id, set.Front, members);
                groups.Add(group);
                items.Add(new StripItem(
                    group.SetId,
                    group.Members,
                    group.Members
                        .Select(key => Resolve(key)!.AppId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    aspect));
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
            var workLeft = (monitor.WorkArea.Left - monitor.Bounds.Left) * toDip;
            var workTop = (monitor.WorkArea.Top - monitor.Bounds.Top) * toDip;
            var workWidth = monitor.WorkArea.Width * toDip;
            var workHeight = monitor.WorkArea.Height * toDip;
            var edgeX = _settings.EdgeFor(monitor.Id) == StripEdge.Left
                ? 0
                : Math.Max(0, monitorWidth - AutoHideController.RevealEdgeDip);
            var edgeZone = new RectD(edgeX, workTop, AutoHideController.RevealEdgeDip, workHeight);
            var exposure = Exposure(monitor, layout, toDip);
            var areaBounds = layout.Tiles.Count == 0
                ? layout.StripBounds
                : Union(layout.Tiles.Select(tile => tile.HitBoundsDip));
            _stripAreas[monitor.Id] = new StripHitArea(
                areaBounds,
                edgeZone,
                layout.StripBounds,
                layout.Tiles,
                exposure);

            var groupsById = groups.ToDictionary(group => group.SetId, StringComparer.Ordinal);
            var visuals = new List<SetTileVisual>(layout.Tiles.Count);
            foreach (var placement in layout.Tiles)
            {
                if (!groupsById.TryGetValue(placement.SetId, out var group))
                {
                    continue;
                }

                var cards = new List<CardVisual>(placement.Cards.Count);
                foreach (var card in placement.Cards)
                {
                    if (Resolve(card.Window) is not { } snapshot)
                    {
                        continue;
                    }

                    cards.Add(new CardVisual(
                        card.Window,
                        card.BoundsDip,
                        card.StackIndex,
                        card.Scale,
                        GetIcon(snapshot),
                        snapshot.IsMinimized,
                        (nint)snapshot.Id.Value,
                        snapshot.AppId,
                        snapshot.AppName,
                        snapshot.Title));
                }

                var badges = Badges(group, placement.BadgeBoundsDip);
                var listing = Listing(
                    group,
                    placement.UnionBoundsDip,
                    workLeft,
                    workTop,
                    workWidth,
                    workHeight);
                visuals.Add(new SetTileVisual(
                    group.SetId,
                    placement.UnionBoundsDip,
                    TileCornerRadiusDip,
                    cards,
                    badges.Rows,
                    placement.BadgeBoundsDip,
                    listing.Rows,
                    listing.Open,
                    highContrast,
                    borderBgra));
            }

            var open = visuals.Any(visual => visual.ListingOpen);
            var visibility = open
                ? StripVisibility.Revealed
                : _visibilityByMonitor.GetValueOrDefault(monitor.Id, StripVisibility.Hidden);
            var modal = FindModal(monitor, toDip);
            var presentation = OverlayPresentation.Hidden;
            if (_settings.Enabled
                && !Suspended
                && !_desktopShown
                && (layout.Tiles.Count > 0 || open))
            {
                presentation = visibility switch
                {
                    StripVisibility.Hidden => OverlayPresentation.Hidden,
                    StripVisibility.Occluded => OverlayPresentation.Occluded,
                    _ => modal is null ? OverlayPresentation.Revealed : OverlayPresentation.Shielded
                };
            }

            overlay.SetTiles(visuals, layout.Tiles);
            overlay.SetPresentation(
                presentation,
                presentation == OverlayPresentation.Shielded ? modal : null);
            _render.Post(new RenderCommand.UpdateOverlay(
                monitor.Id,
                visuals,
                layout.Tiles,
                presentation,
                layout.StripBounds));
        }
    }

    private double AspectOf(WindowSnapshot snapshot)
    {
        var boundsAspect = snapshot.Bounds.Height > 0
            ? (double)snapshot.Bounds.Width / snapshot.Bounds.Height
            : 4.0 / 3.0;
        // A minimized window reports its icon-rail rect. Remember the last real
        // aspect, and fall back to the restore rect after an app restart when
        // that memory is gone but the windows are still minimized.
        if (snapshot.IsMinimized)
        {
            var aspect = _lastAspect.TryGetValue(snapshot.Id, out var remembered)
                ? remembered
                : RestoreAspect(snapshot.Id);
            _lastAspect[snapshot.Id] = aspect;
            return aspect;
        }

        _lastAspect[snapshot.Id] = boundsAspect;
        return boundsAspect;
    }

    private (IReadOnlyList<BadgeChipVisual> Rows, bool Open) Badges(
        StripGroup group,
        RectD cluster)
    {
        var apps = group.Members
            .Reverse()
            .Select(key => Resolve(key))
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .GroupBy(snapshot => snapshot.AppId, StringComparer.Ordinal)
            .Select(app => new
            {
                AppId = app.Key,
                Snapshot = app.First(),
                Count = app.Count()
            })
            .ToArray();
        if (apps.Length == 0)
        {
            return (Array.Empty<BadgeChipVisual>(), false);
        }

        var display = apps.Take(4).ToArray();
        var overflow = apps.Length - display.Length;
        var slots = display.Length + (overflow > 0 ? 1 : 0);
        var step = slots > 1 ? Math.Max(0, (cluster.Width - 28) / (slots - 1)) : 0;
        var rows = new List<BadgeChipVisual>(slots);
        for (var index = 0; index < display.Length; index++)
        {
            var app = display[index];
            rows.Add(new BadgeChipVisual(
                app.AppId,
                app.Snapshot.AppName,
                app.Count,
                new RectD(cluster.X + index * step, cluster.Y, 28, 28),
                false,
                GetIcon(app.Snapshot)));
        }

        if (overflow > 0)
        {
            rows.Add(new BadgeChipVisual(
                string.Empty,
                string.Empty,
                overflow,
                new RectD(cluster.X + display.Length * step, cluster.Y, 28, 28),
                true,
                null));
        }

        return (rows, _openListingSetId == group.SetId);
    }

    private (IReadOnlyList<ListingRowVisual> Rows, bool Open) Listing(
        StripGroup group,
        RectD union,
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight)
    {
        if (_openListingSetId != group.SetId)
        {
            return (Array.Empty<ListingRowVisual>(), false);
        }

        var members = group.Members
            .Reverse()
            .Where(key => Resolve(key) is not null)
            .ToArray();
        if (members.Length == 0)
        {
            return (Array.Empty<ListingRowVisual>(), true);
        }

        var width = Math.Min(ListingMaxWidthDip, Math.Max(220, union.Width));
        width = Math.Min(width, Math.Max(120, workWidth - 12));
        var visibleRows = Math.Min(
            members.Length,
            Math.Max(1, (int)((workHeight - 12) / ListingRowDip)));
        var maxScroll = Math.Max(0, members.Length - visibleRows);
        _listingMaxScroll[group.SetId] = maxScroll;
        var scroll = Math.Clamp(_listingScroll.GetValueOrDefault(group.SetId), 0, maxScroll);
        _listingScroll[group.SetId] = scroll;
        var height = visibleRows * ListingRowDip + ListingPaddingDip * 2;
        var x = union.X + union.Width / 2 - width / 2;
        var maxX = workLeft + Math.Max(0, workWidth - width);
        x = Math.Clamp(x, workLeft, maxX);
        var y = union.Y + union.Height / 2 - height / 2;
        var maxY = workTop + Math.Max(0, workHeight - height);
        y = Math.Clamp(y, workTop, maxY);
        var rows = new List<ListingRowVisual>(visibleRows);
        for (var index = 0; index < visibleRows; index++)
        {
            var rowIndex = scroll + index;
            if (rowIndex >= members.Length)
            {
                break;
            }

            var key = members[rowIndex];
            if (Resolve(key) is not { } snapshot)
            {
                continue;
            }

            rows.Add(new ListingRowVisual(
                key,
                rowIndex,
                new RectD(x + ListingPaddingDip, y + ListingPaddingDip + index * ListingRowDip,
                    width - ListingPaddingDip * 2, ListingRowDip),
                snapshot.AppId,
                snapshot.AppName,
                snapshot.Title,
                GetIcon(snapshot)));
        }

        return (rows, true);
    }

    private unsafe RectD? FindModal(MonitorDescriptor monitor, double toDip)
    {
        if (_source.GetForegroundWindow() is not { } foreground)
        {
            return null;
        }

        var hwnd = new HWND((nint)foreground.Value);
        var owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
        if (owner == HWND.Null || PInvoke.IsWindowEnabled(owner))
        {
            return null;
        }

        var popup = PInvoke.GetLastActivePopup(owner);
        if (popup == HWND.Null || popup == owner)
        {
            return null;
        }

        var bounds = _source.GetBounds((nint)popup.Value);
        if (bounds.Width <= 0 || bounds.Height <= 0
            || MonitorTopology.IdForBounds(_monitors, bounds) != monitor.Id)
        {
            return null;
        }

        return new RectD(
            (bounds.Left - monitor.Bounds.Left) * toDip,
            (bounds.Top - monitor.Bounds.Top) * toDip,
            bounds.Width * toDip,
            bounds.Height * toDip);
    }

    private double Exposure(MonitorDescriptor monitor, StripLayout layout, double toDip)
    {
        if (_source.GetForegroundWindow() is not { } foreground)
        {
            return monitor.Bounds.Width * toDip;
        }

        var bounds = _source.GetExtendedFrameBounds((nint)foreground.Value);
        if (bounds.Width <= 0 || bounds.Height <= 0
            || MonitorTopology.IdForBounds(_monitors, bounds) != monitor.Id)
        {
            return monitor.Bounds.Width * toDip;
        }

        var foregroundLeft = (bounds.Left - monitor.Bounds.Left) * toDip;
        var foregroundRight = foregroundLeft + bounds.Width * toDip;
        return _settings.EdgeFor(monitor.Id) == StripEdge.Left
            ? Math.Max(0, foregroundLeft - layout.StripBounds.X)
            : Math.Max(0, layout.StripBounds.X + layout.StripBounds.Width - foregroundRight);
    }

    private void ToggleListing(string setId)
    {
        if (_openListingSetId == setId)
        {
            CloseListing();
            return;
        }

        _openListingSetId = setId;
        _listingScroll[setId] = 0;
        _autoHide.Suppress(true);
        Refresh();
    }

    private void CloseListing()
    {
        if (_openListingSetId is null)
        {
            return;
        }

        ClearListing();
        Refresh();
    }

    private void ClearListing()
    {
        _openListingSetId = null;
        _listingScroll.Clear();
        _listingMaxScroll.Clear();
        _autoHide.Suppress(false);
    }

    private void ScrollListing(string setId, int delta)
    {
        if (_openListingSetId != setId || delta == 0)
        {
            return;
        }

        var max = _listingMaxScroll.GetValueOrDefault(setId);
        var current = _listingScroll.GetValueOrDefault(setId);
        var next = Math.Clamp(current - Math.Sign(delta), 0, max);
        if (next == current)
        {
            return;
        }

        _listingScroll[setId] = next;
        Refresh();
    }

    private void ActivateFromUia(string monitorId, WindowId window)
    {
        _visibilityByMonitor[monitorId] = StripVisibility.Revealed;
        Refresh();
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
        if (previous is not null)
        {
            CaptureActiveGeometry(snapshot.MonitorId);
        }

        var result = _model.Activate(window, ActivationSource.User);
        if (result.Outcome == ActivationOutcome.Failure)
        {
            FileLogger.Default.Write(
                "activate",
                $"window={window.Value} app={snapshot.AppId} reason=model");
            Reconcile();
            StatusMessage?.Invoke("Windows refused to activate the window.");
            return;
        }

        var active = _model.Monitors.GetValueOrDefault(snapshot.MonitorId)?.ActiveSet;
        if (active is null)
        {
            if (previous is not null)
            {
                _model.Restore(previous);
            }

            Reconcile();
            StatusMessage?.Invoke("Windows refused to activate the window.");
            return;
        }

        ClearListing();
        var key = WindowKey.For(snapshot);
        foreach (var member in active.Members)
        {
            if (Resolve(member) is null)
            {
                continue;
            }

            WindowActivation.Restore(member.Window);
            _hiddenByApp.Remove(member.Window);
            RestoreGeometry(member);
        }

        if (result.Outcome == ActivationOutcome.CreatedSet
            && !active.Geometry.ContainsKey(key))
        {
            CenterSetStart(key);
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

    private void ShiftAddWindow(WindowId window)
    {
        if (!_model.Windows.TryGetValue(window, out var snapshot))
        {
            Reconcile();
            return;
        }

        _model.Monitors.TryGetValue(snapshot.MonitorId, out var previous);
        if (previous is not null)
        {
            CaptureActiveGeometry(snapshot.MonitorId);
        }

        var result = _model.ShiftAdd(window);
        if (result.Outcome == ActivationOutcome.Failure)
        {
            Reconcile();
            return;
        }

        var active = _model.Monitors.GetValueOrDefault(snapshot.MonitorId)?.ActiveSet;
        if (active is null)
        {
            if (previous is not null)
            {
                _model.Restore(previous);
            }

            Reconcile();
            return;
        }

        ClearListing();
        var key = WindowKey.For(snapshot);
        foreach (var member in active.Members)
        {
            if (Resolve(member) is null)
            {
                continue;
            }

            WindowActivation.Restore(member.Window);
            _hiddenByApp.Remove(member.Window);
            RestoreGeometry(member);
        }

        if (result.Outcome == ActivationOutcome.CreatedSet
            && !active.Geometry.ContainsKey(key))
        {
            CenterSetStart(key);
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

    private void AddWindowToStage(WindowKey key) => ShiftAddWindow(key.Window);

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

        ClampGeometries();
        Refresh();
    }

    private void CaptureActiveGeometry(string monitorId)
    {
        if (!_model.Monitors.TryGetValue(monitorId, out var state)
            || state.ActiveSet is not { } active)
        {
            return;
        }

        foreach (var key in active.Members)
        {
            CaptureGeometry(key.Window);
        }
    }

    private void CaptureGeometry(WindowId window)
    {
        if (!_model.Windows.TryGetValue(window, out var snapshot) || snapshot.IsMinimized)
        {
            return;
        }

        var hwnd = (nint)window.Value;
        var bounds = _source.GetBounds(hwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var monitorId = snapshot.MonitorId;
        var monitor = _monitors.FirstOrDefault(candidate => candidate.Id == monitorId);
        var dpi = _dpiByMonitor.GetValueOrDefault(monitorId, monitor?.Dpi ?? 96);
        var maximized = _source.IsMaximized(hwnd);
        var restore = maximized ? _source.GetRestoreBounds(hwnd) : (WindowBounds?)null;
        _model.CaptureGeometry(
            window,
            WindowGeometry.FromPixels(bounds, monitorId, dpi, maximized, restore));
    }

    private bool RestoreGeometry(WindowKey key)
    {
        var geometry = FindGeometry(key);
        if (geometry is null)
        {
            return false;
        }

        var monitor = SelectMonitor(geometry);
        if (monitor is null)
        {
            return false;
        }

        var dpi = _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
        var bounds = geometry.IsMaximized
            ? geometry.RestoreBoundsToPixels(dpi)
            : geometry.ToPixels(dpi);
        var clamped = Clamp(bounds, monitor.WorkArea);
        if (!WindowActivation.Place(key.Window, clamped))
        {
            return false;
        }

        if (geometry.IsMaximized)
        {
            WindowActivation.Maximize(key.Window);
        }

        return true;
    }

    private WindowGeometry? FindGeometry(WindowKey key)
    {
        foreach (var state in _model.Monitors.Values)
        {
            if (state.ActiveSet is { } active
                && active.Geometry.TryGetValue(key, out var geometry))
            {
                return geometry;
            }

            foreach (var set in state.StripSets)
            {
                if (set.Geometry.TryGetValue(key, out geometry))
                {
                    return geometry;
                }
            }
        }

        return null;
    }

    private MonitorDescriptor? SelectMonitor(WindowGeometry geometry)
    {
        var saved = _monitors.FirstOrDefault(monitor => monitor.Id == geometry.MonitorId);
        if (saved is not null)
        {
            return saved;
        }

        var pixels = geometry.ToPixels(geometry.Dpi);
        var id = MonitorTopology.IdForBounds(_monitors, pixels);
        return id is null
            ? _monitors.FirstOrDefault()
            : _monitors.FirstOrDefault(monitor => monitor.Id == id);
    }

    private static WindowBounds Clamp(WindowBounds bounds, WindowBounds area)
    {
        var maxX = area.Left + Math.Max(0, area.Width - bounds.Width);
        var maxY = area.Top + Math.Max(0, area.Height - bounds.Height);
        return new WindowBounds(
            Math.Clamp(bounds.Left, area.Left, maxX),
            Math.Clamp(bounds.Top, area.Top, maxY),
            bounds.Width,
            bounds.Height);
    }

    private void CenterSetStart(WindowKey key)
    {
        if (!_model.Windows.TryGetValue(key.Window, out var snapshot)
            || snapshot.IsMinimized
            || snapshot.IsFullscreen
            || !_source.CanQueryProcess(snapshot.ProcessId))
        {
            return;
        }

        var hwnd = (nint)key.Window.Value;
        if (_source.IsMaximized(hwnd))
        {
            return;
        }

        var monitor = _monitors.FirstOrDefault(candidate => candidate.Id == snapshot.MonitorId);
        if (monitor is null)
        {
            return;
        }

        var bounds = _source.GetBounds(hwnd);
        var width = bounds.Width > 0 ? bounds.Width : 800;
        var height = bounds.Height > 0 ? bounds.Height : 600;
        var work = monitor.WorkArea;
        var target = new WindowBounds(
            work.Left + (work.Width - width) / 2,
            work.Top + (work.Height - height) / 2,
            width,
            height);
        WindowActivation.Place(key.Window, target);
    }

    private void ClampGeometries()
    {
        foreach (var state in _model.Monitors.Values)
        {
            foreach (var set in Sets(state))
            {
                foreach (var pair in set.Geometry.ToArray())
                {
                    var monitor = _monitors.FirstOrDefault(candidate =>
                        candidate.Id == pair.Value.MonitorId);
                    if (monitor is null)
                    {
                        continue;
                    }

                    var dpi = _dpiByMonitor.GetValueOrDefault(monitor.Id, monitor.Dpi);
                    var scale = 96.0 / (dpi == 0 ? 96 : dpi);
                    var work = new RectD(
                        monitor.WorkArea.Left * scale,
                        monitor.WorkArea.Top * scale,
                        monitor.WorkArea.Width * scale,
                        monitor.WorkArea.Height * scale);
                    var clamped = pair.Value.ClampInto(work);
                    if (clamped != pair.Value)
                    {
                        _model.CaptureGeometry(pair.Key.Window, clamped);
                    }
                }
            }
        }
    }

    private static IEnumerable<StageSet> Sets(MonitorState state)
    {
        if (state.ActiveSet is { } active)
        {
            yield return active;
        }

        foreach (var set in state.StripSets)
        {
            yield return set;
        }
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

    private WindowSnapshot? Resolve(WindowKey key)
    {
        return _model.Windows.TryGetValue(key.Window, out var snapshot)
            && snapshot.ProcessId == key.ProcessId
            ? snapshot
            : null;
    }

    private static RectD Union(IEnumerable<RectD> bounds)
    {
        var first = bounds.First();
        var left = first.X;
        var top = first.Y;
        var right = first.X + first.Width;
        var bottom = first.Y + first.Height;
        foreach (var rect in bounds)
        {
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.X + rect.Width);
            bottom = Math.Max(bottom, rect.Y + rect.Height);
        }

        return new RectD(left, top, right - left, bottom - top);
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
