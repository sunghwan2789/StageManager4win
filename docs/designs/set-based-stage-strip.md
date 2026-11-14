# StageManager4win: Set-Based Stage and Strip

* **Author:** Sunghwan Bang
* **Status:** Proposed
* **Date:** 2026-11-14

--------------------------------------------------------------------------------

## Overview

StageManager4win is a local Windows window manager that keeps the active task on the stage and moves inactive windows to a strip at the screen edge. Today the app is the unit of that model: one strip tile represents one application, and the stage holds that application's windows.

This design changes the unit to a set. A set is a group of windows the user treats as one task on one monitor. It can contain several windows of one app or windows from several apps. Activating a window outside the current set creates a new set and moves the previous set to the strip as one tile. The tile shows every window in the set as a fanned card stack, with the active window in front.

The target user is one person working on a local Windows desktop with several apps and windows. The value is one return point per task, fewer strip entries, and a strip that does not block the window the user is working in. Set membership and window positions live in memory for the life of the process; restart persistence is not part of this design.

## The Problem

The current model groups by application, but users switch between tasks. A task can span a browser, an editor, and a terminal, while one application can hold windows from unrelated tasks. App grouping therefore splits one task across several tiles and merges unrelated windows under one tile.

The current strip also has interaction defects that the set model would inherit if it were built on the same code:

- Activation reorders the strip under the pointer. A click can land on a different app than the one the user aimed at.
- The strip renders at most three cards per group and exposes only the front card to pointer and accessibility input. The fourth and later windows are unreachable.
- The overlay is topmost for the whole monitor. It can intercept a click intended for a modal dialog's Save button.
- A tile is created after its windows are minimized, so it falls back to an app icon instead of a preview.
- The strip has no per-set position memory. Returning to a task does not restore the window arrangement.

The proposed solution is a set model with a stable strip order, one tile per set, an in-memory position snapshot per set, and an overlay that sits behind the stage windows until the user reaches the screen edge.

## Goals

- Make the set the unit placed on the stage. Support both a single app with several windows and several apps in one set.
- Create a set automatically when the user activates a window outside the current set. Move the previous set to the strip as one tile. Switch to an existing set when one of its windows is activated.
- Render one strip tile per set. Show every member as a fanned card stack with a shared middle y coordinate. The active card is in front and extends furthest at the top, bottom, and inner edge.
- Use the union of all card previews as the tile bounds. Keep a shared baseline and equal gaps across one-window and multi-window tiles. Mirror the fan on a right-edge strip.
- Show a badge at 28 DIP at rest. Grow the badge to 36 DIP when the badge itself is hovered. Anchor it to the tile's bottom-left for a left strip and bottom-right for a right strip. Show multi-app badges side by side.
- Open a set listing centered on the tile when the badge is clicked. Each row shows a small app badge, the window title, and the app name. Selecting a row activates that window.
- Support tile click, repeated-click cycling, back-card click, and Shift-click to add a window to the current set. Keep strip order stable and append new tiles.
- Keep the strip behind the stage windows by default. Reveal it at the 2 DIP screen edge, keep it visible during window moves, and hide it fully when 20 DIP or less remains exposed.
- Remove tilt. Reveal the strip with a straight slide from the edge.
- Remove the auto-hide setting and its toggle hotkey.
- Remember each set's window positions in memory across set switches. Center only the window that starts a set, and only when it has no saved position. Apply monitor, DPI, and off-screen clamping on restore.
- Make every window in every set reachable by pointer, keyboard, accessibility, and the badge listing.
- Give each goal a measurable acceptance check in the verification plan.

## Non-Goals

- Persisting sets or window geometry across a StageManager4win restart. In-memory state is sufficient for this design.
- Cloud sync, user accounts, multi-user state, or any network service.
- Moving or resizing other applications' windows beyond the set-start center placement and the set restore.
- Manual set naming, merging, or a set editor.
- Dropping set members to reduce preview cost. Capture policy changes; membership does not.
- Tilt, shear, or decorative perspective.
- Keeping the auto-hide setting, its per-monitor override, or its toggle hotkey.
- New third-party dependencies.
- Changing the supported Windows versions.

## Open Questions

- **Window destroy signal:** `WindowEventWatcher` currently observes create and hide events, not destroy. Does it need a new `EVENT_OBJECT_DESTROY` hook, or can membership be validated on the next reconcile? The design prefers the destroy hook; the implementation must confirm it is available for elevated and cloaked windows.
- **Bottom z-order behavior:** does `SetWindowPos(HWND_BOTTOM)` keep the overlay above the desktop and below normal windows across virtual desktops and always-on-top clients? A small probe is required before the occluded state is considered proven.
- **Accessibility pass-through:** does an empty window region plus `WS_EX_TRANSPARENT` remove the overlay from UIA hit-testing and click routing on the supported Windows versions? This must be verified with a modal dialog and a screen reader.
- **Extended frame bounds:** does `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` return usable bounds for elevated, cloaked, and UWP windows, or is a `GetWindowRect` fallback required? The exposure rule needs one reliable source.

## Critical User Journeys (CUJs)

### CUJ 1: Start a set from an unassigned window

- **Given:** A monitor has one active set and one strip set.
- **When:** The user activates a window that belongs to neither set.
- **Then:** A new set is created for that window, the previous active set becomes one strip tile appended to the strip, and the new window is centered only if it has no saved geometry.

### CUJ 2: Return to a set

- **Given:** A set is on the strip with saved member positions.
- **When:** The user clicks its tile.
- **Then:** The current set moves to the strip, the clicked set becomes active, its members are restored and clamped, and its saved foreground window is activated.

### CUJ 3: Add a window to the current set

- **Given:** A set is active and another window is visible or on the strip.
- **When:** The user Shift-clicks that window.
- **Then:** The window moves into the active set, leaves its previous set, an emptied previous set is deleted, and the active set's tile shows the new member.

### CUJ 4: Open the set listing from the badge

- **Given:** A set tile is visible in the revealed strip.
- **When:** The user hovers and clicks the badge.
- **Then:** A listing centered on the tile shows one row per member with a small app badge, title, and app name. It scrolls when the rows do not fit. Selecting a row activates that window; an outside click or a second badge click closes the listing. Escape also closes it when the overlay has keyboard focus.

### CUJ 5: Reveal and hide the strip while a window covers it

- **Given:** The active window covers the strip column and 20 DIP or less remains exposed.
- **When:** The user moves the pointer to the 2 DIP screen edge.
- **Then:** The strip slides straight in front of the active window with no tilt. After the pointer leaves the strip and its 30 DIP margin for 500 ms, the strip hides fully if 20 DIP or less is exposed; otherwise it returns behind the windows.

### CUJ 6: Move a window without losing the strip

- **Given:** The strip is visible.
- **When:** The user moves a window across the strip.
- **Then:** The strip stays visible during the move, and the hide decision is evaluated only after the move ends.

### CUJ 7: Close a member

- **Given:** A set has several members.
- **When:** One member closes.
- **Then:** It is removed from the set, its geometry is discarded, and an empty set is deleted. If the active set becomes empty, the next strip set is promoted.

