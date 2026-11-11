# StageManager4win v2 — design

Status: draft for approval
Date: 2026-11-08
Decisions: one tile per app, drawn as a small stack of that app's windows; multi-app stages; drag a tile off the strip (or Shift-click it) to add it to the stage; drag an on-stage window onto the strip (or minimize it) to remove it; clicking the wallpaper toggles show desktop.

## 1. Intent

v1 shipped window-level stages: one tile per window, one app's windows on the stage at a time. v2 closes the gaps the user picked:

- the strip shows one tile per app, drawn like the macOS stack: the front window live, up to two windows behind it, offset and dimmed (no twist inside a stack)
- each app tile carries a slight alternating lean, and the app icon badge sits at the tile's bottom-left
- a stage can hold several apps
- dragging a tile onto the desktop, or Shift-clicking it, adds its app to the stage
- dragging an on-stage window onto the strip removes it, and minimizing has the same effect
- clicking the wallpaper toggles show desktop

The strip keeps the v1 meaning: **it lists windows that are outside the stage.** The stage is not drawn; it is the set of windows the strip excludes.

### macOS evidence (no decompilation)

From `WindowManager.app` strings, class names, and the bundle's localization tables:

- There is **no app-level "Remove from Stage" UI**. The complete user-facing string set of WindowManager.app is `Desktop`, `Desktop %@`, `Fullscreen`, `Window`, `Share This Window`, `Currently Sharing This Window`, `No Available Windows`. Dock's tables contain no stage-removal item either. Accessibility actions are only `AXExpose*`.
- Removal is a **window drag**: `StageManagerOnStageWindowDragModifier`, `StageManagerStripStageWindowDragModifierComponent`, `StageManagerWindowDragSettleTransitionModifierComponent`, `removeFromStage`, `removeWindowFromUserSet window=`, gated by `dragMustMoveToStage`, `forceMoveToStage`, `hasMouseCrossedStageToStripMinimumXDistance`.
- Add and remove are two directions of the same drag: `addToStage` / `addWindowToStage` versus `removeFromStage`. The `Settle` component suggests the decision is made on release.
- Wallpaper click uses `notifyClickDidEndOnDesktopBackgroundWindow:` and the `EnableStandardClickToShowDesktop` preference.

The binary is stripped, so call graphs and the exact distance threshold are not established. The clone uses the same shape with its own threshold.

Because this app never moves other processes' windows, the drag-to-strip gesture is applied to the real window (detected, not moved), and its effect is a minimize — the macOS window shrinking into the strip.

## 2. Goals

- One strip tile per app, drawn as a stack of that app's off-stage windows.
- A per-monitor stage that can contain windows from several apps.
- Drag a strip tile off the strip, or Shift-click it: add that app to the stage and activate it.
- Drag an on-stage window onto the strip tiles: remove it from the stage (it minimizes).
- Minimize a window by any means: remove it from the stage (already true in v1).
- Click the desktop wallpaper: toggle show desktop.
- Keep every existing v1 behaviour: hover, scroll, auto-hide, cycle hotkeys, UIA, persistence, capture.
- Keep the stage rule simple: clicking an app outside the stage replaces the stage; dragging or Shift-clicking adds to it.

## 3. Non-goals / anti-goals

- Moving or resizing other apps' windows into the strip (unchanged from v1).
- A "Remove from Stage" menu or button. The gesture and minimize cover it.
- A drawn stage, stage chips, or a second row of tiles.
- Animation parity with macOS; the stack is static, not a physics fan.
- Tiling, Mission Control, virtual desktops.
- Per-app grouping overrides; grouping is always on in v2.
- Making the strip a file/space list: it stays a window list grouped by app.

Anti-goal: a drag must never strand a window in a state the user cannot exit. A dropped tile that fails to activate is rolled back and logged.

## 4. Constraints

