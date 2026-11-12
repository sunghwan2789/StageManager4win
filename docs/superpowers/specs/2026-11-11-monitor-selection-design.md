# Monitor Selection and Per-Monitor View Settings — Design

Date: 2026-11-11
Status: approved

## Intent

Some monitors should be left completely alone. Today StageManager4win creates
a strip and a stage on every connected monitor and uses one global set of
settings. A monitor used for a game, a full-screen dashboard, or a second
person's desktop still gets an overlay, its off-stage windows are minimized,
and its wallpaper clicks toggle show-desktop for the whole machine.

The feature lets the user mark connected monitors as unmanaged. On an
unmanaged monitor the app shows nothing, touches no window, and ignores
pointer-scoped hotkeys and wallpaper clicks. The user can also choose a
different strip edge and auto-hide behavior for each managed monitor.

## Goals

- A per-monitor **Managed** switch. Unmanaged monitors have no overlay, no
  strip, no stage, no window minimization, no pointer-scoped hotkey response,
  and no wallpaper-click response.
- Per-monitor **Strip edge** and **Auto-hide** overrides, with "use global" as
  the default for each.
- Excluding a monitor at runtime restores any windows the app had minimized
  on that monitor and stops touching them immediately.
- Settings persist by monitor and survive restarts and reconnects.
- Existing single-monitor behavior is unchanged when no overrides exist.

## Non-goals

- Per-monitor window mode (`One at a Time` / `All at Once` / `Automatic`).
  It stays global.
- Per-monitor preview FPS. It stays global.
- Per-monitor hotkeys or excluded-app lists. They stay global.
- Following a physical monitor across ports or cable swaps. Settings follow
  the monitor connection (the PnP monitor device path), so they stay with the
  port rather than the panel. `StableMonitorId` is a possible later upgrade.
- Restoring a monitor's previous stage when it is re-included. A re-included
  monitor behaves like a freshly connected one: no saved stage is restored,
  and the normal foreground rule may stage its active app. Its remaining
  windows go to the strip.
- A first-run setup wizard or automatic "main monitor only" default. A
  monitor with no saved entry is managed.

## Approaches considered

### A. Per-monitor overrides (chosen)

Add a sparse list of monitor entries to `AppSettings`. Each entry carries
`managed`, an optional strip edge, and an optional auto-hide value. `null`
means "use the global setting". The coordinator resolves effective values per
monitor and filters the window model by managed monitors. The settings form
shows one row per connected monitor.

Small, keeps the core stage model untouched, and maps directly to the
request. Only the app settings, coordinator, overlay lifecycle, and settings
form change.

### B. Full per-monitor profiles

Every setting (window mode, FPS, hotkeys, excluded apps included) becomes
per-monitor. This requires a per-monitor mode in `StageModel`, per-monitor
frame pacing in `RenderThread`, and a much larger settings UI. Rejected:
the user asked for view settings, and this rewrites core behavior for no
stated need.

### C. Manage only the primary monitor

A single global "only manage the primary monitor" switch. Smallest change,
but it cannot select an arbitrary monitor and cannot vary the strip edge or
auto-hide per monitor. Rejected: it does not satisfy the request.

## Design

### Settings model

Add `MonitorSettings` to `StageManager4win.Core/Settings` (with
`using StripEdges = StageManager4win.Core.Layout.StripEdge;`):

```csharp
public sealed record MonitorSettings(
    string MonitorId,
    bool Managed = true,
    StripEdges? StripEdge = null,
    bool? AutoHide = null);
```

`StripEdge` is the existing enum from `StageManager4win.Core.Layout`.
`AppSettings` gains:

```csharp
public IReadOnlyList<MonitorSettings> Monitors { get; init; }
    = Array.Empty<MonitorSettings>();
```

Resolution helpers live on `AppSettings` so the behavior is unit-testable:

- `MonitorSettings? ForMonitor(string monitorId)` — ordinal-ignore-case lookup.
- `bool IsManaged(string monitorId)` — missing entry means `true`.
- `StripEdge EdgeFor(string monitorId)` — override or global `StripEdge`.
- `bool AutoHideFor(string monitorId)` — override or global `AutoHide`.

`Normalize()`:

- Replaces a null `Monitors` list with an empty list.
- Trims monitor IDs, drops blank IDs.
- Deduplicates by monitor ID (case-insensitive); the last entry wins.
- Replaces an undefined strip edge with `null` (use global).
- Leaves `Managed = false` entries intact; the list is sparse because
  default-valued entries are not required.