### CUJ 8: Minimize a member

- **Given:** A set has several members.
- **When:** The user minimizes one member.
- **Then:** The member leaves the set, same as a close. The set keeps its other members, an emptied set is deleted, and an emptied active set promotes the next strip set.

## Technical Plan (Architecture)

### State model

Replace the flat `Stage` and `Strip` lists with ordered set records per monitor. The stage is the active set; the strip is the ordered list of the remaining sets.

```text
StageSet
  Id: string
  Members: ordered WindowId list, back card first, front card last
  Foreground: WindowId
  Geometry: map WindowId -> WindowGeometry

WindowGeometry
  BoundsDip: WindowBounds
  MonitorId: string
  Dpi: uint
  IsMaximized: bool
  RestoreBoundsDip: WindowBounds | null

MonitorState
  MonitorId: string
  ActiveSet: StageSet | null
  StripSets: ordered StageSet list
```

Invariants:

- A window belongs to at most one set on one monitor.
- `Foreground` is a member.
- `Members` contains no duplicates.
- The last member is the front card. When the foreground changes, that member moves to the end.
- A set with no members is deleted.
- A set id is unique for the process lifetime and is never reused.

`WindowId` is the native handle and is stable only while the window exists. Membership lookups also compare the snapshot's `ProcessId` and validate that the handle still exists, so a recycled handle cannot inherit a stale set membership.

### Transitions

All membership changes go through one transition surface on `StageModel`. The coordinator supplies the activation source; passive foreground changes do not create sets.

```text
Activate(window, source)
ShiftAdd(window)
Remove(window, reason)
SwitchToSet(setId)
OnMonitorChanged(window)
```

Transition rules:

- **Activate a member of the active set:** update `Foreground`, move that member to the end of `Members`, and refresh.
- **Activate a member of a strip set:** snapshot the active set, remove the target set from `StripSets`, promote it to `ActiveSet`, append the demoted set to `StripSets`, restore the promoted set's geometry, and activate its `Foreground`.
- **Activate an unassigned window:** create a singleton set. If the window has no saved geometry, center it in its monitor's work area. Demote the current active set and append it to the strip.
- **Shift-add a window:** remove it from its previous set, delete that set if empty, append it to the active set, set it as foreground, and snapshot its geometry.
- **Remove a member:** remove it from the set and delete the set if empty. If the active set becomes empty, promote the first strip set.
- **Minimize a member:** remove it from the set, same as `Remove`. Delete the set if it becomes empty. If the active set becomes empty, promote the first strip set. A minimized window is not retained as a card.
- **Close a member:** remove it and delete the set if empty.
- **Monitor change:** remove the window from its old set, create or append to a set on the destination monitor, and delete an emptied old set.
- **Show desktop, session lock, fullscreen pause:** suppress set creation and preserve the current sets. Show desktop uses the existing temporary stage snapshot; it does not create or delete sets.

The coordinator attributes activation before calling `Activate`:

- Input within a short window before the foreground change, or a click, hotkey, or UIA invoke from this app, is user-initiated.
- Restoration and programmatic activation set a suppression flag that the reconcile path checks.
- Owner, tool, popup, and modal windows do not create sets. They remain associated with the foreground set and do not trigger a demotion.
- Activations that arrive within a short interval are coalesced into one transition.
- A churn ceiling logs and keeps the current set when the same monitor exceeds a small number of set creations per minute.

`WindowMode` remains the creation policy. `AllAtOnce` adds every visible, non-minimized window of the activated app to the new set. `OneAtATime` adds only the activated window.

### Strip order and hit capture

- `StripSets` is ordered. A demoted set appends to the end. A promoted set is removed. No other set changes relative order.
- The pointer-down handler captures the set id under the pointer before any layout runs. The click uses that captured id, not a fresh hit test after the transition.
- A new tile appends; the activated tile is removed. A second click at the same point may hit the next tile, which is expected because the first tile is gone.
- The existing `CycleCursor` walks the active set's members first, then every member of each strip set in strip order, front card first.
- The strip receives one tile per `StageSet`. `StripGrouping` no longer groups by app id.

### Tile geometry

`TileStack` computes the fan from the foreground card's aspect, clamped to the existing `MaxTileWidth` and `MaxTileHeight`.

```text
n = members.Count
cardW, cardH = size from foreground aspect
step = n == 1 ? 0 : min(14, max(4, (300 - cardW) / (n - 1)))
unionW = cardW + (n - 1) * step
unionH = cardH
middleY = unionTop + unionH / 2
```

For a left strip, card `i` (zero is the back card) has:

```text
scale = max(0.86, 0.97 ^ (n - 1 - i))
left = unionLeft + i * step
top = middleY - scale * cardH / 2
```

The front card has `i = n - 1` and scale 1.0. For a right strip, replace `left` with `right = unionRight - i * step` and anchor the scale at the right edge. All cards share `middleY`.

- The tile baseline is the union's left edge for a left strip and the union's right edge for a right strip.
- The gap between union rectangles is 30 DIP.
- The badge anchors at the union's bottom-left or bottom-right with a 6 DIP inset.
- The tile hit area is the union rectangle expanded by 20 DIP on top, 8 DIP on the bottom, and 28 DIP on each side. Card rectangles are tested front-last. The badge and listing are tested before the tile.
- Hover scales the card stack to 1.03. The badge cluster is a separate root visual and is not scaled by card hover.
- The badge grows from 28 DIP to 36 DIP when the badge itself is hovered, over 120 ms. Multi-app badges sit side by side, front app first, 4 DIP apart, with a 4 DIP overlap when the row is too wide. Show four app badges plus a `+N` chip when the set has five or more distinct apps.

### Overlay and input state machine

The overlay remains one full-monitor, no-activate window. Its state is explicit:

| State    | Z-order             | Region                              | Input                 | Accessibility         |
| -------- | ------------------- | ----------------------------------- | --------------------- | --------------------- |
| Hidden   | bottom, non-topmost | empty                               | pass-through          | none                  |
| Occluded | bottom, non-topmost | interactive bounds                  | pass-through          | none                  |
| Revealed | topmost             | interactive bounds                  | enabled               | enabled               |
| Shielded | topmost             | interactive bounds minus modal rect | enabled outside modal | enabled outside modal |

