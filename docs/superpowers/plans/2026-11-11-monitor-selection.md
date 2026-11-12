# Monitor Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user exclude monitors from all management and override strip edge and auto-hide per monitor.

**Architecture:** `AppSettings` gains a sparse `Monitors` list with a managed flag and nullable view overrides. `MonitorDescriptor.Id` becomes the PnP monitor device path, and the coordinator filters the window model, overlays, hotkeys, clicks, show-desktop, and fullscreen pause by managed monitors. The settings form edits one row per connected monitor.

**Tech Stack:** .NET 10, WinForms, CsWin32, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-11-monitor-selection-design.md`

## Global Constraints

- Core targets `net10.0`; app targets `net10.0-windows10.0.22621.0`.
- Settings JSON uses camelCase property names and string enums via `AtomicJsonFile`.
- A monitor with no saved entry is managed and uses global view settings.
- No migration code: the monitor list is new, and pre-change workspace files are from an unreleased build.
- No new dependencies. Use the existing `MonitorDescriptor`, `MonitorTopology`, and CsWin32 patterns.
- Do not push commits unless the user asks.
- Multi-monitor behavior requires real hardware; the VM has one display.

## Review Focus

- A settings file with blank or duplicate monitor IDs must normalize to one usable entry per monitor, last entry wins.
- A monitor whose device path is empty must fall back to its GDI name instead of sharing an empty key with every other monitor.
- A minimized window's icon-rail rectangle can overlap the primary monitor; assignment must use its restore bounds so it does not leak onto a managed monitor.
- With zero managed monitors, display-change and session-switch callbacks must still run on the UI thread and leave no window hidden.
- A wallpaper click or pointer-scoped hotkey over an unmanaged monitor must do nothing, while the show-desktop hotkey still acts on managed monitors.

---

### Task 1: Per-monitor settings model

**Files:**
- Create: `src/StageManager4win.Core/Settings/MonitorSettings.cs`
- Modify: `src/StageManager4win.Core/Settings/AppSettings.cs`
- Test: `tests/StageManager4win.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `MonitorSettings(string MonitorId, bool Managed = true, StripEdge? StripEdge = null, bool? AutoHide = null)`
- Produces: `AppSettings.Monitors`, `ForMonitor(string)`, `IsManaged(string)`, `EdgeFor(string)`, `AutoHideFor(string)`

- [ ] **Step 1: Write the failing tests**

Add to `SettingsStoreTests`:

```csharp
[Fact]
public void SaveThenLoad_RoundTripsMonitorSettings()
{
    var store = new SettingsStore(_dir);
    var settings = new AppSettings
    {
        Monitors = new[]
        {
            new MonitorSettings(@"\\?\DISPLAY#ONE", Managed: false),
            new MonitorSettings(@"\\?\DISPLAY#TWO", StripEdge: StripEdge.Right, AutoHide: true)
        }
    };

    store.Save(settings);
    var loaded = store.Load();

    Assert.Equal(settings.Monitors, loaded.Monitors);
    Assert.Contains("\"monitors\"", File.ReadAllText(store.FilePath));
}

[Fact]
public void Normalize_CleansMonitorEntries()
{
    var nullList = new AppSettings { Monitors = null! }.Normalize();
    Assert.Empty(nullList.Monitors);

    var normalized = new AppSettings
    {
        Monitors = new[]
        {
            new MonitorSettings("  "),
            new MonitorSettings(@"  \\.\DISPLAY1  ", Managed: true, StripEdge: StripEdge.Left),
            new MonitorSettings(@"\\.\DISPLAY1", Managed: false, StripEdge: (StripEdge)99),
            new MonitorSettings(@"\\.\DISPLAY2", StripEdge: StripEdge.Right),
            new MonitorSettings(@"\\.\DISPLAY2", StripEdge: StripEdge.Left)
        }
    }.Normalize();

    Assert.Equal(2, normalized.Monitors.Count);
    Assert.Equal(@"\\.\DISPLAY1", normalized.Monitors[0].MonitorId);
    Assert.False(normalized.Monitors[0].Managed);
    Assert.Null(normalized.Monitors[0].StripEdge);
    Assert.Equal(@"\\.\DISPLAY2", normalized.Monitors[1].MonitorId);
    Assert.Equal(StripEdge.Left, normalized.Monitors[1].StripEdge);
}

[Fact]
public void MonitorResolvers_UseDefaultsAndOverrides()
{
    var settings = new AppSettings
    {
        StripEdge = StripEdge.Left,
        AutoHide = false,
        Monitors = new[]
        {
            new MonitorSettings("A", Managed: false, StripEdge: StripEdge.Right, AutoHide: true),
            new MonitorSettings("B")
        }
    };

    Assert.False(settings.IsManaged("a"));
    Assert.True(settings.IsManaged("missing"));
    Assert.Equal(StripEdge.Right, settings.EdgeFor("A"));
    Assert.Equal(StripEdge.Left, settings.EdgeFor("missing"));
    Assert.True(settings.AutoHideFor("A"));
    Assert.False(settings.AutoHideFor("missing"));
    Assert.Null(settings.ForMonitor("missing"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: compile error, `MonitorSettings` and `AppSettings.Monitors` do not exist.

- [ ] **Step 3: Implement the settings model**

Create `MonitorSettings.cs` with `using StripEdges = StageManager4win.Core.Layout.StripEdge;` and the record from the interfaces block.

In `AppSettings`, add `Monitors` and the resolvers:

```csharp
public IReadOnlyList<MonitorSettings> Monitors { get; init; }
    = Array.Empty<MonitorSettings>();

public MonitorSettings? ForMonitor(string monitorId) =>
    Monitors.FirstOrDefault(entry =>
        string.Equals(entry.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));

public bool IsManaged(string monitorId) => ForMonitor(monitorId)?.Managed ?? true;

public StripEdges EdgeFor(string monitorId) =>
    ForMonitor(monitorId)?.StripEdge ?? StripEdge;

public bool AutoHideFor(string monitorId) =>
    ForMonitor(monitorId)?.AutoHide ?? AutoHide;
```

In `Normalize()`, add `Monitors = NormalizeMonitors(Monitors)`. The helper trims IDs, drops blank IDs, deduplicates case-insensitively with last-entry-wins, and turns an undefined `StripEdge` into `null`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Settings/MonitorSettings.cs \
  src/StageManager4win.Core/Settings/AppSettings.cs \
  tests/StageManager4win.Core.Tests/SettingsStoreTests.cs
git commit -m "feat: add per-monitor settings"
```

---

### Task 2: Device-path monitor identity

**Files:**
- Create: `src/StageManager4win.Core/Layout/MonitorKey.cs`
- Modify: `src/StageManager4win.Core/Layout/MonitorDescriptor.cs`
- Modify: `src/StageManager4win.App/Windows/DisplayManager.cs`
- Modify: `src/StageManager4win.App/NativeMethods.txt`
- Test: `tests/StageManager4win.Core.Tests/MonitorKeyTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `MonitorKey.FromDeviceId(string? deviceId, string gdiDeviceName) -> string`
- Produces: `MonitorDescriptor(string Id, WindowBounds Bounds, WindowBounds WorkArea, uint Dpi, string GdiDeviceName = "", string Name = "", bool IsPrimary = false)`

- [ ] **Step 1: Write the failing tests**

Create `MonitorKeyTests.cs`:

```csharp
using StageManager4win.Core.Layout;
using Xunit;

namespace StageManager4win.Core.Tests;

public class MonitorKeyTests
{
    [Fact]
    public void FromDeviceId_StripsTheInterfaceGuid()
    {
        var path = @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

        Assert.Equal(
            @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018",
            MonitorKey.FromDeviceId(path, @"\\.\DISPLAY1"));
    }