- Windows 11 22H2+ (build 22621+), .NET 10, CsWin32 only, no new dependencies.
- The overlay is a full-monitor, topmost, no-activate window whose window region is clipped to the tiles. Drag feedback must fit that surface (expand the region during a drag; no new window).
- Input must stay responsive: the low-level mouse hook only records points and posts; it never calls Shell or blocks.
- No global keyboard hooks; the existing hotkey path is unchanged.
- Windows may refuse foreground activation; the existing rollback path stays.
- A window drag (`EVENT_SYSTEM_MOVESIZE*`) must not be confused with a WGC capture, a resize, Aero Snap, or a tile drag.

## 5. Chosen approach

Keep **windows as the unit of state**, and group them by app only when building the strip and the stack. The stage is a window list whose entries may span apps.

Rejected alternatives:

- **App-level state model** (stage = set of AppIds, windows derived): cleaner on paper, but rewrites `StageModel`, `MonitorState`, persistence, and every v1 test, and loses the per-window MRU the strip order depends on.
- **Explicit per-app window lists on the stage**: same rewrite cost with no extra capability.

The chosen approach keeps `StageModel`'s public shape, adds two operations, and changes one rule inside `SetForeground`.

## 6. Architecture deltas

### 6.1 Stage model (`Core/Stages/StageModel.cs`)

New rule in `SetForeground(window)`:

1. If no window of `window.AppId` is on the stage, the stage is **replaced** with that app's contribution.
2. If the app is already on the stage, **only that app's contribution is swapped**; other apps' windows stay on the stage. This covers Alt-Tabbing between two stage apps, and clicking another window of an app that already has a window on stage.

`ApplyForeground` becomes `RebuildStage(monitor, stageApps, foreground)`: `stage = union over stage apps of (OneAtATime: the foreground window of that app; AllAtOnce: all of that app's visible, non-minimized windows)`. `Automatic` keeps behaving as it does today (same as One at a Time).

A passive foreground change to an app outside the stage **replaces** the stage. That is the Stage Manager rule, not a bug: switching to an unrelated app switches the stage. Shift-click and drag are how the user keeps apps together.

`Add(window)` joins the stage (AllAtOnce) when **any** window of that app is already on the stage, not only when `ActiveAppId` matches.

`SetMode` rebuilds the stage per stage app: switching to AllAtOnce expands every stage app; switching to One at a Time collapses each stage app to its foreground window.

New operations:

- `AddToStage(WindowId window)`: the representative window is always added (and restored if minimized); in AllAtOnce, that app's other non-minimized windows join too. Minimized siblings stay in the strip. If the app already has a window on stage, this is the same as a click (swap that app's contribution).
- `RemoveFromStage(WindowId window)`: remove that window from the stage and insert it at the head of the strip. Used when a window-drag lands on the strip; the coordinator then minimizes the real window.

`Minimize(window)` already removes from the stage; no change. `MonitorState` keeps its shape.

### 6.2 The strip group and its stack (`StageCoordinator.Refresh`)

Walk `state.Strip` once and group by `AppId`, ordered by each app's first occurrence (the strip is MRU-ordered). Each group yields:

- **front**: the first window of the app in strip order — the representative
- **back 1, back 2**: the next two windows of that app, if any

The group is emitted as up to three `TileVisual`s sharing one rect: the layout rect of the front window. `TileVisual` gains `int StackIndex` (0 front, 1 and 2 back) and `int AppWindowCount`. Back visuals are capture sources too, so the renderer creates a surface for each and the existing capture loop starts a session for each; no capture-set change is needed. Windows beyond the third still exist and surface as earlier ones move.

The strip scrolls as one column. `StripLayout` computes `ContentHeight` and `MaxScroll`; when the content fits, the column is centered and the wheel is ignored, so tiles never disappear. When it overflows, `scrollOffset` shifts every tile, and `OverlayWindow` clips both drawing and its window region to `StripBounds`. `StageCoordinator` animates the offset toward a target (64 DIP per wheel notch, 15 ms tick, 0.35 approach), so a wheel burst glides instead of jumping. There is no fixed visible-item cap; `MaxVisibleStripItems` is gone.