- Hidden sets root opacity to 0 and applies an empty region.
- Occluded sets opacity to 0.85, applies the interactive region, and adds `WS_EX_TRANSPARENT`.
- Revealed removes `WS_EX_TRANSPARENT` and applies the interactive region.
- Shielded subtracts the foreground window's modal rectangle from the region. The rest of the strip remains interactive. The modal rectangle is found with `GetLastActivePopup` and the owner's enabled state; the code does not rely on the dialog class.
- The foreground frame comes from `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, with `GetWindowRect` as a fallback.
- For a left strip, exposed width is `max(0, foregroundLeft - stripLeft)`. For a right strip, it is `max(0, stripRight - foregroundRight)`.
- The reveal edge is 2 DIP. The strip stays revealed while the pointer is within the strip, its tiles, or a 30 DIP margin. After 500 ms outside, it returns to occluded when more than 20 DIP is exposed and to hidden when 20 DIP or less is exposed.
- Window moves set a suppression flag. The hide evaluation runs once when the move ends.
- A 100 ms poll samples the cursor and the foreground frame. Move and resize events update the frame immediately.
- The set listing is drawn in the same overlay visual tree. While it is open, the window region expands to the listing bounds and the listing takes input before the tile. If the rows exceed the available height, the wheel scrolls the rows and the UIA off-screen flags follow the visible clip.

### Position memory

- Geometry is stored in memory per set and per window. No disk schema is added.
- Snapshot on move or resize end and set switch. Removing a member for any reason discards its geometry; minimizing or closing does not save it.
- Restore scales by the DPI difference, chooses the monitor with the largest intersection when the saved monitor is gone, keeps the title bar inside the work area, and skips windows that no longer exist.
- A monitor or DPI change clamps the in-memory rectangles immediately.
- Initial center placement applies only to the window that starts a set, only when it has no saved geometry, and never to maximized, fullscreen, minimized, or elevated windows. It uses `SetWindowPos` without activation or z-order change, suppresses reconciliation, re-reads the result, and keeps the current geometry on failure.
- On startup, the existing flat workspace restore runs unchanged. `StageModel.Reset` turns the restored stage list into one active set and each restored strip window into a singleton set in strip order. No set or geometry data is read from disk.

### Capture and rendering

- One tile visual per strip set. Card visuals are children of the tile; the badge cluster is a separate root child so card hover scaling does not deform it.
- Live capture is limited to the front three cards. Older cards use the last cached frame. Back cards refresh at 5 frames per second; the front card uses the normal render rate.
- Capture stops while the strip is hidden or occluded.
- The last captured frame is retained when a set is demoted; no one-shot capture round trip is added. A card that never had a frame shows a deliberate placeholder with the app icon and member count. A user-minimized member leaves the set and is not rendered as a card.
- The tile visual tree is rebuilt only when set membership, geometry, or layout changes. Hover and badge growth use transforms, not a full rebuild.

### Accessibility

- One set button per tile with automation id `set|{SetId}` and a name containing the foreground app, window count, and app count.
- One card button per member with automation id `window|{hwnd}` and union-relative bounds.
- One badge button with automation id `setbadge|{SetId}` that opens the listing.
- One button per listing row with the app badge, title, and app name.
- `IsOffscreen` is computed from the current region and scroll position.
- The Next and Previous hotkeys cycle through every active-set member and then the strip fronts. UIA invoke drives the same paths as click, badge, add, and listing row. The overlay does not become focusable.
- Hidden and occluded states expose no accessibility subtree.

### Component map and data flow

```text
Activation / hotkey / UIA
          |
          v
StageCoordinator  --->  StageModel transition  --->  MonitorState
          |                                              |
          |                                              +--> ActiveSet
          |                                              +--> StripSets
          v
StripLayoutCalculator / TileStack  --->  OverlayTarget  --->  OverlayWindow
          |                                   |
          |                                   +--> TileVisual + cards + badge
          v