JSON shape (camelCase, string enums, written by `AtomicJsonFile`):

```json
"monitors": [
  {
    "monitorId": "\\\\?\\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018",
    "managed": true,
    "stripEdge": "Right",
    "autoHide": true
  },
  {
    "monitorId": "\\\\?\\DISPLAY#DELA06E#5&2e2fefea&0&UID1078019",
    "managed": false,
    "stripEdge": null,
    "autoHide": null
  }
]
```

An existing settings file without `monitors` deserializes to an empty list,
which means every monitor is managed and uses global view settings. No
migration code.

### Monitor identity

The persisted key is the monitor's PnP device interface path, not the GDI
display name. `MonitorDescriptor` becomes:

```csharp
public sealed record MonitorDescriptor(
    string Id,              // persisted key: monitor device path
    string GdiDeviceName,   // \\.\DISPLAYn, used for native calls and diagnostics
    string Name,            // EDID model name, display only
    bool IsPrimary,
    WindowBounds Bounds,
    WindowBounds WorkArea,
    uint Dpi);
```

`DisplayManager` keeps enumerating with `EnumDisplayMonitors` and
`GetMonitorInfo`. For each monitor it then calls
`EnumDisplayDevices(gdiDeviceName, 0, ..., EDD_GET_DEVICE_INTERFACE_NAME)` and
reads `DeviceID` and `DeviceString`. The trailing interface-class GUID
(`#{e6f07b5f-...}`) is stripped from `DeviceID`; `Id` is the remaining path,
for example `\\?\DISPLAY#DELA06E#5&2e2fefea&0&UID1078018`. When `DeviceID` is
empty, `Id` falls back to `GdiDeviceName`. A small core helper builds this key
from `(deviceId, gdiDeviceName)` so the normalization is unit-testable.
`IsPrimary` comes from `MONITORINFOF_PRIMARY`.

Add `EnumDisplayDevices`, `DISPLAY_DEVICEW`, `EDD_GET_DEVICE_INTERFACE_NAME`,
and `MONITORINFOF_PRIMARY` to `NativeMethods.txt`. Enumerating adapters with a
null device returns nothing on the VM, while enumerating by the known
`DISPLAYn` name works, so query each monitor by its GDI name.

Identity properties:

- Device paths are unique per physical monitor and port, and survive reboots,
  sleep/wake, and Windows display reordering. PowerToys uses the same value
  (with the interface GUID stripped) as its monitor ID.
- Moving a monitor to another port produces a new path, so settings stay with
  the port. The user accepted this. `DisplayTarget.StableMonitorId` was
  considered for panel-following but is not used now: it adds a WinRT
  `DisplayManager` dependency and has its own exceptions for identical
  monitors. It can later become the primary key with the device path as a
  fallback.
- The name comes from EDID and is normally the model name (for example
  `DELL U2720Q`). A virtual or non-PnP display can report a generic name such
  as `Generic Non-PnP Monitor` (confirmed on the VM), and two identical
  monitors report the same name. Names are display labels only, not keys.
- VM check: `EnumDisplayDevices("\\.\DISPLAY1", 0, ...)` returned
  `\\?\DISPLAY#Default_Monitor#4&1801d19c&0&UID0#{...}` with
  `Generic Non-PnP Monitor` as the name. `StableMonitorId` was available too
  (`NOEDID_...`), and its target's `DeviceInterfacePath` matched this path.

### Window model filtering

`StageCoordinator.Enumerate()` keeps enumerating with all monitors (so a
window gets its real monitor assignment) and then filters out snapshots whose
`MonitorId` is not managed.

To keep a manually minimized window from leaking onto a managed monitor, use
the window's restore bounds when assigning a minimized window to a monitor.
Add a core helper:

```csharp
MonitorTopology.IdForWindow(monitors, bounds, restoreBounds, minimized)
```

which picks `restoreBounds` when `minimized` is true. `WindowSource.Describe`
uses it. The snapshot's `Bounds` stay the real icon-rail rectangle; only the
monitor choice changes. This also removes the monitor-change promotion that
minimized windows currently cause.

### Effective per-monitor view settings

Every place that reads `AppSettings.StripEdge` or `AppSettings.AutoHide` for a
specific monitor uses the resolver instead:

- `Refresh()` layout and edge reveal zone use `EdgeFor(monitor.Id)`.
- Strip visibility uses `AutoHideFor(monitor.Id)`.
- `GetStripArea(monitorId)` returns null when the monitor is unmanaged or has
  effective auto-hide off, so `AutoHideController` never reveals or hides it.
- When an auto-hide override changes, clear `_visibleByMonitor` so managed
  strips return to the default visible state.

Global `Enabled` remains the master switch. A monitor is active only when
`Enabled` is true and the monitor is managed.

### Overlay and render lifecycle

Create overlays and render targets only for managed monitors. `Start()`,
`StartRenderThread()`, and the display-change teardown all use the managed
list. An unmanaged monitor has no overlay window, so it cannot intercept
clicks or draw tiles.

When `ApplySettings` detects a change to the managed set, it rebuilds the
overlay/render targets for the new set without reloading the workspace. A
display-topology change still performs the existing full rebuild and
workspace load; it now creates targets only for managed monitors.

With zero managed monitors there are no overlay windows. `RunOnUiThread`
currently marshals through `_overlays.Values.FirstOrDefault()` and would
silently drop display-change, session-switch, and device-lost callbacks.
Capture the UI `SynchronizationContext` in the coordinator constructor
(`SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext()`)
and post through it instead. `Refresh()` may keep its early return when no
overlays exist; `Reconcile()` and overlay creation still run when a display
is later connected or re-included.

`ApplySettings` also performs the exclusion transition before rebuilding:

1. For each newly excluded monitor, find model windows with that monitor ID.
2. Restore windows tracked in `_hiddenByApp` with
   `WindowActivation.RestoreNoActivate`, and remove them from that set.
3. If show-desktop is active, drop that monitor from `_desktopStage` and
   restore its saved windows in the same way so none are stranded. If
   `_desktopStage` becomes empty, clear `_desktopShown` and
   `_desktopForeground` as well, so a later re-include cannot leave every
   strip hidden behind a stale show-desktop state.
4. Remove those windows from the stage model and `_lastAspect`.
5. Clear per-monitor coordinator state: strip areas, visibility, scroll
   offsets/targets, max scroll, DPI, and any active cycle cursor.
6. Save the workspace and refresh.

For newly included monitors, the rebuild creates the overlay and
`Reconcile()` applies the normal fresh-start rule: the current foreground app
on that monitor may be staged, and the remaining windows go to the strip.
The existing hide timer minimizes off-stage windows as usual.

### Hotkeys, wallpaper clicks, show desktop, fullscreen

- `MonitorUnderPointer()` returns null when the pointer is over an unmanaged
  monitor. Toggle and cycle then do nothing. If the pointer is not over any
  monitor, the foreground window's monitor is used only when managed.
- `ShowDesktop` is a global action: it snapshots, minimizes, and restores
  only managed monitors, regardless of where the pointer is. A deliberate
  show-desktop hotkey still works when the pointer happens to rest on an
  unmanaged monitor. `_desktopStage` and the restore path filter by managed
  status. If excluding monitors leaves `_desktopStage` empty, clear
  `_desktopShown` and `_desktopForeground`.
- `DesktopClickWatcher` gains a managed-point predicate. A wallpaper click
  on an unmanaged monitor is ignored before the overlay/desktop/icon checks.
  The predicate reads the current settings, so it follows re-inclusion.
- Fullscreen pause is gated on the fullscreen foreground window's monitor
  being managed. A fullscreen game on the unmanaged monitor does not pause
  the managed strips. If the fullscreen window is on a managed monitor, the
  existing global pause behavior stays.

### Settings form

`SettingsForm` gains a "Monitors" section above the global settings. It
receives the connected monitor list from the coordinator.

Columns: Monitor, Managed, Strip edge, Auto-hide.

- Monitor label: `DELL U2720Q — 2560×1440` plus `(primary)` when applicable.
  When the name is blank or starts with `Generic`, use `Display N` instead.
  `N` is parsed from `GdiDeviceName`; the resolution comes from bounds.
- Managed: checkbox.
- Strip edge: `Use global`, `Left`, `Right`.
- Auto-hide: `Use global`, `On`, `Off`.

Controls remain enabled for unmanaged monitors so the user can preconfigure
them; the values only take effect when the monitor is managed.

