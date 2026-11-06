# StageManager4win rewrite — design

Status: draft for approval
Date: 2026-11-06
Decisions: Windows 11 only; independent per-monitor stages; one tile per window.

## 1. Intent

Build a Windows-native Stage Manager: one active stage of windows plus a strip of recent windows, with live previews and fast switching. This replaces the current 502×500 WinForms demo with a per-monitor overlay that manages other applications without injecting into them.

The macOS behaviour was reverse-engineered from `WindowManager.app` (macOS 27.0.1) with REA. Observed facts: the strip is a list of recent windows or app window sets; off-stage windows are live and scaled into the strip; the active stage is centred at about 75% of the display width; the strip is about 12% wide and vertically centred; settings include app grouping and per-app window presentation (`Automatically`, `One at a Time`, `All at Once`); there are toggle and cycle hotkeys. Exact transition curves, auto-add heuristics, and the `AppWindowGroupingBehavior` enum mapping were not decompiled.

This clone uses **one tile per window**, the equivalent of macOS “Group windows by application = off”. App ownership (`AppUserModelID`) is still tracked for activation policy, icons, exclusions, and accessibility names. One-tile-per-app grouping is a future option, not v1.

## 2. Goals

- Per-monitor strip of live previews, one tile per window.
- One independent active stage per monitor; the stage holds the active app’s window set according to `WindowMode`.
- Window-level MRU and activation.
- New window, foreground, minimise, close, move, and desktop-switch handling that keeps the model consistent.
- Toggle, cycle, and show-desktop hotkeys.
- Settings UI and best-effort persistence.
- Multi-monitor and per-monitor DPI.
- Accessibility: Narrator can discover and invoke strip tiles.
- Graceful fallbacks when capture is unavailable.

## 3. Non-goals / anti-goals

- One tile per app (window grouping) in v1.
- Moving or resizing other apps’ real windows into the strip. Windows apps resist this (minimum sizes, DPI, fullscreen, virtual desktops), and it risks breaking layouts. We render previews and activate windows instead.
- Mission Control, App Expose, Spaces, window tiling, screen sharing, widgets, or taskbar replacement.
- Multi-app stages, drag-to-stage, and wallpaper-click show desktop in v1 (phase 2).
- Pixel-perfect macOS animation parity.
- Capturing protected/DRM content, HDR tone mapping, or fullscreen-exclusive games.
- Injecting code into other processes.
- Hiding desktop icons or Windows widgets.
- An anti-goal of reliability: the app must never leave a window hidden, minimised, or unclickable because of a bug. Every state change has a recovery path.

## 4. Constraints

- Windows 11 22H2+ (build 22621+). TFM `net10.0-windows10.0.22621.0`; runtime check with a clear message on older builds.
- WGC window capture on Windows 11 draws a yellow capture border around each captured window. An unpackaged app cannot disable it (`IsBorderRequired` + restricted capability); the user can disable “Screenshot borders” in Windows Settings. Accepted v1 limitation.
- Minimised windows stop producing frames; keep the last frame and dim the tile.
- WGC frame-pool surfaces are clipped to the pool size, not scaled. Use a full-size pool per captured window and downscale in the shader.
- No Vortice, Win2D, or WinUI. All Win32/WinRT interop comes from CsWin32 (latest stable, 0.3.x; the repo currently has 0.2.164-beta).
- `Windows.Graphics.Capture` types come from the Windows SDK projection enabled by the TFM; CsWin32 generates `IGraphicsCaptureItemInterop`, `RoGetActivationFactory`, `IActivationFactory`, `CreateDirect3D11DeviceFromDXGIDevice`, and the DirectX/DComp entry points.
- WinForms stays as the shell. The overlay is a native HWND with a DirectComposition target, not a WinForms control.
- If CsWin32 misses or misgenerates a COM method, hand-write only that minimal `[ComImport]` interface method. No external wrapper library.

## 5. Chosen approach