TileUiaProvider / StripUiaProvider
```

Core changes:

- New `StageSet` and `WindowGeometry` records under `src/StageManager4win.Core/Stages/`.
- `MonitorState` carries `ActiveSet` and `StripSets`.
- `StageModel` owns the transition surface and membership invariants.
- `StripGrouping` is replaced by set-keyed grouping; `StripGroup` carries a set id.
- `TileStack`, `StripLayoutCalculator`, `StripLayoutOptions`, `TilePlacement`, and `StripItem` become set-aware. `CycleCursor` stays generic and consumes `StageModel.CycleOrder`.
- Tests cover membership invariants, activation transitions, strip order, geometry, and hit targets.

App changes:

- `StageCoordinator` attributes activation, drives transitions, restores geometry, and owns the suppression flag.
- `OverlayWindow` implements the four states, region changes, z-order changes, and input transparency.
- `OverlayTarget`, `TileVisual`, and `RenderCommand` render fanned cards, the badge cluster, and the listing.
- `AutoHideController` gains the exposure predicate and move suppression. The auto-hide setting is removed.
- `TileUiaProvider`, `StripUiaProvider`, and `AddToStageUiaProvider` expose sets, cards, badges, and listing rows.
- `WindowActivation`, `WindowEventWatcher`, and `SettingsForm` gain the restore, destroy, and settings changes.
- `AppSettings` and `MonitorSettings` drop `AutoHide`; `SettingsStore` ignores old keys.

## Alternatives Considered

### Alternative A: Keep one tile per app and only fix MRU reordering

- **Pros:** Smallest change. Keeps the existing grouping, layout, persistence, and tests. The strip reorder fix alone removes the highest-severity click defect.
- **Cons:** Does not represent a task that spans several apps. Still merges unrelated windows of one app. Does not provide a task-level return point or per-set position memory.
- **Reason for Rejection:** Rejected by author: the set, not the app, is the unit placed on the stage. The MRU fix remains a prerequisite, but it is not sufficient.

### Alternative B: Use the stage as the only state and derive sets at render time

- **Pros:** No new set aggregate. Uses the existing stage and strip lists. Less state to migrate and fewer invariants.
- **Cons:** Set membership disappears when a set leaves the stage, so a composite set cannot be restored. The strip can only regroup by app id or window order. Per-window set positions cannot survive a switch.
- **Reason for Rejection:** Rejected: the design requires membership and positions to survive set switches. It remains a fallback for a smaller first release.

### Alternative C: Add a second set store beside the existing workspace

- **Pros:** Keeps the existing stage model untouched. Sets can be added incrementally.
- **Cons:** Two sources of membership and restore state. The coordinator must keep the flat lists and the set store synchronized. Duplicate identity and save logic.
- **Reason for Rejection:** Rejected: sets extend `MonitorState` in memory; a parallel store would duplicate membership and restore logic.

### Alternative D: Two overlay windows, one behind and one topmost

- **Pros:** Each window has one z-order role. The behind window can pass input through while the topmost window handles reveal.
- **Cons:** Two input paths, two accessibility trees, and two visibility state machines. The windows can disagree during transitions and modal shielding.
- **Reason for Rejection:** Rejected: one overlay with hidden, occluded, revealed, and shielded states avoids duplicated input and accessibility behavior.

### Alternative E: Use the front-card rectangle as the tile

- **Pros:** Matches the current layout. Scroll, hit-testing, and badge anchoring stay close to today's code.
- **Cons:** The tile does not contain the whole set. Back cards overflow the layout rect. The badge anchors to the front window rather than the tile. Adjacent tiles can overlap unpredictably.
- **Reason for Rejection:** Rejected by author: the tile bounds must be the union of all previews, even though that changes layout, scroll, hit-testing, and the badge anchor.

### Alternative F: Persist sets and geometry across restart

- **Pros:** A relaunch returns the user to the previous task arrangement. Supports long-lived workspaces.
- **Cons:** Requires a versioned schema, a stable window identity, a restore clamp, a synchronous exit flush, and migration from the flat workspace. The identity problem is not solved by the current app id and title.
- **Reason for Rejection:** Rejected by author: in-memory state is sufficient. The existing flat workspace restore remains unchanged and is not extended.

### Alternative G: Offset cards down and to the right

- **Pros:** Produces an explicit diagonal staircase with a simple position offset per card.
- **Cons:** Conflicts with the author's shared middle y. The tile's vertical bounds grow with card count. Back cards move away from the badge anchor and the hit area.
- **Reason for Rejection:** Rejected by author: all cards share a true middle y. The staircase comes from the horizontal step and the per-depth scale of the back cards.

### Alternative H: Cap rendered cards and show an overflow count

- **Pros:** Bounds capture cost and tile size. Keeps the tile legible for large sets.
- **Cons:** The tile no longer shows every member. The overflow count adds a second interaction path and a new accessibility element. Members beyond the cap are less discoverable.
- **Reason for Rejection:** Rejected: every set member stays visible as a card. Cost is controlled by limiting live capture to the front three cards and compressing the horizontal step.

### Alternative I: Keep auto-hide as an option

- **Pros:** Users who dislike edge reveal can keep the strip visible. Preserves the existing setting and hotkey.
- **Cons:** Two visibility models in one overlay. The occlusion rule and the user setting can disagree. More settings, migration, and test combinations.
- **Reason for Rejection:** Rejected by author: edge reveal is always enabled and the toggle hotkey is removed.

### Alternative J: Keep the strip always on top

- **Pros:** The strip is always reachable and never hidden by a window. Simpler z-order and input model.
- **Cons:** The strip covers window content and can intercept clicks intended for a modal dialog. It forces the user to work around the overlay.
- **Reason for Rejection:** Rejected by author: the strip sits behind the windows by default and reveals at the edge.

### Alternative K: Choose ordered sets per monitor versus flat windows with tags

- **Pros:** Ordered sets give one authoritative membership model and clear invariants. Flat windows with tags keep the initial diff smaller.
- **Cons:** Ordered sets rewrite the stage model and its tests. Flat tags create two sources of truth for stage order and membership.
- **Reason for Rejection:** The author chose ordered sets per monitor: one authoritative model is worth the rewrite, and the flat-tag model fails when a window moves or closes and the tag and list disagree.

## Detailed Implementation Plan

### Implementation corrections

Five details need to be made concrete before implementation. They refine, rather than change, the approved architecture.

1. **Membership carries the process id.** A bare `WindowId` is a native handle that Windows can recycle. `StageSet` therefore stores `WindowKey(WindowId, ProcessId)` for every member and for geometry. A lookup only matches when both parts match the live snapshot.
2. **A member is removed on minimize only when it belongs to the active set.** Demoting a set minimizes its windows; those windows are already members of a strip set and must keep their membership. The minimize-equals-remove rule applies only when the minimized window belongs to the active set.
3. **The strip region is the union of interactive bounds, not the whole strip column.** The region covers set hit bounds, the badge cluster, and the open listing, clipped to the strip column. `WS_EX_TRANSPARENT` handles click-through while occluded.
4. **Keyboard reachability is the existing cycle hotkeys plus UIA invoke, not Tab focus.** The overlay stays `WS_EX_NOACTIVATE`; it must not steal focus from the active window. Next and Previous cycle through every window in the active set and then the strip sets. UIA exposes set, card, badge, add, and listing-row invoke actions.
5. **The one-shot capture before demotion is dropped.** The active set's front cards were captured at the normal rate, and the last DComp surface survives minimize. A card that never had a frame keeps the deliberate placeholder. This avoids a synchronous render-thread round trip on the UI thread.

### Implementation order and checkpoints

Work in five phases, with a build and test checkpoint after each.

1. **Core state.** `WindowKey`, `WindowGeometry`, `StageSet`, `MonitorState`, `StageModel`, `WorkspaceRestore`. Checkpoint: core tests pass with the new membership and transition rules.
2. **Core layout.** `TileStack`, `StripLayoutCalculator`, `StripLayoutOptions`, `StripItem`, `TilePlacement`, `TileHitTest`, `StripGroup`. Checkpoint: layout and hit-test tests pass without any App changes.
3. **Overlay surface.** `OverlayWindow`, `TileVisual`, `OverlayTarget`, `RenderCommand`, `RenderThread`, `AutoHideController`, `WindowEventWatcher`, `WindowSource`, `WindowActivation`, `NativeMethods.txt`. Checkpoint: a probe confirms bottom z-order and click-through, then the App builds with the four presentation states.
4. **Coordinator.** `StageCoordinator` activation attribution, set transitions, strip order, geometry save/restore, capture policy, workspace conversion. Checkpoint: manual CUJs 1, 2, 3, 6, 7, and 8 pass.
5. **Accessibility and settings.** `SetUiaProvider`, `ActionUiaProvider`, `TileUiaProvider`, `StripUiaProvider`, `AddToStageUiaProvider`, `SettingsForm`, `HotkeyManager`, `TrayApplicationContext`. Checkpoint: full manual verification, including screen reader and legacy settings.

Do not reorder phases 1 and 2. The coordinator cannot be written cleanly until the set aggregate and the set-tile layout exist.

### Core state types

```text
WindowKey
  Window: WindowId
  ProcessId: int

WindowGeometry
  BoundsDip: RectD
  MonitorId: string
  Dpi: uint
  IsMaximized: bool
  RestoreBoundsDip: RectD?

StageSet
  Id: string
  Members: ordered WindowKey list, back card first, front card last
  Foreground: WindowKey
  Geometry: map WindowKey -> WindowGeometry
  Front => Members[^1]

MonitorState
  MonitorId: string
  ActiveSet: StageSet?
  StripSets: ordered StageSet list