`BuildResult` starts from the existing list, replaces entries for connected
monitors, and keeps entries for disconnected monitors untouched. A connected
monitor whose values all equal the defaults (`Managed = true`, both overrides
null) gets no entry, keeping the file sparse.

`TrayApplicationContext` passes `_coordinator.Monitors` (a read-only snapshot
of the current monitor list) when opening the form. The form is reopened to
see a newly connected monitor; there is no live refresh while it is open.

### Persistence and reconnect

- Stored entries for disconnected monitors are retained, so reconnecting the
  same monitor to the same port restores its settings. Moving it to another
  port produces a new device path and therefore a fresh default entry.
- A monitor connected for the first time has no entry: managed, global edge,
  global auto-hide.
- Workspace save/load only sees managed monitors because the model excludes
  the others. Saved workspace entries for an unmanaged monitor are ignored
  on load and are dropped from the next save.
- Workspace and settings both use the device path. No migration is needed:
  the monitor list is new in settings, and any pre-change workspace file is
  from an unreleased build.

## Testing and verification

### Unit tests

- Settings normalization: blank/duplicate monitor IDs, undefined edge,
  null list, and last-entry-wins.
- Resolver defaults and overrides for `IsManaged`, `EdgeFor`, `AutoHideFor`.
- Settings JSON round-trip with monitor entries.
- Monitor key helper: strips the trailing interface GUID, falls back to the
  GDI name when the device path is empty, and keeps a valid path otherwise.
- `MonitorTopology.IdForWindow` uses restore bounds only when minimized.

### Manual, single monitor (Windows VM)

- Uncheck the only monitor: strip disappears, app-minimized windows are
  restored, toggle/cycle hotkeys do nothing, wallpaper clicks do nothing,
  and settings persist across restart.
- Recheck it: the strip returns. The active app on that monitor may be staged
  by the normal foreground rule; the rest return to the strip.
- With every monitor unmanaged, a display-settings change and a session
  lock/unlock do not crash or strand state. The settings form still reports
  the connected monitor and the monitor can be managed again.
- Show-desktop while the only monitor is unmanaged: the hotkey does nothing
  visible, and re-managing the monitor leaves a visible strip.
- Per-monitor edge and auto-hide overrides behave like the global settings.
- Existing v2 checklist still passes on the managed monitor.

### Manual, multiple monitors (real hardware)

- Exclude the secondary monitor: no strip/overlay, its windows are never
  minimized by the app, and a fullscreen game there does not pause the
  primary strip.
- Drag a stage window from the primary to the excluded monitor: it stays
  visible and leaves the model.
- Show desktop on the primary leaves the excluded monitor untouched.
- Excluding a monitor while show-desktop is active restores that monitor's
  windows; excluding the last managed monitor clears show-desktop so the next
  re-include shows the strip.
- Wallpaper clicks on the excluded monitor do nothing; toggle/cycle hotkeys
  there do nothing; the show-desktop hotkey still acts on managed monitors.
- Per-monitor edge and auto-hide differ correctly.
- Restart and reconnect to the same port: settings persist by the monitor
  device path. Moving the monitor to another port gives it a fresh entry.

The development VM has a single display. Multi-monitor behavior must be
verified on real hardware before the feature is called complete.

## Risks

- Device-path identity is per monitor and port, not per panel. A cable swap
  moves settings with the port.
- Device paths can be empty on unusual virtual displays; the GDI-name
  fallback then inherits display-number renumbering.
- Monitor names come from EDID: identical monitors share a name, and virtual
  displays report a generic one. The display number stays visible in the
  fallback and the name is not used as a key.
- Multi-monitor behavior cannot be fully verified on the single-display VM.
  The VM can only confirm the generic-name fallback, not a real model name.
- A window spanning two monitors is assigned by its center, unchanged from
  today; such a window can be treated as unmanaged if its center sits on the
  excluded monitor.
- Event plumbing with zero managed monitors changes from overlay-based
  marshaling to a captured `SynchronizationContext`; this path needs a
  manual lock/unlock and display-change check.

## Decisions left to the builder

- Exact control sizes and spacing in the Monitors section.
- Whether to use a `GroupBox` or a bordered section for the monitor table.
- Whether the primary label comes from `DisplayManager` or is derived in the
  form (the design expects `MonitorDescriptor.IsPrimary`).
- Test file names and test-data helpers.
- Implementation order; suggested: settings model and tests, monitor lookup
  helper, coordinator filtering and lifecycle, settings form, VM checks.