1. **Capture:** Windows Graphics Capture (`GraphicsCaptureItem` via interop), one capture session per visible window tile.
2. **Presentation:** one borderless, topmost, no-activate, `WS_EX_NOREDIRECTIONBITMAP` overlay window per monitor, with a DirectComposition visual tree.
3. **Rendering:** D3D11 device; a tiny full-screen-triangle shader samples the captured BGRA texture and writes into a tile-sized `IDCompositionSurface`; DComp clips rounded corners and composites over the desktop with per-pixel alpha.
4. **Shell:** WinForms for settings, tray, hotkeys, and the message loop.
5. **Interop:** CsWin32 only.

Rejected alternatives: WGC + CPU readback (too slow, 10–15 fps), DWM thumbnails only (reliable but rectangular and no custom composition; kept as a fallback), WinUI 3/WPF shell (the overlay still needs native DComp; XAML buys nothing for a fully custom-drawn strip), Vortice (user prefers CsWin32).

## 6. Architecture

### 6.1 Threads

- **UI thread:** WinForms message loop, tray, settings, hotkey registration, WinEvent hooks, monitor/window enumeration, overlay HWND creation, UIA.
- **Render thread:** owns the D3D11 device/context, DComp device, capture sessions, frame queues, shader pipeline, and visual tree. Layout changes arrive as commands.
- **WGC callbacks:** `Direct3D11CaptureFramePool.CreateFreeThreaded` raises `FrameArrived` on worker threads. The callback only enqueues the latest frame per window; the render thread copies it and disposes it. Drop stale frames.
- **Synchronisation:** UI → render via a lock-protected command queue; render → UI only for errors/state that affect settings. No UI `Invoke` per frame.

### 6.2 Modules

- `WindowWatcher`: `EnumWindows` + `SetWinEventHook` (create, destroy, show, hide, cloak, uncloak, minimise start/end, foreground, move/size end, desktop switch). Debounced re-enumeration; monitor membership is recomputed on move/size end, not on every location change.
- `WindowClassifier`: candidate rules, AUMID resolution, shell/own-process exclusions.
- `WindowRegistry`: window-level MRU, app ownership, icons, exclusion list.
- `StageManager`: pure state machine for stage/strip transitions.
- `MonitorLayout`: per-monitor strip/stage geometry, DPI conversion, tile fitting.
- `CaptureManager`: create/recreate/dispose WGC sessions and frame pools.
- `OverlayRenderer`: D3D11 + DComp device, shader, surfaces, visuals, hit-test region, UIA provider.
- `SettingsStore`: JSON settings and persisted window order; atomic writes.
- `TrayController`: enable/disable, settings, exit.
- `HotkeyManager`: `RegisterHotKey` for toggle/cycle/show-desktop.

### 6.3 Window discovery and app ownership

Include only windows that a user would consider app windows:

- top-level, visible, not `WS_EX_TOOLWINDOW`, not `WS_EX_NOACTIVATE`, not topmost, not cloaked, non-zero size, not owned by another window (dialogs stay with their owner), not the shell (`GetShellWindow`, `Shell_TrayWnd`, `Progman`, `WorkerW`), not our own process.
- Exclude fullscreen windows from the strip; hide the overlay while a fullscreen app is foreground.
- Resolve AUMID with `SHGetPropertyStoreForWindow` + `PKEY_AppUserModel_ID`; fall back to the process image path, then the executable name. AUMID is used for app ownership, `WindowMode`, icons, exclusions, and accessibility names — **not** for grouping tiles.
- UWP: use the `ApplicationFrameWindow` AUMID; capture the frame window. If capture fails, fall back to an icon tile.
- Elevated windows: activation and capture may be blocked by UIPI. Skip capture and show a fallback tile; never silently lose the window from the model.
- Exclude apps in the user’s exclusion list.

### 6.4 State model