```

`StageSet` is a record. `StageModel` replaces records rather than mutating lists in place, so a projection handed to the renderer is a stable snapshot. The set id is generated by a process-lifetime counter on the UI thread, for example `set-000001`; ids are never reused.

`WindowGeometry` stores DIP, not pixels. Conversion happens at the coordinator boundary with the current monitor DPI. `RestoreBoundsDip` is populated only for maximized windows and is used to restore the normal rectangle before maximizing again.

### StageModel transition surface

```text
Reset(windows, foreground)
Add(snapshot)
Update(snapshot) -> MonitorChange?
Remove(window, reason)
SetForeground(window)                  // passive; never creates a set
Activate(window, source) -> ActivationResult
ShiftAdd(window) -> ActivationResult
SwitchToSet(setId) -> ActivationResult
OnMonitorChanged(window) -> ActivationResult
ApplyWorkspace(monitorId, stage, strip, foreground)
CycleOrder(monitorId) -> IReadOnlyList<WindowId>
```

```text
ActivationSource: User, Restore, Programmatic, Passive
RemoveReason: Closed, Minimized, ShiftAdded, MonitorChanged
ActivationResult: Changed, CreatedSet, PromotedSetId, DemotedSetId, Failure
```

Rules:

- `Activate` with `User` is the only path that creates a set for an unassigned window. `Restore` and `Programmatic` switch to a set only when the window is already a member. `Passive` only updates the active set's foreground when the window is already the foreground member; otherwise it is a no-op.
- Activating a member of the active set updates `Foreground` and moves that member to the end of `Members`. Activating a member of a strip set promotes that set, demotes the current active set, and appends the demoted set to the end of `StripSets`.
- `ShiftAdd` targets the active set on the window's monitor. If that monitor has no active set, the window becomes a singleton active set. The window is removed from its previous set first; an emptied previous set is deleted.
- `Remove` deletes the set when the last member leaves. If the active set empties, the first strip set is promoted. Geometry for the removed key is discarded.
- `OnMonitorChanged` removes the window from its old set, deletes an emptied old set, promotes the old monitor's next strip set when needed, and appends the window to the destination monitor's active set or creates a singleton set there.
- `SetForeground` is used by reconcile for passive foreground changes. It never creates, promotes, or demotes a set.
- `SetMode` only changes the policy for future set creation. It does not regroup existing sets; this is a deliberate change from the current `SetMode` behavior.
- `AllAtOnce` applies only when a new set is created. It adds the activated window plus every visible, non-minimized window of the same app on that monitor. Those windows are removed from their previous sets, and emptied sets are deleted. `OneAtATime` adds only the activated window. `Automatic` continues to behave as `OneAtATime`; no new heuristic is added.
- `ApplyWorkspace` replaces one monitor's sets with one active set built from the saved stage list and one singleton strip set per saved strip window. `Foreground` selects the active set's front. This is the only startup conversion from flat workspace data.
- `CycleOrder` returns the active set's members front-first, then every member of each strip set in strip order, front card first. The existing `CycleCursor` consumes this flat list unchanged.
- `Reset` seeds one active set for the supplied foreground, plus `AllAtOnce` siblings when that mode applies, and one singleton strip set for every other window in input order. With no foreground, every window is a singleton strip set and there is no active set.

### Activation attribution in StageCoordinator

The coordinator is the only caller that decides whether a foreground change is user-initiated.

- `WindowEventWatcher` raises `Foreground` for every top-level foreground change, including owned dialogs. The coordinator ignores any window that is not in `_model.Windows`; `WindowFilter` already excludes owned, tool, topmost, cloaked, fullscreen, and own-process windows.
- A foreground event records `_foregroundCandidate` and a timestamp. The existing 100 ms debounce coalesces bursts; the latest candidate wins.
- `_suppressActivation` is a counter, not a bool. It is raised around restore, programmatic activation, center placement, show desktop, session pause, and fullscreen pause. While raised, reconcile calls `SetForeground` only, never `Activate(User)`.
- With suppression clear, a foreground candidate that is a managed window is user-initiated. Reconcile calls `Activate(window, User)`. This covers taskbar, Alt-Tab, click, and hotkey activation without a global input hook.
- If `Activate` fails, the coordinator logs the window and reason, keeps the previous model state, calls `Reconcile`, and reports the existing status message.
- The churn guard keeps a queue of set-creation timestamps per monitor. Above eight creations in sixty seconds, the coordinator logs, appends a singleton set to the strip without demoting the active set, and leaves the current task on stage. This keeps the window reachable without allowing a foreground loop to empty the stage.

### Strip order and hit capture

- `StripSets` is the only strip order. A demoted set appends; a promoted set is removed. No other set changes relative order.
- `OverlayWindow` captures the `HitTarget` on `WM_LBUTTONDOWN`, before any transition. The `WM_LBUTTONUP` path uses the captured target, not a fresh hit test after the layout changed.
- A new tile appends and the activated tile is removed. A second click at the same point may hit the next tile; that is expected.
- Scrolling is per monitor and moves whole set unions. The scroll offset is clamped to the layout's `MaxScroll`.
- Drag-to-add keeps the existing gesture: a drag ending outside the strip on the same monitor adds the dragged window to the current set; a drop inside the strip cancels. The ghost uses the union hit bounds.

### Fan layout and hit targets

`TileStack` owns the fan math. It no longer exposes `TiltSlope` or any shear value.

```text
n = members.Count
cardW, cardH = foreground aspect clamped to MaxTileWidth / MaxTileHeight
step = n == 1 ? 0 : min(14, max(4, (300 - cardW) / (n - 1)))
unionW = cardW + (n - 1) * step
unionH = cardH
middleY = unionTop + unionH / 2
```

For a left strip, card `i` (zero is the back card) has:

```text
scale = max(0.86, 0.97 ^ (n - 1 - i))
left = unionLeft + i * step
top = middleY - scale * cardH / 2
```

The front card is `i = n - 1`, scale 1.0. A right strip mirrors the horizontal step and anchors the fan at the union's right edge; the shared `middleY` is unchanged. All cards share the same true middle y.

`TilePlacement` carries:

```text
SetId
UnionBoundsDip
Cards: ordered CardPlacement list, back to front
BadgeBoundsDip
HitBoundsDip
```

`CardPlacement` carries `WindowKey`, `BoundsDip`, `StackIndex`, and `Scale`. `TileHitTest` returns a `HitTarget`:

```text
Kind: Card, Badge, ListingRow, SetBackground
SetId: string
Window: WindowKey?
RowIndex: int?
```

Hit order is listing rows, badge cluster, cards front-to-back, then the union hit bounds. The listing is tested first because it can overlap a neighbouring tile. The badge is tested before the card because it sits inside the union but is a separate action.

`TileStack.HitBounds(union)` expands 20 DIP on top, 8 DIP on the bottom, and 28 DIP on each side. The badge cluster is included in the union's hit bounds. Hover growth is covered by a 2.5% region padding.

### Overlay presentation and input

`OverlayWindow` owns one explicit presentation state per monitor:

| State    | Z-order             | Region                              | Input                 | Accessibility         |
| -------- | ------------------- | ----------------------------------- | --------------------- | --------------------- |
| Hidden   | not topmost, bottom | empty                               | pass-through          | none                  |
| Occluded | not topmost, bottom | interactive bounds                  | `WS_EX_TRANSPARENT`   | none                  |
| Revealed | topmost             | interactive bounds                  | enabled               | enabled               |
| Shielded | topmost             | interactive bounds minus modal rect | enabled outside modal | enabled outside modal |

- Hidden sets root opacity to 0 and applies an empty region.
- Occluded sets opacity to 0.85, applies the interactive region, and adds `WS_EX_TRANSPARENT`.
- Revealed removes `WS_EX_TRANSPARENT` and applies the interactive region.
- Shielded subtracts the foreground window's modal rectangle from the region. The modal rectangle comes from `GetLastActivePopup` on the foreground owner plus the owner's enabled state; the code does not rely on the dialog class. `GetLastActivePopup` is added to `NativeMethods.txt`.
- Z-order is applied by first calling `SetWindowPos(HWND_NOTOPMOST)` for Hidden and Occluded, then `SetWindowPos(HWND_BOTTOM)`. Revealed and Shielded call `SetWindowPos(HWND_TOPMOST)`. All calls use `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE`. `WS_EX_TOPMOST` must not remain set while occluded, or the overlay stays above normal windows.
- The interactive region is the union of set hit bounds, badge clusters, and the open listing, clipped to the strip column. Do not use the whole strip column; that would intercept clicks in empty space.
- `WS_EX_TRANSPARENT` is toggled with `SetWindowLong(GWL_EXSTYLE)`, added to `NativeMethods.txt`.
- The foreground frame comes from `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, with `GetWindowRect` as a fallback. Exposed width is `max(0, foregroundLeft - stripLeft)` on the left and `max(0, stripRight - foregroundRight)` on the right.
- The reveal edge is 2 DIP. The strip stays revealed while the pointer is within the strip, its tiles, or a 30 DIP margin. After 500 ms outside, it returns to occluded when more than 20 DIP is exposed and to hidden when 20 DIP or less is exposed.
- Window moves set the move-suppression flag. The hide evaluation runs once when the move ends. The existing 100 ms poll samples the cursor and foreground frame; move and resize events update the frame immediately.