An app with a window on the stage still gets a strip tile if it has another window off-stage (One at a Time, multi-window app). Clicking it swaps that app's stage contribution, so no window becomes unreachable; repeated clicks cycle that app's windows in One at a Time.

`StripLayout` gains `StripBounds` (the full strip column in DIP) for auto-hide reveal and window-drag targeting. The tile rects stay as they are.

### 6.3 Overlay input: click, Shift-click, drag (`Overlay/OverlayWindow.cs`)

Activation moves from button-down to button-up so a drag can be told from a click:

- `WM_LBUTTONDOWN`: hit-test the front/stack rect; remember `(window, startPoint, shift)`; `SetCapture`; do **not** activate. Shift is read here, not on release, so letting go of Shift mid-click does not change the intent.
- `WM_MOUSEMOVE` while captured and moved more than 4 DIPs: raise `DragMoved(point)` with the monitor-local point.
- `WM_LBUTTONUP`: release capture. If the total movement is at most 4 DIPs and the point is still inside the tile, raise `TileDropped` when Shift was held (add to stage) or `TileClicked` otherwise (activate). If it moved more and the point is outside the strip column, raise `TileDropped`. Inside the strip column, cancel.
- `WM_CAPTURECHANGED`: cancel the drag.
- Auto-hide is suppressed from drag start to drop/cancel.

The overlay is already full-monitor, so no new window or hook is needed for tile drags. Drops only land on the source monitor; dragging off the monitor cancels.

Hit testing uses the tile rect expanded by `TileStack.HitBounds` (20 DIP up, 8 DIP down, 28 DIP per side), so the peeking back cards are part of the target. The 30 DIP layout gap keeps adjacent groups from overlapping.

### 6.4 Rendering: the stack and the ghost

Each group is drawn back-to-front in the existing visual list:

| card | offset (DIP) | scale | rotation | dim |
|---|---|---|---|---|
| back 2 | -28, -8 | 0.94 | none | 0.8 |
| back 1 | -14, -4 | 0.97 | none | 0.9 |
| front | 0, 0 | 1.0 | none | 1.0 |

Same-app cards only recede: offset left and up, smaller, dimmer, no twist. The perspective belongs to the tile position, not the app index. Each tile is a trapezoid whose far (right) edge is shorter and more transparent than the near (left) edge: the pixel shader scales the far edge to 0.86 and fades it to 0.72 alpha while the left edge keeps full height and opacity. Tiles above the strip's vertical center lean the far edge down, tiles below lean it up, fanning away from the middle like the macOS example; `TileStack.TiltSlope` returns that vertical shear (`TiltShiftRatio = 0.08`). The shear is applied with `IDCompositionVisual.SetTransform` to a card visual inside a container visual, so the rectangle clip leans with the card and the icon badge stays upright. `TilePainter` gains dim, far-scale, and far-alpha constants: the pixel shader multiplies the sampled colour by them (`1.0` for the front, the table values for the back). The front keeps the existing border; back cards draw no border.

Only the front card carries the app icon badge: width 30% of the tile width clamped to 28–52 DIP, inset 6 DIP, corner radius 8 DIP, painted from the app icon by `TilePainter`.

Minimized strip windows stay fully opaque so a live-looking preview is not washed out; hovering dims the non-hovered neighbours to 0.92. `OverlayTarget.Update` rebuilds the child list in reverse emission order because DirectComposition places each newly added child in front; otherwise the back cards land above their front card.

The ghost reuses the group: `RenderCommand.UpdateDragGhost(monitorId, window, pointDip)` draws the same cards at the pointer, clamped to the monitor. While a ghost exists, `OverlayWindow.UpdateRegion` unions the strip rects with the stack rect of the dragged group; the region returns to normal on drop/cancel.

### 6.5 Window drag onto the strip = remove

`WindowEventWatcher` hooks `EVENT_SYSTEM_MOVESIZESTART` and `EVENT_SYSTEM_MOVESIZEEND`.