- `WindowRef`: HWND, PID, AUMID, app name, title, bounds, monitor, minimised/cloaked/fullscreen flags, last-active time.
- `MonitorState`: ordered `Stage` of windows, ordered `Strip` of windows (MRU), `Foreground` window (nullable), and `ActiveApp` (AUMID, nullable).
- A window belongs to exactly one monitor (the monitor containing its centre). Independent per-monitor stages; one active app per monitor.
- v1 stages contain windows from one app. The model allows multiple apps per stage so drag-to-stage can be added without a rewrite; v1 does not expose it.
- `WindowMode`:
  - `One at a Time`: stage = the foreground window only.
  - `All at Once`: stage = all visible windows of the foreground window’s app on that monitor.
  - `Automatic`: v1 policy is one at a time; revisit after observing real app behaviour.
- A window on stage is not in that monitor’s strip. Minimised windows are still in the strip and can be activated.

Transitions (applied to the monitor `M` that owns the affected window):

- Foreground window `W` on `M`:
  - `M.ActiveApp = W.App`; remove `W` from `M.Strip`.
  - `One at a Time`: `M.Stage = { W }`. Other visible windows of `W.App` on `M` go to the top of `M.Strip` (MRU order).
  - `All at Once`: `M.Stage = all visible windows of W.App on M`; remove them from `M.Strip`.
  - Previous stage windows not in the new stage move to the top of `M.Strip` in their previous MRU order.
  - Other monitors are unchanged.
- New window: if its app is `M.ActiveApp` and `All at Once`, add to the stage; otherwise add to the top of `M.Strip`. If it is foreground, run the foreground transition.
- Minimise: if the window is on the stage, remove it and add it to the top of `M.Strip`; if it is already in the strip, leave its position. If the stage becomes empty, leave `M.ActiveApp` until the next foreground event; if no foreground arrives, the stage stays empty.
- Close: remove the window; if it was foreground, wait for the next foreground event; otherwise just remove it. Remove the app from `ActiveApp` when it has no windows on `M`.
- Move to another monitor (on move end): remove from the old monitor’s stage/strip; if it was foreground, the old stage falls back to the top of its strip; add it to the new monitor as foreground (run the foreground transition) or to the new monitor’s strip.
- Click tile on `M`: activate that window; apply `WindowMode` for its app; the clicked window becomes foreground.
- Desktop switch: re-enumerate and rebuild state for the current virtual desktop; hide overlays during the switch. State is in memory per desktop, not persisted across desktops.
- Own overlay windows and tray windows are never candidates.

### 6.5 Layout

- Use the monitor work area. Strip edge is configurable (default left), width ≈ 12% of monitor width (clamp), tiles fit the available height with a 30 DIP gap; vertically centred under the work-area top.
- Tile size preserves the captured window aspect ratio; default content box about 160×120 DIP, scaled by DPI.
- Strip items are **windows**, one tile per window (the window’s live preview). No app grouping in v1.
- Overflow: the strip scrolls with the mouse wheel; all windows remain in the model and in cycle order, but only visible tiles are captured. `max strip items` caps the visible set, not the model.
- The active stage is not resized; Windows windows stay where they are. The “75% stage” is a macOS visual, not a Windows behaviour.
- Persist per-monitor window order by `(AUMID, title)`, with duplicates matched in order; prune missing windows on startup. Best-effort only, because window handles are not stable. Monitor identity is device name + bounds, with a fallback re-match by device name when bounds change.

### 6.6 Overlay and rendering