### Badge cluster and set listing

- The badge cluster is a separate root child of the overlay visual tree. Card hover scaling must not deform it.
- At rest each badge is 28 DIP. When the badge cluster is hovered, each badge grows to 36 DIP over 120 ms. Card hover scales the card stack to 1.03 and leaves the badge cluster unchanged.
- The cluster anchors to the union's bottom-left for a left strip and bottom-right for a right strip, with a 6 DIP inset.
- Multi-app badges sit side by side, front app first, 4 DIP apart. When the row is too wide, badges overlap by 4 DIP. Show four app badges plus a `+N` chip when the set has five or more distinct apps.
- The set listing is drawn in the same overlay visual tree. It is centered on the tile union, clamped into the work area, with a maximum width of 320 DIP, 6 DIP padding, and 32 DIP rows. The visible height is capped by the work area; excess rows scroll by wheel and the UIA `IsOffscreen` flags follow the visible clip.
- Each row shows the app badge, window title, and app name. Rows are ordered front card first.
- While the listing is open, the overlay region expands to the listing bounds and the listing is hit-tested before tiles. The listing closes on a second badge click, an outside click, a set switch, or a monitor change. Escape is handled only when the overlay already has keyboard focus; the overlay does not take focus, so Escape is not a primary close path.
- Shielded state suppresses badge listing while a modal dialog is open. Badge clicks outside the modal remain pass-through.

### Capture and rendering

- One `SetTileVisual` per strip set. It owns the union bounds, the ordered card visuals, the badge cluster, and the listing visual.
- Live capture is limited to the front three cards of each revealed or shielded set. The front card uses the normal render rate; the next two use 5 frames per second. Older cards never request capture and keep their last surface.
- `RenderThread.SyncCaptureRequests` builds requests only for those front-three cards. Hidden and occluded sets request nothing.
- A card that has never had a frame shows a deliberate placeholder with the app icon and member count; it does not silently fall back to a flat colour.
- The tile visual tree is rebuilt only when membership, geometry, or layout changes. Hover and badge growth use transforms, not a rebuild.
- Tilt is removed from `OverlayTarget`; the shader and painter do not receive a shear value. `TileStack.TiltSlope` is deleted with its tests.

### Geometry and restore

- Snapshot on move or resize end and on set switch. A removed member's geometry is discarded; minimizing or closing does not save it.
- Snapshot uses the window's normal rectangle from `GetWindowPlacement`, the current monitor id, the current DPI, and the maximized flag. For maximized windows, `RestoreBoundsDip` stores the normal rectangle.
- Restore scales by the DPI difference, chooses the monitor with the largest intersection when the saved monitor is gone, keeps the title bar inside the work area, and skips windows that no longer exist.
- A monitor or DPI change clamps the in-memory rectangles immediately.
- Initial center placement applies only to the window that starts a set, only when it has no saved geometry, and never to maximized, fullscreen, minimized, or elevated windows. It uses `SetWindowPos` without activation or z-order change, suppresses reconciliation, re-reads the result, and keeps the current geometry on failure. A window that cannot be opened with `PROCESS_QUERY_LIMITED_INFORMATION` is treated as elevated and skipped.
- On startup the existing flat workspace restore runs unchanged. `StageModel.ApplyWorkspace` converts the saved stage list into one active set and each saved strip window into a singleton strip set in saved order. No set or geometry data is read from disk.

### Accessibility

- One set button per tile with automation id `set|{SetId}` and a name containing the foreground app, window count, and app count.
- One card button per member with automation id `window|{hwnd}` and card-relative bounds.
- One badge button per set with automation id `setbadge|{SetId}` that opens the listing.
- One add button per card with automation id `add|{hwnd}` that performs `ShiftAdd`.
- One listing row button per member with automation id `listing|{hwnd}`, the app badge, title, and app name.
- `IsOffscreen` is computed from the current region and the modal rectangle. Hidden and occluded states expose no accessibility subtree.
- Keyboard reachability is the existing Next and Previous hotkeys plus UIA invoke. The overlay does not become focusable and does not implement Tab or arrow-key focus navigation.

### Settings and hotkey removal

- `AppSettings` drops `AutoHide` and `AutoHideFor`. `MonitorSettings` drops `AutoHide`. `HotkeySettings` drops `Toggle`.
- Old `autoHide` and `hotkeys.toggle` keys are ignored by the existing JSON loader. No migration code is added.
- `SettingsForm` removes the global Auto-hide checkbox, the per-monitor Auto-hide combo, and the Toggle hotkey row. `MonitorRow` and `MergeMonitors` drop the field.
- `HotkeyManager` registers three hotkeys, not four. `Dispatch` cases are Next, Previous, Show Desktop; `UnregisterAll` unregisters ids 1 through 3.
- `TrayApplicationContext` removes the `ToggleRequested` subscription.
- `StageCoordinator` removes `ToggleStripUnderPointer`, `_visibleByMonitor`, `SetAutoHideVisible`, all `AutoHideFor` checks, and the auto-hide branch in `ApplySettings`. Edge reveal is always enabled for managed monitors.

### File-by-file plan

#### Created

| File | Change and rationale |
|---|---|
| `src/StageManager4win.Core/Stages/WindowKey.cs` | New `WindowKey(WindowId, ProcessId)` value. Membership and geometry need a stable identity that cannot inherit a recycled handle. |
| `src/StageManager4win.Core/Stages/WindowGeometry.cs` | New geometry record plus pure DPI-scale and work-area clamp helpers. Separates saved DIP geometry from interop and makes the restore rules testable. |
| `src/StageManager4win.Core/Stages/StageSet.cs` | New set aggregate with id, ordered members, foreground, and geometry. The stage, strip, and renderer all consume one set shape. |
| `src/StageManager4win.App/Accessibility/SetUiaProvider.cs` | New fragment provider for a set button and its child cards, badge, add action, and listing rows. A set is now a real accessibility node, not a front-card proxy. |
| `src/StageManager4win.App/Accessibility/ActionUiaProvider.cs` | New invoke-only provider for the badge and listing rows. Avoids three near-identical provider classes. |
| `tests/StageManager4win.Core.Tests/StageSetTests.cs` | New tests for membership invariants, promotion, demotion, minimize/close removal, monitor change, Shift-add, and AllAtOnce creation. |
| `tests/StageManager4win.Core.Tests/WindowGeometryTests.cs` | New tests for DPI scaling, title-bar clamping, missing-monitor selection, and maximized restore bounds. |