- Start, for a window on the stage: remember the window, its monitor, and its size; suppress auto-hide.
- During the move, when the cursor enters that monitor's `StripBounds`, force the strip visible (keep reveal, skip hide).
- End: remove only when **all** hold — the window was on the stage, the monitor did not change, the **width and height are unchanged** (within 2 px), and the cursor is over a strip tile (not the gap or the edge).
  - The size test is the Aero Snap and resize discriminator: snapping to a half or quarter changes the size, and resizing changes it, so neither triggers removal.
  - The tile test keeps a drop in the outer margin from firing.
- On match: call `RemoveFromStage` and `ShowWindow(SW_MINIMIZE)`. The normal minimize path then reconciles the model.
- Release auto-hide suppression on end, cancel, or window disappearance.

Using the cursor position (not the window rectangle) makes the test independent of window size and of the overlay covering the strip.

### 6.6 Wallpaper click toggles show desktop

A `WH_MOUSE_LL` hook on the UI thread, installed only while the app is enabled:

- `WM_LBUTTONDOWN`: if the point is over the desktop, record the point and time.
- `WM_LBUTTONUP`: if the point moved at most 4 DIPs and less than 500 ms elapsed, **post** the decision to the UI thread and clear. Always return `CallNextHookEx`: the click still reaches the desktop.
- The decision runs off the hook: `IsWallpaper(point)` uses `AccessibleObjectFromPoint` (MSAA). It is the wallpaper when the hit is the desktop view or list view itself and not a list item; over an icon it returns the item, so the click is ignored. If the call fails, do not toggle.
  - `LVM_HITTEST` is not used: it is above `WM_USER`, pointer-bearing messages are not marshalled cross-process, and sending it to Explorer is unsafe.
- Ignore the taskbar, our own overlay, and any point inside a strip tile.
- Toggling while the desktop is already shown restores the windows, matching the macOS click-again behaviour.
- The toggle does not call `Shell.Application.ToggleDesktop`, which is a no-op on current Windows 11 builds. The first click snapshots each monitor's stage, minimizes its windows with `SW_MINIMIZE`, and hides the strip; the second click restores the stage and windows, then re-activates the previous foreground window.

The hook never swallows input and never runs Shell or COM work inside the callback.

### 6.7 Strip scrolling

The overlay is `WS_EX_NOACTIVATE`, so it never receives `WM_MOUSEWHEEL`; a `WH_MOUSE_LL` hook (`Input/StripWheelWatcher`) forwards each wheel delta with the cursor point to the UI thread and always calls `CallNextHookEx`, so the wheel still reaches the window under the pointer.

`OnStripWheel` resolves the monitor whose tile stack contains the point, requires `MaxScroll > 0`, then adds `delta / 120 * 64 DIP` to the target. `OnScrollTick` moves the offset 35% of the remaining distance every 15 ms and stops within 0.5 DIP. Off-strip wheels change nothing.

### 6.8 Persistence

`MonitorWorkspace` gains `IReadOnlyList<WindowIdentity>? Stage`. Save/restore includes it. A v1 workspace file without the field deserializes as `null`, which the loader maps to an empty list; the restored `Foreground` then stages that app exactly as v1 did. No migration code.

### 6.9 Accessibility

Group tiles stay `Button` elements keyed by the representative window. The name becomes `"App — title"`, or `"App — N windows"` when the group holds more than one off-stage window; the automation id stays `AppId|Title` of the representative. The bounds are the stack rect. Invoking a tile runs the click path (replace or swap).

Because UIA `Invoke` cannot carry Shift, each group exposes a second invokable child, `"Add <App> to the stage"`, with the same bounds. It runs the `TileDropped` path so keyboard and screen-reader users can build a multi-app stage. Removal needs no UIA action: minimize is reachable from every window's system menu.

## 7. Behaviour spec