- Overlay HWND per monitor: `WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`, no `WS_EX_LAYERED`.
- `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`; tiles receive clicks without stealing focus.
- Hit testing: set the window region to the union of tile rectangles so empty strip areas are click-through across processes. `HTTRANSPARENT` alone is not reliable across processes. Rounded corners are visual only; the hit region may stay rectangular.
- DComp: target on the overlay HWND; root visual; one child visual per window tile.
- Per tile: `IDCompositionSurface` at tile pixel size; `IDCompositionRectangleClip` with rounded corners; `SetContent(surface)`; offset from layout.
- Capture frame → `ID3D11Texture2D`; create an SRV; render a full-screen triangle into the tile surface with the downscale shader; `EndDraw`; `Commit` once per batch.
- Shader: embedded HLSL, `D3DCompile` at startup, `SV_VertexID` triangle, linear sampler. No vertex buffer, no input layout, no D2D, no DirectWrite.
- Frame rate: cap at 30 fps (configurable 15/30/60). Drop frames when the render thread is busy.
- Minimised tiles: keep the last frame, set visual opacity to ~0.5.
- Hover: raise/scale the tile and emphasize it with relative opacity (hovered 1.0, others 0.92); restore on leave.
- Empty state: no tile.
- Capture failure: icon tile (extract the associated icon with `System.Drawing.Icon.ExtractAssociatedIcon`, then upload it as a texture) plus the window title as the accessible name. No text in the overlay.
- Device loss: tear down all sessions/surfaces, recreate the device, re-enumerate, and restart captures. Retry with backoff; never crash the UI thread.

### 6.7 Capture lifecycle

- Capture only the window tiles currently visible in the strip (max 5–8 by fit).
- On show: create `GraphicsCaptureItem` for the window, then a `Direct3D11CaptureFramePool` at `item.Size` with 2 buffers and `CreateFreeThreaded`, then a session.
- On frame: compare `frame.ContentSize` with the current size; if changed, recreate the pool and tile surface. Dispose the frame promptly.
- On hide/minimise/close: dispose the session/pool/surface. Keep the last preview bitmap for a fade-out if cheap; otherwise drop it.
- On auto-hide: stop sessions when the strip is fully hidden; restart on reveal (accept a short black/first-frame delay).
- On lock/RDP disconnect/fullscreen: stop all sessions and hide overlays; resume on unlock/foreground.
- Capture is best-effort: if `GraphicsCaptureSession.IsSupported()` is false, or item creation/capture fails, use icon tiles and log once.

### 6.8 Interaction

- Click tile: activate that window.
  - `One at a Time`: restore/raise the clicked window.
  - `All at Once`: restore all of the app’s windows on that monitor, then foreground the clicked window.
  - `Automatic`: v1 policy is one at a time.
- `WindowMode` is global in v1; per-app overrides are phase 2.
- Clicking the active stage’s window is normal Windows behaviour; the foreground hook updates the model.
- Toggle hotkey: show/hide the strip on the monitor under the pointer (fallback: foreground monitor).
- Cycle forward/back: take a snapshot of that monitor’s strip order at the start of a cycle, keep a cursor, and advance/wrap through the snapshot. Cycle activation is always one-at-a-time, regardless of `WindowMode`; skip windows that have closed or moved monitors. Foreground changes during the cycle do not reorder the snapshot; release it when the cycle ends (timeout or pointer move).
- Auto-hide: a 100 ms cursor poll per monitor; reveal when the pointer reaches the strip edge in DIPs, hide after a 500 ms delay when it leaves the strip.
- Show desktop: v1 uses a hotkey and `Shell.Application.ToggleDesktop()`; wallpaper-click detection is phase 2 because it requires shell hooks.
- Drag-to-stage from another app: phase 2. The v1 model must not assume one app per stage forever.
- No keyboard focus is taken by the overlay; UIA `Invoke` and global hotkeys are the keyboard paths.
- Default hotkeys: `Ctrl+Alt+Space` toggle, `Ctrl+Alt+Period` next, `Ctrl+Alt+Comma` previous, `Ctrl+Alt+D` show desktop. All configurable; avoid `Alt+Tab`, `Win+Tab`, `Win+D`, and `Win+Space`.

### 6.9 Settings and persistence

`%LOCALAPPDATA%\StageManager4win\settings.json`, written atomically (temp + rename), with a backup. Settings:

- enabled, window mode (global), auto-hide, strip edge, max visible strip items, preview fps.
- hotkeys: toggle, next, previous, show desktop.
- excluded AUMIDs.
- per-monitor persisted window order `(AUMID, title)` and the active app/window (state file separate from settings). Best-effort; prune missing windows.

On startup: re-enumerate, match persisted windows in order, append unknown windows by recency, prune missing. Never persist window handles. Do not show a “group by app” setting in v1.

### 6.10 Multi-monitor and DPI

- Per-monitor overlay, strip, stage, and active app. The foreground window’s monitor is the active monitor; other monitors keep their own stage and strip.
- A window belongs to exactly one monitor, so it never appears on two strips.
- Activation always targets the clicked tile’s window on that monitor. `All at Once` restores that app’s windows on that monitor only.
- Per-monitor DPI awareness v2 via the application manifest; handle `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, and monitor add/remove.
- Layout in DIPs; convert to physical pixels for surfaces and window regions. Verify DComp’s DPI mapping in the spike; if surfaces are treated as DIPs, compensate explicitly.
- Clamp the strip so it never overlaps the taskbar or leaves the work area.
- On hotplug, rebuild monitor states; prefer the persisted active window, then the top of the new strip.

### 6.11 Accessibility

- Custom UI Automation provider on the overlay HWND via `WM_GETOBJECT` + `UiaReturnRawElementProvider`.
- Root provider = strip; child providers = tiles. Each tile: `ControlType.Button`, name = “App — Window title”, `InvokePattern` activates the window, bounding rectangle = tile rect, `AutomationId` = AUMID + title.
- Narrator must announce tiles and activation. Test with Inspect and Narrator.
- While the strip is hidden, keep the overlay HWND alive with an empty hit-test region and invisible visuals; the UIA tree remains available. UIA focus or Invoke on a tile reveals the strip on that monitor. Do not `ShowWindow(SW_HIDE)` the overlay if it would remove the UIA tree.
- High contrast: draw a 1 px system-color border around tiles when high contrast is on; do not rely on custom colors alone.

### 6.12 Failure handling

- Window closed during activation/capture: catch, remove from the model, continue.
- `SetForegroundWindow` refused by Windows: call `SwitchToThisWindow`/`AttachThreadInput` fallback, then show a non-blocking notification.
- Explorer restart: re-enumerate; the shell window handle is gone.
- Display hotplug: recreate overlays and captures.
- Session lock/unlock: pause/resume.
- Capture border/DRM: fallback tile.
- HDR: v1 captures SDR BGRA8. If the source is HDR (`DXGI_COLOR_SPACE` / advanced color), use an icon fallback rather than showing washed-out colors; HDR tone mapping is out of scope.
- Crash: log to `%LOCALAPPDATA%\StageManager4win\logs`; never log full window titles by default.
- Settings corruption: fall back to defaults and keep the broken file as `.bak`.

## 7. Behaviour spec (v1)

- Each monitor has its own strip, stage, and active app.
- Strip lists recent off-stage **windows** on that monitor, ordered by recency, one tile per window.
- Windows on the monitor’s stage are not in its strip.
- Foreground change on a monitor applies `WindowMode` to the new app’s windows on that monitor; previous stage windows move to that monitor’s strip. Other monitors are unchanged.
- New windows: join the stage if their app is active and `All at Once`; otherwise join the strip.
- Minimise: the window moves from the stage (if present) to the strip.
- Clicking a strip tile restores/raises that window and applies `WindowMode`.
- Toggle shows/hides the strip on the pointer’s monitor; windows keep their state.
- Cycle hotkeys walk a frozen strip snapshot, one window at a time; they wrap and do not reorder the strip permanently.
- Overflow: the strip scrolls; all windows remain reachable by cycle and UIA.
- Auto-hide reveals on edge hover; peek scales the hovered tile.
- Show desktop calls the shell toggle; a second call restores.
- Fullscreen foreground hides the overlay and pauses capture.
- Virtual desktops are handled independently in memory; switching rebuilds state for the current desktop.
- Lock/unlock, display changes, and Explorer restarts are handled without a restart.

## 8. Testing

- Unit tests (new test project):
  - AUMID ownership, unknown-AUMID fallback, multi-process apps.
  - Window-level foreground/new/minimise/close/move transitions, per-monitor independence.
  - `One at a Time` vs `All at Once` stage membership.
  - Cycle snapshot, wrap, skip-closed, and no permanent reorder.
  - Overflow: model retains all windows while only visible tiles are captured/rendered.
  - Layout: tile fit, strip width clamp, DPI conversion, monitor selection, no taskbar overlap.
  - Settings round-trip, corrupted file recovery, persistence prune.
- Integration: `CaptureManager` behind an interface with a fake session; test create/recreate/dispose and failure paths.
- Manual matrix: Windows 11 22H2+; single/multi-monitor; 100/150/200% DPI; portrait; classic Win32, UWP, Chrome/Electron, Explorer, elevated app, fullscreen game, minimised, cloaked/virtual desktop, RDP, lock/unlock, Explorer restart, display hotplug.
- Accessibility: Inspect + Narrator; tile names and Invoke; hidden-strip reveal via UIA.
- Performance: 5/8/12 windows at 1080p/4K; CPU, GPU, memory, frame latency; capture border visibility.
- Recovery: kill a captured app, close during activation, unplug a monitor, corrupt settings.

## 9. Phases

0. **Spike (throwaway):** one window, one overlay, WGC → DComp, verify transparency, click-through region, DPI mapping, yellow border, frame rate, and CsWin32 generation of D3D11/DComp/WGC interop. Stop and reassess if the border or generation is a blocker.
1. **Foundation:** new project structure, TFM, CsWin32, D3D11/DComp overlay, window watcher/registry, window-level state machine, layout; static icon tiles.
2. **Behaviour:** foreground/new/minimise/close/move/hotkeys/activation; per-monitor independence; `WindowMode`.
3. **Live capture:** WGC sessions per window tile, shader downscale, frame lifecycle, fallbacks.
4. **Product:** settings, persistence, auto-hide/peek, show desktop, display/lock handling.
5. **Polish and hardening:** accessibility, animations, high contrast, tests, performance, recovery.

## 10. Open questions

None. The three product decisions are settled: Windows 11, independent per-monitor stages, one tile per window. Default hotkeys are a builder call and remain configurable.

## 11. Calls made by the builder

- Cycle order uses a frozen snapshot with a cursor; cycle activation is one-at-a-time; it wraps and does not permanently reorder the strip.
- Strip overflow scrolls; all windows stay in the model and in cycle order, but only visible tiles are captured.
- `WindowMode` is global in v1; `Automatic` maps to one-at-a-time; per-app overrides are phase 2.
- App grouping (one tile per app) is phase 2; v1 is always one tile per window.
- Virtual-desktop state is in memory only and rebuilt on switch; no cross-desktop activation.
- HDR is out of scope; HDR sources use an icon fallback.
- A hidden strip keeps its overlay HWND and UIA tree alive with an empty hit region; it is not `SW_HIDE`n.
- Window-to-monitor membership uses the window centre; a window belongs to one monitor.
- Wallpaper-click show desktop, tile labels, multi-app stages, and drag-to-stage are phase 2.
- The overlay hit region is the union of tile rectangles; rounded corners are visual only.
- v1 has no theme setting: the overlay has no chrome, only tiles with a fixed dark backdrop.
- Auto-hide polls the cursor every 100 ms instead of creating edge-trigger HWNDs; same behaviour with fewer topmost windows.
- Icon fallback uses `System.Drawing.Icon.ExtractAssociatedIcon`, not `SHGetFileInfo`.
- Hover uses scale plus relative opacity, not a shader brightness pass.
- Persistence is best-effort by `(AppId, title)` because window handles are not stable.