#### Modified: Core

| File | Change and rationale |
|---|---|
| `src/StageManager4win.Core/Stages/MonitorState.cs` | Replace flat `Stage`, `Strip`, `Foreground`, and `ActiveAppId` with `ActiveSet` and ordered `StripSets`. This is the chosen per-monitor state model. |
| `src/StageManager4win.Core/Stages/StageModel.cs` | Replace app-keyed staging with the set transition surface, `WindowKey` membership, set ids, workspace application, cycle order, and the new minimize/promote rules. This is the single authority for membership. |
| `src/StageManager4win.Core/Stages/StripGroup.cs` | Carry `SetId`, `WindowKey` front, and set members instead of an app group. The strip tile is now a set projection. |
| `src/StageManager4win.Core/Layout/StripItem.cs` | Carry `SetId`, member count, and the foreground aspect instead of a single `WindowId`. Layout needs the set identity and fan size. |
| `src/StageManager4win.Core/Layout/TilePlacement.cs` | Carry the set id, union bounds, ordered card placements, badge bounds, and hit bounds. The tile is no longer one front rectangle. |
| `src/StageManager4win.Core/Layout/StripLayoutCalculator.cs` | Lay out one union per set, preserve the shared baseline and 30 DIP gap, and mirror the fan on the right edge. The old per-window sizing no longer matches the tile model. |
| `src/StageManager4win.Core/Layout/StripLayoutOptions.cs` | Add the 300 DIP union cap, 14 DIP step cap, 4 DIP step floor, 0.97 depth scale, 0.86 scale floor, and 6 DIP badge inset. These are the approved fan constants. |
| `src/StageManager4win.Core/Layout/TileStack.cs` | Replace the three fixed card offsets and `TiltSlope` with the shared-middle-y fan and union hit bounds. Tilt is removed by the design. |
| `src/StageManager4win.Core/Layout/TileHitTest.cs` | Return a `HitTarget` for listing row, badge, card, or set background instead of a single `WindowId`. Pointer-down must capture the exact action before layout changes. |
| `src/StageManager4win.Core/Settings/AppSettings.cs` | Remove `AutoHide` and `AutoHideFor`. Edge reveal is no longer a setting. |
| `src/StageManager4win.Core/Settings/MonitorSettings.cs` | Remove the per-monitor `AutoHide` override. The setting is deleted globally. |
| `src/StageManager4win.Core/Settings/HotkeySettings.cs` | Remove `Toggle`. The manual strip-toggle hotkey is deleted. |
| `src/StageManager4win.Core/Settings/WorkspaceRestore.cs` | Return a flat stage/strip/foreground plan instead of constructing the old `MonitorState`. `StageModel.ApplyWorkspace` converts the plan into one active set plus singleton strip sets. |

#### Modified: App

| File | Change and rationale |
|---|---|
| `src/StageManager4win.App/StageCoordinator.cs` | Own activation attribution, set transitions, strip order, geometry save/restore, workspace conversion, capture priority, churn guard, listing state and scroll, and the new presentation requests. This is the orchestration point for every CUJ. |
| `src/StageManager4win.App/Overlay/OverlayWindow.cs` | Add the four presentation states, topmost/non-topmost z-order switching, interactive region with modal subtraction, `WS_EX_TRANSPARENT` toggling, listing hit-testing and wheel scrolling, and badge/card hit targets. The overlay can no longer be unconditionally topmost. |
| `src/StageManager4win.App/Rendering/OverlayTarget.cs` | Render set tiles with fanned cards, a separate badge cluster, clipped and scrollable listing rows, badge hover growth, and no tilt. The visual tree must match the set model and the badge requirement. |
| `src/StageManager4win.App/Rendering/TileVisual.cs` | Replace the single-tile record with `SetTileVisual`, `CardVisual`, `BadgeClusterVisual`, and `SetListingVisual`. Renderer and UIA need the set/card/listing hierarchy. |
| `src/StageManager4win.App/Rendering/RenderCommand.cs` | Change `UpdateOverlay` to carry set visuals, presentation state, clip, and listing; change hover to a set/card/badge target. The render thread must know the new visual shape. |
| `src/StageManager4win.App/Rendering/RenderThread.cs` | Build capture requests only for the front three cards of revealed or shielded sets, and stop requests for hidden/occluded sets. This bounds capture cost without dropping members. |
| `src/StageManager4win.App/Input/AutoHideController.cs` | Add the exposure predicate, the 20 DIP hide threshold, and move suppression; report reveal/occlude instead of a bool. The controller now drives the presentation state. |
| `src/StageManager4win.App/Input/HotkeyManager.cs` | Remove the Toggle hotkey, register three bindings, and renumber dispatch ids. The toggle hotkey is deleted with the setting. |
| `src/StageManager4win.App/Windows/WindowEventWatcher.cs` | Add `EVENT_OBJECT_DESTROY` and `WindowEventKind.Destroyed`, and keep reconcile as the fallback. Membership must be removed when a window closes, including when the destroy hook is unavailable for elevated or cloaked windows. |
| `src/StageManager4win.App/Windows/WindowSource.cs` | Add a maximized/placement query needed for geometry snapshots. The existing bounds and restore-bounds helpers are reused. |
| `src/StageManager4win.App/Windows/WindowActivation.cs` | Add no-activate place and center helpers, and keep minimize/restore/activate. Geometry restore and initial centering must not change z-order or steal focus. |
| `src/StageManager4win.App/Accessibility/TileUiaProvider.cs` | Expose a card button with `window|{hwnd}`, card bounds, app badge, title, app name, and `IsOffscreen`. The front card is no longer the only accessible element. |
| `src/StageManager4win.App/Accessibility/StripUiaProvider.cs` | Build the root tree from set providers, update only in revealed/shielded states, and expose listing rows. The old front-only tile list cannot represent a set. |
| `src/StageManager4win.App/Accessibility/AddToStageUiaProvider.cs` | Rename the label and action to “Add to current set” and target `ShiftAdd`. The old stage-add semantics no longer exist. |
| `src/StageManager4win.App/UI/SettingsForm.cs` | Remove the global Auto-hide checkbox, the per-monitor Auto-hide combo, the Toggle hotkey row, and the `AutoHide` monitor row field. The settings UI must match the deleted settings. |
| `src/StageManager4win.App/TrayApplicationContext.cs` | Remove the `ToggleRequested` subscription. The coordinator no longer exposes a manual strip toggle. |
| `src/StageManager4win.App/NativeMethods.txt` | Add `GetLastActivePopup` and `SetWindowLong`. Modal shielding and `WS_EX_TRANSPARENT` toggling need these APIs. |

#### Modified: Tests