| Input | Before | v2 |
|---|---|---|
| Click a strip tile | activate that window | activate that app: replace the stage, or swap only that app's window if it already has one on stage; repeated clicks cycle the app's windows in One at a Time |
| Shift-click a strip tile | nothing | add that app to the stage and activate it (same as a drop) |
| Press a strip tile and drag off the strip | nothing | the group follows the pointer; release adds the app to the stage and activates it |
| Press a strip tile, drag, release inside the strip | nothing | cancel, no state change |
| Drag an on-stage window's title bar onto a strip tile | window stays where dropped | window minimizes, leaves the stage, appears at the head of the strip |
| Drag a window to the screen edge to snap, or resize it | snap/resize | unchanged (the size test rejects it) |
| Minimize an on-stage window | leaves the stage | unchanged |
| Click the wallpaper | nothing | toggle show desktop |
| Click a desktop icon | selects it | unchanged |
| Drag on the wallpaper (rubber band) | selects | unchanged |

Hover, scroll, auto-hide, cycle hotkeys, and UIA invoke keep their v1 behaviour.

## 8. Testing

Core (unit, extend `StageModelTests`):

- foreground change to an app already on the stage keeps the other apps' windows
- foreground change to an outside app replaces the stage
- `AddToStage` unions, restores a minimized representative, and leaves minimized siblings in the strip
- `AddToStage` for an app already on stage swaps instead of duplicating
- `RemoveFromStage` removes one window and heads the strip
- One at a Time versus All at Once contributions under a multi-app stage
- `Add` joins the stage when any window of the app is staged
- `SetMode` expands and collapses every stage app
- minimize still removes, and removing the foreground clears it
- workspace round-trip with a stage; a v1 file loads with an empty stage

Layout (unit): `StripBounds` for both edges; grouping order and cycle order; overflow detection, scroll clamping at both ends, and centered content when it fits.

Interaction helpers (unit): drag threshold, drop-inside/outside, and the removal predicate (on stage, same monitor, same size, cursor on a tile).

Manual checks: stack drawing and z-order, icon badge, per-app lean, ghost and region, drop outside adds, Shift-click adds, drop inside cancels, window drag to a tile minimises, window drag to the edge snaps instead, minimize removes, wallpaper click toggles, icon click does not, auto-hide stays visible during a drag, wheel scrolls and clips with both ends clamped, UIA names and the add child.

## 9. Phases

1. Core model and tests (`SetForeground` rule, `AddToStage`, `RemoveFromStage`, `Add`, `SetMode`).
2. Strip grouping, group-aware scroll, `AppWindowCount`, UIA names and add child.
3. `StripBounds` and the overlay click-on-up, drag, and ghost (uses 2).
4. Window drag onto the strip (uses 3 for `StripBounds` and the tile rects).
5. Wallpaper click hook (independent).
6. Workspace persistence and settings wiring (uses 1).

Phases 3 to 5 are independent of each other once 1 and 2 land. Each phase leaves the app shippable.

## 10. Open questions

None blocking. The stack offsets, the drop tolerance, and the ghost scale are builder calls, recorded below.

## 11. Calls made by the builder

- Windows remain the state unit; grouping happens at render time.
- The stack is emitted as up to three `TileVisual`s with `StackIndex`; the front carries the hit target, the stack rect carries the hit area.
- Activation moves to button-up with a 4 DIP click threshold.
- Shift-click and a drop share one path (`TileDropped` → `AddToStage`); Shift is sampled at button-down.
- Removing by drag ends in a real minimize; the minimize path already updates the model.
- The window-drag test requires an unchanged window size, which excludes Aero Snap and resizes.
- The drop target is a strip tile, not the whole strip column; the column only drives reveal.
- Wallpaper detection uses MSAA on the UI thread and never `LVM_HITTEST`.
- Grouping is always on; no new setting, and the fixed visible-item cap is gone. The strip scrolls whenever its content overflows.
- Back cards are live captures darkened in the shader; no new capture type.
- Same-app cards do not twist; the alternating lean is per app tile so the strip reads as separate apps.
- Tiles stay opaque even while minimized; only back cards and hovered-out neighbours dim.
- Wheel input comes from a low-level hook because the no-activate overlay is never the focus window.
- No new dependency, no new window, no new thread.