    [Fact]
    public void FromDeviceId_KeepsAPathWithoutTheGuid()
    {
        var path = @"\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018";

        Assert.Equal(path, MonitorKey.FromDeviceId(path, @"\\.\DISPLAY1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromDeviceId_FallsBackToTheGdiName(string? deviceId)
    {
        Assert.Equal(@"\\.\DISPLAY1", MonitorKey.FromDeviceId(deviceId, @"\\.\DISPLAY1"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: compile error, `MonitorKey` does not exist.

- [ ] **Step 3: Implement `MonitorKey` and the descriptor fields**

Create `MonitorKey.cs` in `StageManager4win.Core.Layout`. `FromDeviceId` trims the device path, returns `gdiDeviceName` when the path is blank, and otherwise removes a trailing `#{...}` interface-class suffix at the last `#{`.

Append the new optional fields to `MonitorDescriptor` so existing four-argument constructors keep compiling:

```csharp
public sealed record MonitorDescriptor(
    string Id,
    WindowBounds Bounds,
    WindowBounds WorkArea,
    uint Dpi,
    string GdiDeviceName = "",
    string Name = "",
    bool IsPrimary = false);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 5: Fill the identity from `DisplayManager`**

Add `EnumDisplayDevices`, `DISPLAY_DEVICEW`, `EDD_GET_DEVICE_INTERFACE_NAME`, and `MONITORINFOF_PRIMARY` to `NativeMethods.txt`.

In `DisplayManager.GetMonitors()`, for each monitor keep the existing `GetMonitorInfo`/DPI calls and then resolve identity:

```csharp
var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
var name = string.Empty;
var id = deviceName;
if (PInvoke.EnumDisplayDevices(
        deviceName, 0, ref device, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
{
    name = device.DeviceString.ToString();
    id = MonitorKey.FromDeviceId(device.DeviceID.ToString(), deviceName);
}

var isPrimary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
monitors.Add(new MonitorDescriptor(
    id, bounds, workArea, dpiX,
    GdiDeviceName: deviceName,
    Name: name,
    IsPrimary: isPrimary));
```

`deviceName` is the existing `info.szDevice.ToString()`.

- [ ] **Step 6: Build the app**

Run: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/StageManager4win.Core/Layout/MonitorKey.cs \
  src/StageManager4win.Core/Layout/MonitorDescriptor.cs \
  src/StageManager4win.App/Windows/DisplayManager.cs \
  src/StageManager4win.App/NativeMethods.txt \
  tests/StageManager4win.Core.Tests/MonitorKeyTests.cs
git commit -m "feat: identify monitors by device path"
```

---

### Task 3: Assign minimized windows by restore bounds

**Files:**
- Modify: `src/StageManager4win.Core/Layout/MonitorTopology.cs`
- Modify: `src/StageManager4win.App/Windows/WindowSource.cs`
- Test: `tests/StageManager4win.Core.Tests/MonitorTopologyTests.cs`

**Interfaces:**
- Consumes: `MonitorDescriptor` from Task 2.
- Produces: `MonitorTopology.IdForWindow(IReadOnlyList<MonitorDescriptor> monitors, WindowBounds bounds, WindowBounds restoreBounds, bool minimized) -> string?`

- [ ] **Step 1: Write the failing tests**

Add to `MonitorTopologyTests`:

```csharp
[Fact]
public void IdForWindow_MinimizedUsesRestoreBounds()
{
    var bounds = new WindowBounds(50, 50, 200, 200);
    var restore = new WindowBounds(1050, 50, 200, 200);

    Assert.Equal("M2", MonitorTopology.IdForWindow(
        new[] { Left, Right }, bounds, restore, minimized: true));
}

[Fact]
public void IdForWindow_NormalUsesCurrentBounds()
{
    var bounds = new WindowBounds(50, 50, 200, 200);
    var restore = new WindowBounds(1050, 50, 200, 200);

    Assert.Equal("M1", MonitorTopology.IdForWindow(
        new[] { Left, Right }, bounds, restore, minimized: false));
}

[Fact]
public void IdForWindow_EmptyList_ReturnsNull()
{
    Assert.Null(MonitorTopology.IdForWindow(
        Array.Empty<MonitorDescriptor>(),
        new WindowBounds(0, 0, 10, 10),
        new WindowBounds(0, 0, 10, 10),
        minimized: false));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: compile error, `IdForWindow` does not exist.

- [ ] **Step 3: Implement `IdForWindow` and use it**

`MonitorTopology.IdForWindow` returns `IdForBounds(monitors, minimized ? restoreBounds : bounds)`. A zero-sized restore rect falls back to `bounds` before lookup.

In `WindowSource.Describe`, compute `minimized = PInvoke.IsIconic(hwnd)` once, use `GetRestoreBounds(handle)` only when minimized, call `MonitorTopology.IdForWindow`, and pass the existing `minimized` value to `WindowSnapshot` instead of calling `IsIconic` again.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 5: Build the app**

Run: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/StageManager4win.Core/Layout/MonitorTopology.cs \
  src/StageManager4win.App/Windows/WindowSource.cs \
  tests/StageManager4win.Core.Tests/MonitorTopologyTests.cs
git commit -m "fix: assign minimized windows by restore bounds"
```

---

### Task 4: Managed overlay lifecycle and exclusion transition

**Files:**
- Modify: `src/StageManager4win.App/StageCoordinator.cs`

**Interfaces:**
- Consumes: `AppSettings.IsManaged`, `AppSettings.EdgeFor`, `AppSettings.AutoHideFor` from Task 1; `MonitorDescriptor` from Task 2.
- Produces: `StageCoordinator.Monitors` (`IReadOnlyList<MonitorDescriptor>`) for Task 6.

- [ ] **Step 1: Capture the UI synchronization context**

Add a `SynchronizationContext _ui` field. In the constructor:

```csharp
_ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
```

Replace `RunOnUiThread` with:

```csharp
private void RunOnUiThread(Action action)
{
    if (_disposed)
    {
        return;
    }
    _ui.Post(_ =>
    {
        if (!_disposed)
        {
            action();
        }
    }, null);
}
```

Change `OnDisplaySettingsChanged` to post a lambda: `RunOnUiThread(() => RecreateOverlays())`, because `RecreateOverlays` now has an optional parameter and no longer converts to `Action`.

- [ ] **Step 2: Create overlays only for managed monitors**

Add `public IReadOnlyList<MonitorDescriptor> Monitors => _monitors;` and a private helper:

```csharp
private IReadOnlyList<MonitorDescriptor> ManagedMonitors(AppSettings settings) =>
    _monitors.Where(monitor => settings.IsManaged(monitor.Id)).ToArray();
```

Use `ManagedMonitors(_settings)` in `Start()` for overlay creation. `StartRenderThread()` already skips monitors without overlays.

Change `RecreateOverlays` to `private void RecreateOverlays(bool loadWorkspace = true)`. After `_monitors = _display.GetMonitors()`, create overlays only for `ManagedMonitors(_settings)`. When `loadWorkspace` is false, do not call `Rebuild()`; instead set `_stripHideReadyAt = Environment.TickCount64 + 1200`, call `Reconcile()`, and restart `_hideStrip`. The display-change path keeps `loadWorkspace: true`.

- [ ] **Step 3: Filter the window model and resolve view settings**

In `Enumerate()`, filter snapshots with `_settings.IsManaged(snapshot.MonitorId)` after app exclusions.

In `GetStripArea`, return null unless `_settings.Enabled && !Suspended && _settings.IsManaged(monitorId) && _settings.AutoHideFor(monitorId)`.

In `Refresh()`, use `_settings.EdgeFor(monitor.Id)` for `StripLayoutOptions` and for `edgeX`, and `_settings.AutoHideFor(monitor.Id)` for `autoHideVisible`.

In `ActivateFromUia`, replace `_settings.AutoHide` with `_settings.AutoHideFor(monitorId)`.

- [ ] **Step 4: Apply managed-set changes**

In `ApplySettings`, keep the old settings long enough to compare:

```csharp
var previous = _settings;
var next = settings.Normalize();
if (_monitors.Any(monitor => previous.AutoHideFor(monitor.Id) != next.AutoHideFor(monitor.Id)))
{
    _visibleByMonitor.Clear();
}

var previousManaged = ManagedMonitors(previous).Select(monitor => monitor.Id).ToHashSet(StringComparer.Ordinal);
var nextManaged = ManagedMonitors(next).Select(monitor => monitor.Id).ToHashSet(StringComparer.Ordinal);
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
```

`ExcludeMonitors(IEnumerable<string> monitorIds)`:

1. For each excluded monitor, materialize `_model.Windows.Values.Where(snapshot => snapshot.MonitorId == monitorId).ToArray()`.
2. For every such window in `_hiddenByApp`, call `WindowActivation.RestoreNoActivate`, then remove it from `_hiddenByApp`.
3. Remove those snapshots from `_model` and `_lastAspect`.
4. If `_desktopShown` and `_desktopStage` is not null, remove the excluded monitors from `_desktopStage`. If it becomes empty, set `_desktopShown = false` and `_desktopForeground = null`; otherwise refresh `_desktopForeground` from the remaining stages.
5. Remove the excluded IDs from `_stripAreas`, `_visibleByMonitor`, `_scrollOffset`, `_scrollTarget`, `_maxScroll`, and `_dpiByMonitor`.
6. If `_cycleMonitorId` is excluded, set `_cycle = null`, `_cycleMonitorId = null`, and stop `_cycleReset`.
7. Call `ScheduleWorkspaceSave()`.

Restoring in step 2 covers show-desktop windows because show-desktop adds its minimized windows to `_hiddenByApp`.

- [ ] **Step 5: Build the app**

Run: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/StageManager4win.App/StageCoordinator.cs
git commit -m "feat: honor managed monitors in the coordinator"
```

---

### Task 5: Scope hotkeys, clicks, show-desktop, and fullscreen

**Files:**
- Modify: `src/StageManager4win.App/StageCoordinator.cs`
- Modify: `src/StageManager4win.App/Input/DesktopClickWatcher.cs`

**Interfaces:**
- Consumes: `StageCoordinator.Monitors`, `AppSettings.IsManaged`; `MonitorTopology.IdAt`.
- Produces: `DesktopClickWatcher(Func<Point, bool> isOverlayPoint, Func<Point, bool> isManagedPoint)`.

- [ ] **Step 1: Gate pointer-scoped actions**

`MonitorUnderPointer()`:

```csharp
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
return ManagedMonitors(_settings).FirstOrDefault()?.Id;
```

Toggle and cycle keep their existing null checks, so they no-op over an unmanaged monitor.

- [ ] **Step 2: Filter show desktop by managed monitors**

In `ShowDesktop`, snapshot only `_model.Monitors.Values` where `_settings.IsManaged(state.MonitorId)`. When restoring, restore only saved states where `_settings.IsManaged(state.MonitorId)`.

- [ ] **Step 3: Gate fullscreen pause**

```csharp
private bool IsFullscreen(WindowId? window)
{
    if (window is not { } value || !_source.IsFullscreen((nint)value.Value))
    {
        return false;
    }
    var monitorId = MonitorTopology.IdForBounds(_monitors, _source.GetBounds((nint)value.Value));
    return monitorId is not null && _settings.IsManaged(monitorId);
}
```

- [ ] **Step 4: Ignore wallpaper clicks on unmanaged monitors**

Add `Func<Point, bool> isManagedPoint` to `DesktopClickWatcher` and store it. In `IsWallpaper`, return false before the overlay/desktop/icon checks unless `_isManagedPoint(point)` is true.

In `StageCoordinator.Start()`, create the watcher with:

```csharp
new DesktopClickWatcher(
    IsOverStrip,
    point => MonitorTopology.IdAt(_monitors, point.X, point.Y) is { } id
        && _settings.IsManaged(id));
```

- [ ] **Step 5: Build the app**

Run: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/StageManager4win.App/StageCoordinator.cs \
  src/StageManager4win.App/Input/DesktopClickWatcher.cs
git commit -m "feat: scope pointer actions to managed monitors"
```

---

### Task 6: Monitor table in the settings form

**Files:**
- Modify: `src/StageManager4win.App/UI/SettingsForm.cs`
- Modify: `src/StageManager4win.App/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `StageCoordinator.Monitors` from Task 4; `MonitorSettings` from Task 1.
- Produces: `SettingsForm(AppSettings settings, IReadOnlyList<MonitorDescriptor> monitors)`.

- [ ] **Step 1: Add the monitor table**

At the top of the outer layout, above the global rows, add a "Monitors" header and a four-column `TableLayoutPanel`: Monitor, Managed, Strip edge, Auto-hide.

For each connected monitor, keep these controls in a row record:

```csharp
private sealed record MonitorRow(
    string MonitorId,
    CheckBox Managed,
    ComboBox Edge,
    ComboBox AutoHide);
```

Edge items are `Use global`, `Left`, `Right`; selected index is 0 for null, 1 for `Left`, 2 for `Right`. Auto-hide items are `Use global`, `On`, `Off`; selected index is 0 for null, 1 for true, 2 for false.

Label helper:

```csharp
private static string MonitorLabel(MonitorDescriptor monitor)
{
    var number = new string(monitor.GdiDeviceName.Where(char.IsDigit).ToArray());
    var name = monitor.Name;
    if (string.IsNullOrWhiteSpace(name)
        || name.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
    {
        name = int.TryParse(number, out var display) ? $"Display {display}" : "Display";
    }
    var label = $"{name} — {monitor.Bounds.Width}×{monitor.Bounds.Height}";
    return monitor.IsPrimary ? label + " (primary)" : label;
}
```

Keep all controls enabled for unmanaged monitors so they can be preconfigured.

- [ ] **Step 2: Merge monitor rows in `BuildResult`**

Pass the rows to `BuildResult`. Start with `original.Monitors` entries whose ID is not a connected monitor (compare with `OrdinalIgnoreCase`), then add each connected row:

- `managed = row.Managed.Checked`
- edge override from the combo index
- auto-hide override from the combo index
- skip the row when `managed && edge is null && autoHide is null`

Assign the resulting list to `AppSettings.Monitors`. This keeps disconnected entries and keeps the file sparse.

- [ ] **Step 3: Pass the monitor list from the tray**

In `TrayApplicationContext.ShowSettings`, open `new SettingsForm(_settings, _coordinator.Monitors)`.

- [ ] **Step 4: Build the app**

Run: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.App/UI/SettingsForm.cs \
  src/StageManager4win.App/TrayApplicationContext.cs
git commit -m "feat: add monitor settings table"
```

---

### Task 7: Verify on the Windows VM

**Files:** none unless a check fails.

**Interfaces:**
- Consumes all previous tasks.

- [x] **Step 1: Run the full core tests and Release build**

Run:

```bash
dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj
dotnet build src/StageManager4win.App/StageManager4win.App.csproj -c Release
```

Expected: all tests pass, 0 warnings.

- [x] **Step 2: Sync the source and build on the VM**

Use the existing VM (`ssh -i ~/.ssh/stagemanager4win_vm user@172.16.17.146`). Sync the repo to `C:\Users\user\sm4w-v2` excluding `.git`, `bin`, and `obj`, then run `dotnet build -c Release` on the VM. Do not stop `WindowsTerminal`.

- [x] **Step 3: Check the single-monitor behavior**

Do not use `sm4w-run` for the persistence checks: its `body.ps1` rewrites
`settings.json` and deletes `workspace.json`. Clone the existing scheduled task
XML, point it at a new `C:\Users\user\sm4w-v2\monitor-run.vbs`, and have that
script only start
`C:\Users\user\sm4w-v2\src\StageManager4win.App\bin\Release\net10.0-windows10.0.22621.0\StageManager4win.exe`
in the interactive session. Leave the settings file untouched.

Using that launcher, verify:

1. Uncheck the only monitor in Settings. The strip disappears; windows the app minimized are restored; toggle and cycle hotkeys do nothing; wallpaper clicks do nothing.
2. Restart the app. The monitor is still unmanaged.
3. Recheck it. The strip returns; the active app may be staged and the rest return to the strip.
4. With the monitor unmanaged, trigger a display-settings change and a session lock/unlock. The app does not crash and no window stays hidden.
5. With the monitor unmanaged, press the show-desktop hotkey: nothing visible happens. Re-manage it: the strip is visible. Press show-desktop again; the stage hides, and pressing it once more restores the windows.
6. Set a per-monitor edge and auto-hide override that differ from the global values; confirm the strip uses the override. Confirm `%LOCALAPPDATA%\StageManager4win\settings.json` stores the entry under `monitors` with the device-path ID.
7. Run the existing v2 smoke checks on the managed monitor (tile click, drag to tile, quit restores windows).

Record the actual results in the task report. If any check fails, fix it and repeat from Step 1.

**Verification results (2026-11-12, Windows 11 ARM64 VM, 1044×809 single display):**

- Core tests: 127/127 pass. Local and VM Release builds: 0 warnings, 0 errors.
- Exclusion via Settings: saved `managed:false` under the device-path ID, saved
  `workspace.json` as `{"monitors":[]}`, destroyed the strip overlay, and
  restored all 8 app windows (`iconic=False`).
- Persistence: restarting through `sm4w-monitor-run` (which never touches the
  settings file) kept the monitor unchecked.
- Re-manage: rechecking the monitor recreated the strip and repopulated the
  workspace with the 8 strip entries.
- Zero-managed hotkeys: posting `WM_HOTKEY` ids 1-4 directly to the app's
  hidden hotkey window had no effect; the app stayed alive, no overlay
  appeared, and every window stayed restored. (Synthetic `keybd_event` cannot
  trigger registered hotkeys under task automation, so the direct message was
  used; with a managed monitor, id 1 toggled the strip and id 2 did not crash.)
- Zero-managed display change: broadcasting `WM_DISPLAYCHANGE` kept the same
  process ID, created no overlay, left every window restored, and left
  settings/workspace unchanged.
- Show desktop: with a managed monitor, `WM_HOTKEY` id 4 hid the strip and
  minimized staged windows; a second press restored them.
- Per-monitor overrides: `stripEdge:Right` and `autoHide:true` stored under
  `monitors`; the strip revealed only at x=1042/1043 on the right edge, was
  hidden with the pointer away and at the left edge, and `Use global` restored
  the global behavior.
- v2 smoke: clicking a tile staged a Paint window; dragging a staged window
  onto a tile returned it to the strip (stage 1→0); quitting restored every
  minimized window.
- Session lock/unlock was not exercised: SSH has no interactive account
  credentials. Treat it as a manual check.
- Multi-monitor behavior remains unverified (single-display VM).

- [x] **Step 4: Commit fixes only if any were needed**

No fixes were needed; there is no verification commit.

```bash
git add -A
git commit -m "fix: address monitor selection verification"
```

- [ ] **Step 5: Leave multi-monitor verification open**

The VM has one display. The feature is not complete until the spec's multi-monitor checklist passes on real hardware: excluding a secondary monitor, dragging a stage window onto it, fullscreen on it, show-desktop isolation, wallpaper/hotkey isolation, per-monitor overrides, and reconnect by device path.