| File | Change and rationale |
|---|---|
| `tests/StageManager4win.Core.Tests/StageModelTests.cs` | Rewrite around set activation, passive activation, Shift-add, minimize/close, monitor change, promotion, and AllAtOnce creation. The old app-stage tests describe the removed model. |
| `tests/StageManager4win.Core.Tests/StripLayoutTests.cs` | Rewrite for set unions, the 300 DIP cap, the step floor, shared middle y, right-edge mirroring, and set-level scrolling. |
| `tests/StageManager4win.Core.Tests/TileStackTests.cs` | Replace fixed offsets and tilt tests with fan scale, step, shared middle y, front-card extent, and union hit bounds. |
| `tests/StageManager4win.Core.Tests/TileHitTestTests.cs` | Test listing-before-badge-before-card-before-background order and the captured target shape. |
| `tests/StageManager4win.Core.Tests/WorkspaceRestoreTests.cs` | Test the flat plan output and the `ApplyWorkspace` conversion: one active set plus singleton strip sets. |
| `tests/StageManager4win.Core.Tests/SettingsStoreTests.cs` | Remove `AutoHide` round-trip assertions, add a legacy JSON test with `autoHide` and `hotkeys.toggle`, and assert the removed keys are ignored. |

#### Deleted

| File | Change and rationale |
|---|---|
| `src/StageManager4win.Core/Stages/StripGrouping.cs` | App-keyed grouping and `MaxCards = 3` are gone. Set membership is already ordered and complete; keeping this file would create a second grouping authority. |
| `tests/StageManager4win.Core.Tests/StripGroupingTests.cs` | The grouping behavior no longer exists. Its replacement coverage lives in `StageSetTests` and `StripLayoutTests`. |

#### Unchanged but verified

- `src/StageManager4win.Core/Stages/CycleCursor.cs` stays generic. `StageModel.CycleOrder` supplies the flattened order.
- `src/StageManager4win.App/Rendering/TilePainter.cs` and `TileShader.cs` keep painting card surfaces; only the shear/tilt input is removed from `OverlayTarget`.
- `src/StageManager4win.App/Capture/CaptureManager.cs` and `CaptureRequest.cs` keep their existing request shape; only the request set and draw cadence change.
- `src/StageManager4win.Core/Settings/SettingsStore.cs` and `WorkspaceStateStore.cs` need no code change. The JSON loader already ignores unknown keys and the workspace schema is unchanged.
- No `.csproj` or dependency changes are needed; SDK-style projects include the new files automatically.

#### Documentation

| File | Change and rationale |
|---|---|
| `docs/designs/set-based-stage-strip.md` | This design document. It records the implementation plan and is not a production file. There is no `docs/designs/SITEMAP.md` to update. |

### Test plan

Automated, in `tests/StageManager4win.Core.Tests`:

- `StageSetTests`
  - A set id is unique and never reused.
  - Members are ordered back to front; the foreground is the last member.
  - Adding an existing key is idempotent; a different process id is not the same member.
  - Removing the last member deletes the set.
  - An emptied active set promotes the first strip set.
  - `ShiftAdd` moves a window between sets and deletes an emptied source.
  - `AllAtOnce` adds only visible, non-minimized siblings of the same app on the same monitor, and only on new-set creation.
  - `SetMode` does not regroup existing sets.
  - `ApplyWorkspace` creates one active set and singleton strip sets.
  - `CycleOrder` returns active members first, then every member of each strip set in strip order.
- `StageModelTests`
  - Activating an unassigned window creates a set, demotes the previous set, and appends it to the strip.
  - Activating an active member reorders the foreground without creating a set.
  - Activating a strip member promotes it and demotes the current set.
  - `Passive` and `Restore` sources never create a set.
  - Minimizing an active member removes it and promotes when the set empties.
  - Minimizing a strip member keeps its membership.
  - Closing a member removes it and discards its geometry.
  - A monitor change moves membership, promotes an emptied source, and keeps the destination foreground consistent.
  - A recycled handle with a different process id is not treated as a member.
- `WindowGeometryTests`
  - DPI scaling round-trips within one pixel.
  - The title bar is clamped into the work area.
  - A missing monitor selects the largest intersection.
  - A maximized window restores its normal rectangle before maximizing.
- `StripLayoutTests`
  - One-window and multi-window sets share the same baseline and 30 DIP gap.
  - Union width caps at 300 DIP; the step never drops below 4 DIP.
  - Back cards scale by 0.97 per depth to a 0.86 floor.
  - All cards share the middle y; the front card extends furthest top, bottom, and inward.
  - Right-edge layouts mirror the horizontal step.
  - Set-level scrolling clamps at both ends.
- `TileHitTestTests`
  - Listing rows win over badges, badges win over cards, and cards win over the set background.
  - Cards are tested front-last.
  - Empty and out-of-bounds points return no target.
- `SettingsStoreTests`
  - A legacy file with `autoHide`, `monitors[].autoHide`, and `hotkeys.toggle` loads successfully.
  - Saving omits the removed keys.
  - Monitor resolvers no longer expose an auto-hide value.
- `WorkspaceRestoreTests`
  - The flat plan keeps current stage windows that are missing from the saved stage.
  - The plan preserves strip order and finds the saved foreground.
  - `ApplyWorkspace` converts the plan into one active set plus singleton strip sets.

### Verification plan

Build and test:

```sh
dotnet build StageManager4win.slnx
dotnet test StageManager4win.slnx
```

Manual checks on a multi-monitor Windows VM with mixed DPI:

1. Activate an unassigned window. A new set appears, the previous set becomes one strip tile, and the new window is centered only when it has no saved geometry.
2. Build a five-window, two-app set. Confirm five cards, a shared middle y, the front card furthest top/bottom/inward, and no tilt. Confirm all five are reachable by click, Shift-click, the badge listing, and the Next/Previous hotkeys.
3. Hover a badge. It grows from 28 to 36 DIP; the cards do not scale. Click it and confirm the listing is centered on the tile, shows badge/title/app name, and each row activates its window. Open a set with more rows than fit and confirm the wheel scrolls the listing and the off-screen flags follow. Confirm outside click and a second badge click close it.
4. Shift-click a window from another set. It moves into the current set, the previous set disappears if empty, and the tile updates.
5. Minimize an active member. It leaves the set; an emptied active set promotes the next strip set. Minimize a strip member and confirm it stays in its set.
6. Switch sets. Confirm the saved positions, maximized state, DPI scaling, and off-screen clamping. Confirm a restart starts with a fresh set model and the flat workspace restore still runs.
7. Cover the strip with a window. Confirm the 2 DIP edge reveals it straight, it stays visible during a window move, and it hides fully at 20 DIP or less exposed.
8. Open a modal dialog from the active app. Confirm the Save button receives clicks and the overlay is shielded. Confirm the overlay sits below normal windows when occluded and above them when revealed.
9. Check virtual desktops and an always-on-top client. Confirm the occluded overlay does not appear above the client and does not disappear behind the desktop.
10. Use a screen reader. Confirm set buttons, card buttons, badge buttons, add buttons, and listing rows are reachable and invokable, and that hidden/occluded states expose no subtree.
11. Open settings. Confirm the Auto-hide controls and Toggle hotkey are gone. Load an old settings file and confirm it opens with defaults and no error.
12. Demote a set with live previews. Confirm the tile keeps the last frames, the front three cards were live, older cards use cached frames, and a never-captured card shows the deliberate placeholder.
