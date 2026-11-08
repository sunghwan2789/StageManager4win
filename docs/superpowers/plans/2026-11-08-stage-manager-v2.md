# StageManager4win v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One tile per app drawn as a macOS-style stack, multi-app stages, drag or Shift-click to add an app to the stage, drag an on-stage window onto the strip (or minimize) to remove it, and a wallpaper click that toggles show desktop.

**Architecture:** Windows stay the unit of state. The strip groups windows by app at render time; the stage is a window list that can span apps. The overlay gains click/Shift-click/drag classification on its existing full-monitor surface, the renderer draws up to three tilted, darkened cards per group, and two new event paths (window move, low-level mouse) implement removal and wallpaper clicks.

**Tech Stack:** C# 13, .NET 10, WinForms shell, CsWin32, D3D11 + DirectComposition, Windows Graphics Capture, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-stage-manager-v2-design.md`

## Global Constraints

- Windows 11 22H2+ (build 22621+), TFM `net10.0-windows10.0.22621.0`, .NET 10.
- No new NuGet dependency, no new window, no new thread. CsWin32 only; hand-write a `[DllImport]` only if CsWin32 misses a member.
- The strip lists windows **outside** the stage. Grouping is always on; there is no new setting.
- One tile per app: front window plus at most two back cards. `MaxVisibleStripItems` counts apps.
- Clicking a strip tile replaces the stage; Shift-click or a drop adds to it. Repeated clicks cycle an app's windows in One at a Time.
- Removal is a real minimize. Never move, resize, or hide another app's window except to restore or minimize it.
- The low-level mouse hook never swallows input and never calls Shell or COM inside the callback.
- The window-move removal test requires an unchanged window size: Aero Snap and resizes must not remove.
- Core tests run on macOS: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`. App builds and manual checks run on the Windows 11 VM.
- Follow existing patterns: records, file-scoped namespaces, `sealed`, null-annotated, xUnit `[Fact]`.

## Review Focus

1. **A window dragged to the screen edge to snap, or resized from the edge, must not minimize.** The removal predicate rejects any size change; pinned by `StageRemovalTests` in Task 5 and Task 10's manual check.
2. **Clicking a desktop icon must not toggle show desktop.** The MSAA child/role test decides; the class/time tests are pinned in Task 5, the MSAA path is a manual check in Task 11.
3. **An app with four or more off-stage windows must stay reachable.** Grouping keeps the first three cards, and each click swaps the front, so later windows surface; pinned by `StripGroupingTests` (Task 2) and the cycle test in Task 1.
4. **A v1 workspace file (no `Stage`) must not open an empty stage.** The loader falls back to the v1 foreground rule; the store's null handling is pinned in Task 12.
5. **Dropping an app whose representative window is minimized must show it.** `AddToStage` includes the representative even when minimized, and the coordinator restores it; pinned in Task 1, manual in Task 9.

---

### Task 1: Stage model multi-app rules

**Files:**
- Modify: `src/StageManager4win.Core/Stages/StageModel.cs`
- Test: `tests/StageManager4win.Core.Tests/StageModelTests.cs`

**Interfaces:**
- Produces: `public bool AddToStage(WindowId window)`, `public bool RemoveFromStage(WindowId window)`.
- Rule: `SetForeground` keeps other apps when the foreground app is already on the stage.

- [ ] **Step 1: Write the failing tests**

Append to `StageModelTests.cs` (reusing the existing `W` and `AssertOrder` helpers):

```csharp
[Fact]
public void SetForeground_AppAlreadyOnStage_KeepsOtherApps()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B") }, new WindowId(1));
    model.AddToStage(new WindowId(3));

    Assert.True(model.SetForeground(new WindowId(2)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(2), new WindowId(3) }, m.Stage);
    AssertOrder(new[] { new WindowId(1) }, m.Strip);
    Assert.Equal(new WindowId(2), m.Foreground);
}

[Fact]
public void SetForeground_OutsideApp_ReplacesStage()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "C") }, new WindowId(1));
    model.AddToStage(new WindowId(2));

    Assert.True(model.SetForeground(new WindowId(3)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(3) }, m.Stage);
    AssertOrder(new[] { new WindowId(2), new WindowId(1) }, m.Strip);
}

[Fact]
public void AddToStage_SecondApp_KeepsFirst()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));

    Assert.True(model.AddToStage(new WindowId(2)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(2), new WindowId(1) }, m.Stage);
    Assert.Empty(m.Strip);
    Assert.Equal(new WindowId(2), m.Foreground);
}

[Fact]
public void AddToStage_AllAtOnce_AddsSiblingsButLeavesMinimized()
{
    var model = new StageModel(WindowMode.AllAtOnce);
    model.Reset(new[] { W(1, "A"), W(2, "B"), W(3, "B"), W(4, "B", minimized: true) }, new WindowId(1));

    Assert.True(model.AddToStage(new WindowId(2)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(2), new WindowId(3), new WindowId(1) }, m.Stage);
    AssertOrder(new[] { new WindowId(4) }, m.Strip);
}

[Fact]
public void AddToStage_MinimizedRepresentative_IsStaged()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "B", minimized: true) }, new WindowId(1));

    Assert.True(model.AddToStage(new WindowId(2)));

    Assert.Contains(new WindowId(2), model.Monitors["M1"].Stage);
}

[Fact]
public void AddToStage_AppAlreadyOnStage_SwapsWithoutDuplicates()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "A") }, new WindowId(1));

    Assert.True(model.AddToStage(new WindowId(2)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(2) }, m.Stage);
    AssertOrder(new[] { new WindowId(1) }, m.Strip);
}

[Fact]
public void RemoveFromStage_HeadsStrip_AndKeepsOtherApps()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
    model.AddToStage(new WindowId(2));

    Assert.True(model.RemoveFromStage(new WindowId(2)));

    var m = model.Monitors["M1"];
    AssertOrder(new[] { new WindowId(1) }, m.Stage);
    AssertOrder(new[] { new WindowId(2) }, m.Strip);
    Assert.Null(m.Foreground);
}

[Fact]
public void Add_AllAtOnce_JoinsStageWhenAnyWindowOfAppIsStaged()
{
    var model = new StageModel(WindowMode.AllAtOnce);
    model.Reset(new[] { W(1, "A"), W(2, "B") }, new WindowId(1));
    model.AddToStage(new WindowId(2));

    model.Add(W(3, "B"));

    Assert.Contains(new WindowId(3), model.Monitors["M1"].Stage);
}

[Fact]
public void SetMode_ExpandsAndCollapsesEveryStageApp()
{
    var model = new StageModel();
    model.Reset(new[] { W(1, "A"), W(2, "A"), W(3, "B"), W(4, "B") }, new WindowId(1));
    model.AddToStage(new WindowId(3));

    model.SetMode(WindowMode.AllAtOnce);
    AssertOrder(
        new[] { new WindowId(3), new WindowId(4), new WindowId(1), new WindowId(2) },
        model.Monitors["M1"].Stage);

    model.SetMode(WindowMode.OneAtATime);
    AssertOrder(
        new[] { new WindowId(3), new WindowId(1) },
        model.Monitors["M1"].Stage);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter StageModelTests`
Expected: FAIL — `AddToStage`/`RemoveFromStage` do not exist.

- [ ] **Step 3: Implement the model rules**

In `StageModel.cs`:

1. Replace `SetForeground`'s body: if `monitor.Stage.Any(id => _windows[id].AppId == snapshot.AppId)`, call the new `ReplaceApp(monitor, window)`; else call `ApplyForeground(monitor, window)`; both then set `Foreground`/`ActiveAppId` themselves.
2. Add `public bool AddToStage(WindowId window)`:
   - unknown id → `false`
   - if the app already has a stage window → `ReplaceApp`
   - else `monitor.Stage.InsertRange(0, Contribution(window))`, remove those ids from `monitor.Strip`, set `Foreground = window`, `ActiveAppId = snapshot.AppId`
   - return `true`
3. Add `public bool RemoveFromStage(WindowId window)`: remove from `monitor.Stage`; if it was not there return `false`; insert at `monitor.Strip[0]`; clear `Foreground` when it matches; clear `ActiveAppId` when no window of that app remains on the stage; return `true`.
4. Add `private List<WindowId> Contribution(WindowId window)`: the window itself, plus in `AllAtOnce` the same app's visible, non-minimized windows on the same monitor, in `monitor.Stage.Concat(monitor.Strip)` order.
5. Add `private void ReplaceApp(MonitorData monitor, WindowId window)`: remove the app's stage windows, insert the contribution at the stage head, remove the contributed ids from the strip, insert the displaced ids at the strip head, set `Foreground`/`ActiveAppId`.
6. Rewrite `ApplyForeground` to build the stage from `Contribution(foreground)` and the strip from the old `Stage.Concat(Strip)` order minus the stage.
7. Rewrite `SetMode` to rebuild every stage app: `AllAtOnce` expands each staged app to its visible, non-minimized windows; otherwise each staged app keeps one window (`monitor.Foreground` if it belongs to that app, else its first staged window). Preserve the `Stage.Concat(Strip)` order for the strip.
8. Change `Add`'s join condition to `_mode == WindowMode.AllAtOnce && monitor.Stage.Any(id => _windows[id].AppId == window.AppId)`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: all pass, including the existing v1 `StageModelTests`.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Stages/StageModel.cs tests/StageManager4win.Core.Tests/StageModelTests.cs
git commit -m "feat: multi-app stage rules in StageModel"
```

---

### Task 2: Strip grouping

**Files:**
- Create: `src/StageManager4win.Core/Stages/StripGroup.cs`
- Create: `src/StageManager4win.Core/Stages/StripGrouping.cs`
- Test: `tests/StageManager4win.Core.Tests/StripGroupingTests.cs`

**Interfaces:**
- Consumes: `WindowSnapshot` (`Id`, `AppId`).
- Produces: `public readonly record struct StripGroup(WindowId Front, IReadOnlyList<WindowId> Windows)` and
  `public static IReadOnlyList<StripGroup> StripGrouping.Group(IReadOnlyList<WindowId> strip, IReadOnlyDictionary<WindowId, WindowSnapshot> windows, int maxCards = StripGrouping.MaxCards)` with `public const int MaxCards = 3`.

- [ ] **Step 1: Write the failing tests**

```csharp
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Tests;

public class StripGroupingTests
{
    private static WindowSnapshot W(long id, string app) =>
        new(new WindowId(id), (int)id, app, app, $"window {id}", null,
            new WindowBounds(0, 0, 800, 600), "M1",
            IsVisible: true, IsMinimized: false, IsCloaked: false, IsFullscreen: false);

    private static Dictionary<WindowId, WindowSnapshot> Map(params WindowSnapshot[] windows) =>
        windows.ToDictionary(window => window.Id);

    [Fact]
    public void Group_OrdersByFirstOccurrence()
    {
        var windows = Map(W(1, "A"), W(2, "B"), W(3, "A"));
        var groups = StripGrouping.Group(
            new[] { new WindowId(1), new WindowId(2), new WindowId(3) }, windows);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new WindowId(1), groups[0].Front);
        Assert.Equal(new WindowId(2), groups[1].Front);
    }

    [Fact]
    public void Group_KeepsAtMostThreeCards()
    {
        var windows = Map(W(1, "A"), W(2, "A"), W(3, "A"), W(4, "A"));
        var groups = StripGrouping.Group(
            new[] { new WindowId(1), new WindowId(2), new WindowId(3), new WindowId(4) }, windows);

        Assert.Single(groups);
        Assert.Equal(
            new[] { new WindowId(1), new WindowId(2), new WindowId(3) },
            groups[0].Windows.ToArray());
    }

    [Fact]
    public void Group_SkipsUnknownWindows()
    {
        var windows = Map(W(1, "A"));
        var groups = StripGrouping.Group(
            new[] { new WindowId(99), new WindowId(1) }, windows);

        Assert.Single(groups);
        Assert.Equal(new WindowId(1), groups[0].Front);
    }

    [Fact]
    public void Group_CollectsNonAdjacentWindowsOfTheSameApp()
    {
        var windows = Map(W(1, "A"), W(2, "B"), W(3, "A"));
        var groups = StripGrouping.Group(
            new[] { new WindowId(1), new WindowId(2), new WindowId(3) }, windows);

        Assert.Equal(
            new[] { new WindowId(1), new WindowId(3) },
            groups[0].Windows.ToArray());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter StripGroupingTests`
Expected: FAIL — `StripGrouping` does not exist.

- [ ] **Step 3: Implement grouping**

`StripGroup.cs`:

```csharp
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public readonly record struct StripGroup(WindowId Front, IReadOnlyList<WindowId> Windows);
```

`StripGrouping.cs`: walk `strip` once; skip ids absent from `windows`; for each unseen `AppId`, collect that app's ids from `strip` in order up to `maxCards`, mark them seen, and emit `new StripGroup(cards[0], cards)`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter StripGroupingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Stages/StripGroup.cs src/StageManager4win.Core/Stages/StripGrouping.cs tests/StageManager4win.Core.Tests/StripGroupingTests.cs
git commit -m "feat: group strip windows by app"
```

---

### Task 3: Strip column bounds

**Files:**
- Modify: `src/StageManager4win.Core/Layout/StripLayout.cs`
- Modify: `src/StageManager4win.Core/Layout/StripLayoutCalculator.cs`
- Test: `tests/StageManager4win.Core.Tests/StripLayoutTests.cs`

**Interfaces:**
- Produces: `StripLayout` gains `RectD StripBounds` as the second positional parameter: `public sealed record StripLayout(IReadOnlyList<TilePlacement> Tiles, RectD StripBounds, int FirstVisibleIndex, int VisibleCount)`.

- [ ] **Step 1: Write the failing tests**

Append to `StripLayoutTests.cs`:

```csharp
[Fact]
public void Calculate_LeftEdge_StripBoundsIsTheColumn()
{
    var options = new StripLayoutOptions(1920, 1080, 0, 1040, StripEdge.Left);
    var layout = StripLayoutCalculator.Calculate(
        new[] { new StripItem(new WindowId(1), 16.0 / 9.0) }, options);

    Assert.Equal(0, layout.StripBounds.X);
    Assert.Equal(0, layout.StripBounds.Y);
    Assert.Equal(230.4, layout.StripBounds.Width, 3);
    Assert.Equal(1040, layout.StripBounds.Height);
}

[Fact]
public void Calculate_RightEdge_StripBoundsIsTheColumn()
{
    var options = new StripLayoutOptions(1920, 1080, 0, 1040, StripEdge.Right);
    var layout = StripLayoutCalculator.Calculate(
        new[] { new StripItem(new WindowId(1), 16.0 / 9.0) }, options);

    Assert.Equal(1689.6, layout.StripBounds.X, 3);
    Assert.Equal(230.4, layout.StripBounds.Width, 3);
}
```

Add `using StageManager4win.Core.Windows;` if the file lacks it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter StripLayoutTests`
Expected: FAIL — `StripLayout` has no `StripBounds`.

- [ ] **Step 3: Implement**

In `Calculate`, after `stripWidth` is computed, build:

```csharp
var stripBounds = new RectD(
    options.Edge == StripEdge.Left ? 0 : options.MonitorWidth - stripWidth,
    options.WorkAreaTop,
    stripWidth,
    options.WorkAreaHeight);
```

and return it as the second `StripLayout` argument. Update every existing `new StripLayout(...)` construction.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter StripLayoutTests`
Expected: PASS, including existing layout tests.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Layout/StripLayout.cs src/StageManager4win.Core/Layout/StripLayoutCalculator.cs tests/StageManager4win.Core.Tests/StripLayoutTests.cs
git commit -m "feat: expose the strip column bounds"
```

---

### Task 4: Stack geometry and drag threshold

**Files:**
- Create: `src/StageManager4win.Core/Layout/TileStack.cs`
- Create: `src/StageManager4win.Core/Layout/DragGesture.cs`
- Test: `tests/StageManager4win.Core.Tests/TileStackTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct StackCard(double OffsetY, double Scale, double RotationDegrees, float Dim)`
  - `public static class TileStack` with `HitBounds(RectD front)`, `Card(int index)`, `const double OverflowTop = 20`, `const double OverflowSide = 8`.
  - `public static class DragGesture` with `const double ClickSlopDip = 4`, `IsClick(double dx, double dy)`, `Outside(RectD bounds, double x, double y)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using StageManager4win.Core.Layout;

namespace StageManager4win.Core.Tests;

public class TileStackTests
{
    [Fact]
    public void HitBounds_ExpandsUpAndSideways()
    {
        var bounds = TileStack.HitBounds(new RectD(100, 200, 160, 120));

        Assert.Equal(92, bounds.X);
        Assert.Equal(180, bounds.Y);
        Assert.Equal(176, bounds.Width);
        Assert.Equal(140, bounds.Height);
    }

    [Fact]
    public void Card_FrontIsUnchanged()
    {
        var card = TileStack.Card(0);
        Assert.Equal(0, card.OffsetY);
        Assert.Equal(1, card.Scale);
        Assert.Equal(0, card.RotationDegrees);
        Assert.Equal(1f, card.Dim);
    }

    [Fact]
    public void Card_BackCardsRecedeAndDarken()
    {
        Assert.True(TileStack.Card(1).OffsetY < 0);
        Assert.True(TileStack.Card(1).Scale < 1);
        Assert.True(TileStack.Card(1).Dim < 1f);
        Assert.True(TileStack.Card(2).OffsetY < TileStack.Card(1).OffsetY);
    }

    [Fact]
    public void IsClick_AllowsFourDipOfMovement()
    {
        Assert.True(DragGesture.IsClick(3, 2));
        Assert.False(DragGesture.IsClick(4.1, 0));
    }

    [Fact]
    public void Outside_IgnoresTheRightAndBottomEdges()
    {
        var bounds = new RectD(0, 0, 100, 100);
        Assert.False(DragGesture.Outside(bounds, 99, 99));
        Assert.True(DragGesture.Outside(bounds, 100, 50));
        Assert.True(DragGesture.Outside(bounds, -1, 50));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter TileStackTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`TileStack.Card`: index 0 → `(0, 1.0, 0, 1f)`; 1 → `(-6, 0.96, -2.5, 0.6f)`; 2 → `(-11, 0.92, 2.0, 0.4f)`; any other index → the index-2 card. `HitBounds` returns `new RectD(front.X - OverflowSide, front.Y - OverflowTop, front.Width + OverflowSide * 2, front.Height + OverflowTop)`.

`DragGesture.IsClick` compares squared distance to `ClickSlopDip * ClickSlopDip`. `Outside` is the half-open rectangle test.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter TileStackTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Layout/TileStack.cs src/StageManager4win.Core/Layout/DragGesture.cs tests/StageManager4win.Core.Tests/TileStackTests.cs
git commit -m "feat: stack geometry and drag thresholds"
```

---

### Task 5: Removal and wallpaper-click predicates

**Files:**
- Create: `src/StageManager4win.Core/Stages/StageRemoval.cs`
- Create: `src/StageManager4win.Core/Input/DesktopClick.cs`
- Test: `tests/StageManager4win.Core.Tests/StageRemovalTests.cs`
- Test: `tests/StageManager4win.Core.Tests/DesktopClickTests.cs`

**Interfaces:**
- Produces:
  - `public static bool StageRemoval.ShouldRemove(bool wasOnStage, bool sameMonitor, int startWidth, int startHeight, int endWidth, int endHeight, bool cursorOnTile, int tolerancePixels = 2)`
  - `public static class DesktopClick` with `const int MaxMoveDip = 4`, `const int MaxMilliseconds = 500`, `IsToggle(double dx, double dy, long elapsedMilliseconds)`, `IsDesktopClass(string className)` (true for `Progman` and `WorkerW`).

- [ ] **Step 1: Write the failing tests**

```csharp
using StageManager4win.Core.Input;
using StageManager4win.Core.Stages;

namespace StageManager4win.Core.Tests;

public class StageRemovalTests
{
    [Fact]
    public void ShouldRemove_WhenEveryConditionHolds()
    {
        Assert.True(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenAeroSnapChangedTheSize()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 960, endHeight: 1040,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheCursorIsNotOnATile()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: false));
    }

    [Fact]
    public void ShouldNotRemove_AcrossMonitors()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: false,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheWindowWasNotOnStage()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: false, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 800, endHeight: 600,
            cursorOnTile: true));
    }

    [Fact]
    public void ShouldNotRemove_WhenTheSizeChangedByMoreThanTheTolerance()
    {
        Assert.False(StageRemoval.ShouldRemove(
            wasOnStage: true, sameMonitor: true,
            startWidth: 800, startHeight: 600,
            endWidth: 803, endHeight: 600,
            cursorOnTile: true));
    }
}

public class DesktopClickTests
{
    [Fact]
    public void IsToggle_AcceptsAShortStillClick()
    {
        Assert.True(DesktopClick.IsToggle(3, 2, 300));
    }

    [Fact]
    public void IsToggle_RejectsADrag()
    {
        Assert.False(DesktopClick.IsToggle(5, 0, 100));
    }

    [Fact]
    public void IsToggle_RejectsASlowPress()
    {
        Assert.False(DesktopClick.IsToggle(0, 0, 501));
    }

    [Fact]
    public void IsDesktopClass_MatchesTheShellWindows()
    {
        Assert.True(DesktopClick.IsDesktopClass("Progman"));
        Assert.True(DesktopClick.IsDesktopClass("WorkerW"));
        Assert.False(DesktopClick.IsDesktopClass("SHELLDLL_DefView"));
        Assert.False(DesktopClick.IsDesktopClass("Shell_TrayWnd"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter "StageRemovalTests|DesktopClickTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`StageRemoval.ShouldRemove` returns true only when every parameter holds and both dimensions differ by at most `tolerancePixels`.

`DesktopClick.IsToggle` reuses the same squared-distance rule with `MaxMoveDip` and `elapsedMilliseconds <= MaxMilliseconds`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter "StageRemovalTests|DesktopClickTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Stages/StageRemoval.cs src/StageManager4win.Core/Input/DesktopClick.cs tests/StageManager4win.Core.Tests/StageRemovalTests.cs tests/StageManager4win.Core.Tests/DesktopClickTests.cs
git commit -m "feat: removal and wallpaper-click predicates"
```

---

### Task 6: Stack rendering and ghost

**Files:**
- Modify: `src/StageManager4win.App/Rendering/TileShader.cs`
- Modify: `src/StageManager4win.App/Rendering/TilePainter.cs`
- Modify: `src/StageManager4win.App/Rendering/TileVisual.cs`
- Modify: `src/StageManager4win.App/Rendering/OverlayTarget.cs`
- Modify: `src/StageManager4win.App/Rendering/RenderCommand.cs`
- Modify: `src/StageManager4win.App/Rendering/RenderThread.cs`

**Interfaces:**
- Consumes: `TileStack.Card`, `StackCard`.
- Produces:
  - `TileVisual` gains `int StackIndex = 0`, `int AppWindowCount = 1`, and `WindowId Group = default` as trailing optional parameters.
  - `public void OverlayTarget.SetGhost(WindowId? front, double deltaX, double deltaY)`.
  - `RenderCommand.UpdateDragGhost(string MonitorId, WindowId? Window, double DeltaX, double DeltaY)`.

- [ ] **Step 1: Add the dim constant to the shader and painter**

In `TileShader.Hlsl`, append `float4 Tint;` to the cbuffer after `BorderColor`, and multiply every return's rgb by `Tint.x` (border, background, sampled texture). In `TilePainter.TileConstants`, append `public Vector4 Tint;`. Add `float dim = 1f` as the last parameter of `Paint` and `PaintFrame`, and set `Tint = new Vector4(dim, dim, dim, 1f)` in both constant blocks.

- [ ] **Step 2: Apply the stack transform in OverlayTarget**

For each `TileVisual`, read `var card = TileStack.Card(tile.StackIndex)`.

- Create the surface with the tile's size as today; pass `card.Dim` to `TilePainter.PaintFrame` and `Paint` (the icon path).
- Store on `TileTarget`: `Front` (the group's front `WindowId`; the renderer uses `tile.Group == default ? tile.Window : tile.Group`), `CardOffsetY`, `CardScale`, `CardRotation`, `BaseOffsetX`, `BaseOffsetY`, `Dim`.
- Add `private Matrix3x2 CardTransform(TileTarget tile, double dpiScale)` returning `Matrix3x2.CreateScale((float)tile.CardScale, center) * Matrix3x2.CreateRotation(radians, center) * Matrix3x2.CreateTranslation(0, (float)(tile.CardOffsetY * dpiScale))` where `center = new Vector2(width/2, height/2)`.
- Add `private void ApplyTransforms()` that, for every tile, sets `SetOffsetX/Y` to `BaseOffset + ghost delta` and `SetTransform(CardTransform)`; store the ghost delta in two fields set by `SetGhost(WindowId? front, double dx, double dy)`, which applies the delta only to tiles whose `Group` is `front` (all others zero) and calls `ApplyTransforms()`.
- Replace `ApplyHover`'s transform logic: non-hovered → `SetTransform(CardTransform)`; hovered → `SetTransform(Matrix3x2.CreateScale(1.05f, center) * CardTransform)`. Skip hover while a ghost is active.
- Call `ApplyTransforms()` at the end of `Update`, and after building `next`, re-add every visual to `_root` in list order (`_root.AddVisual(tile.Visual, true, null)`) so back cards stay behind their front card when groups change.

- [ ] **Step 3: Route the ghost command**

`RenderCommand.cs`: add `internal sealed record UpdateDragGhost(string MonitorId, WindowId? Window, double DeltaX, double DeltaY) : RenderCommand;`.
`RenderThread.cs`: in the command switch, call `target.SetGhost(update.Window, update.DeltaX, update.DeltaY)` and set `dirty = true`.

- [ ] **Step 4: Build**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Run the app; strip tiles show one live front card.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.App/Rendering
git commit -m "feat: draw app stacks and a drag ghost"
```

---

### Task 7: Overlay click, Shift-click, and drag

**Files:**
- Modify: `src/StageManager4win.App/Overlay/OverlayWindow.cs`

**Interfaces:**
- Consumes: `DragGesture`, `TileStack.HitBounds`, `TileVisual.StackIndex`.
- Produces events:
  - `public event Action<WindowId>? TileDropped`
  - `public event Action<WindowId, double, double>? DragMoved` (window, delta X, delta Y in DIPs)
  - `public event Action? DragStarted`
  - `public event Action? DragEnded`
  - `public void SetStripBounds(RectD boundsDip)`

- [ ] **Step 1: Track front tiles and the drop area**

Add fields: `private IReadOnlyList<TileVisual> _frontTiles = Array.Empty<TileVisual>();`, `private RectD _stripBounds;`, `private WindowId? _dragWindow;`, `private PointF _dragStart;`, `private bool _dragShift;`, `private bool _dragging;`, `private RectD? _ghost;`.

`SetTiles`: set `_frontTiles = tiles.Where(tile => tile.StackIndex == 0).ToArray()`; build `_placements` from `TileStack.HitBounds(tile.BoundsDip)`; pass `_frontTiles` to `_uiaRoot?.Update`.

`SetStripBounds(RectD boundsDip)`: store it; no region change.

- [ ] **Step 2: Classify the gesture**

Add `WmLeftButtonUp = 0x0202` and `WmCaptureChanged = 0x0215`. Add `private PointF PointFromPacked(nint packed)` returning DIPs (the body of `HitFromPacked` without the hit test).

Replace the `WmLeftButtonDown` case:

```csharp
case WmLeftButtonDown:
    var point = PointFromPacked(message.LParam);
    if (TileHitTest.Hit(_placements, point.X, point.Y) is not { } pressed)
    {
        return;
    }

    _dragWindow = pressed;
    _dragStart = point;
    _dragShift = (Control.ModifierKeys & Keys.Shift) != 0;
    _dragging = false;
    Capture = true;
    return;
```

Add `WmMouseMove` handling before the hover update: when `_dragWindow is { } drag` and `Capture` is true, compute `dx = point.X - _dragStart.X`, `dy = point.Y - _dragStart.Y`; if not `_dragging` and `!DragGesture.IsClick(dx, dy)`, set `_dragging = true` and raise `DragStarted`; when `_dragging`, raise `DragMoved(drag, dx, dy)` and set `_ghost` from the pressed tile's front bounds offset by `(dx, dy)`, then return without changing hover.

Add the `WmLeftButtonUp` case:

```csharp
case WmLeftButtonUp:
    var up = PointFromPacked(message.LParam);
    var released = _dragWindow;
    var wasDragging = _dragging;
    var shift = _dragShift;
    _dragWindow = null;
    _dragging = false;
    _dragShift = false;
    _ghost = null;
    Capture = false;
    UpdateRegion();
    if (released is { } window)
    {
        if (!wasDragging && HitFromPacked(message.LParam) == window)
        {
            if (shift) TileDropped?.Invoke(window);
            else TileClicked?.Invoke(window);
        }
        else if (wasDragging && DragGesture.Outside(_stripBounds, up.X, up.Y))
        {
            TileDropped?.Invoke(window);
        }
    }

    if (wasDragging)
    {
        DragEnded?.Invoke();
    }

    return;
case WmCaptureChanged:
    if (_dragWindow is not null || _dragging)
    {
        _dragWindow = null;
        _dragging = false;
        _dragShift = false;
        _ghost = null;
        UpdateRegion();
        DragEnded?.Invoke();
    }

    break;
```

`UpdateRegion` unions `TileStack.HitBounds(placement.Bounds)` for each placement and `_ghost` when set (converted with the same DPI padding as today).

- [ ] **Step 3: Guard the UIA root**

`EnsureUiaRoot` passes `_frontTiles` instead of `_tiles`.

- [ ] **Step 4: Build**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual check: click activates on release; Shift-click raises no activation; a drag outside the strip raises the drop event (add a temporary log line, remove it before commit).

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.App/Overlay/OverlayWindow.cs
git commit -m "feat: click, shift-click, and drag on strip tiles"
```

---

### Task 8: Coordinator strip groups and stack visuals

**Files:**
- Modify: `src/StageManager4win.App/StageCoordinator.cs`
- Modify: `src/StageManager4win.App/Rendering/TileVisual.cs`

**Interfaces:**
- Consumes: `StripGrouping.Group`, `StripLayout.StripBounds`, `TileStack.HitBounds`, and the `TileVisual` fields added in Task 6 (`StackIndex`, `AppWindowCount`, `Group`).
- Produces: strip tiles emitted back-to-front, one group per app.

- [ ] **Step 1: Add the visual fields**

Extend `TileVisual` with the trailing parameters above. The coordinator passes `stackIndex`, `group.Windows.Count`, and `group.Front`.

- [ ] **Step 2: Rebuild Refresh around groups**

In `Refresh`, replace the raw `strip.Skip(first).Take(maxItems)` loop with:

```csharp
var groups = StripGrouping.Group(strip, _model.Windows);
var first = Math.Clamp(_firstVisibleIndex.GetValueOrDefault(monitor.Id), 0, Math.Max(0, groups.Count - 1));
_firstVisibleIndex[monitor.Id] = first;
var items = new List<StripItem>(Math.Min(maxItems, groups.Count));
var emitted = new List<(StripGroup Group, double Aspect)>(Math.Min(maxItems, groups.Count));
foreach (var group in groups.Skip(first).Take(maxItems))
{
    if (!_model.Windows.TryGetValue(group.Front, out var snapshot)) continue;
    var boundsAspect = snapshot.Bounds.Height > 0 ? (double)snapshot.Bounds.Width / snapshot.Bounds.Height : 4.0 / 3.0;
    var aspect = snapshot.IsMinimized ? _lastAspect.GetValueOrDefault(group.Front, boundsAspect) : boundsAspect;
    if (!snapshot.IsMinimized) _lastAspect[group.Front] = boundsAspect;
    items.Add(new StripItem(group.Front, aspect));
    emitted.Add((group, aspect));
}
```

After `layout` is computed, build `tiles` by walking `layout.Tiles` and, for each placement, emitting its group's windows **back to front** (`StackIndex = count - 1 - i`), all with the same `BoundsDip`, `Group = group.Front`, and `AppWindowCount = count`. Skip windows missing from `_model.Windows`.

Pass `layout.StripBounds` to `overlay.SetStripBounds`.

- [ ] **Step 3: Make Scroll group-aware**

`Scroll` must clamp against the group count:

```csharp
var count = _model.Monitors.TryGetValue(monitorId, out var state)
    ? StripGrouping.Group(state.Strip, _model.Windows).Count
    : 0;
```

- [ ] **Step 4: Build and check**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual: an app with several off-stage windows draws one tile with tilted, darkened cards; the front is live; scrolling moves by app; the strip shows one tile per app.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.App/StageCoordinator.cs src/StageManager4win.App/Rendering/TileVisual.cs
git commit -m "feat: app-grouped strip with stacked tiles"
```

---

### Task 9: Add-to-stage, drag wiring, and auto-hide suppression

**Files:**
- Modify: `src/StageManager4win.App/StageCoordinator.cs`
- Modify: `src/StageManager4win.App/Input/AutoHideController.cs`
- Modify: `src/StageManager4win.App/Windows/WindowActivation.cs`

**Interfaces:**
- Consumes: `OverlayWindow.TileDropped`, `DragStarted`, `DragMoved`, `DragEnded`, `StageModel.AddToStage`, `WindowActivation.Restore`.
- Produces: `public void AutoHideController.Suppress(bool suppressed)`, `public static void WindowActivation.Minimize(WindowId window)`, and `StripHitArea` gains `IReadOnlyList<RectD> TilesDip` and `RectD StripBoundsDip`.

- [ ] **Step 1: Wire the overlay events**

In `CreateOverlay`:

```csharp
overlay.TileDropped += window => AddWindowToStage(window);
overlay.DragStarted += () => _autoHide.Suppress(true);
overlay.DragMoved += (window, dx, dy) =>
    _render.Post(new RenderCommand.UpdateDragGhost(monitorId, window, dx, dy));
overlay.DragEnded += () =>
{
    _autoHide.Suppress(false);
    _render.Post(new RenderCommand.UpdateDragGhost(monitorId, null, 0, 0));
};
```

Add `private void AddWindowToStage(WindowId window)`: look up the snapshot; capture `previous = _model.Monitors[...]`; call `_model.AddToStage(window)`; if false, `Reconcile()` and return; restore every window now on the stage (`WindowActivation.Restore`); activate `window`; `Refresh()`; `ScheduleWorkspaceSave()`; on activation failure restore `previous`, `Reconcile()`, and report the existing status message.

- [ ] **Step 2: Complete the strip hit area**

`StripHitArea` becomes `record StripHitArea(RectD BoundsDip, RectD EdgeZoneDip, RectD StripBoundsDip, IReadOnlyList<RectD> TilesDip)`. In `Refresh`, pass `layout.StripBounds` and `layout.Tiles.Select(t => t.Bounds).ToArray()`; when the layout is empty, keep the edge zone and pass the layout's `StripBounds` with an empty tile list.

`AutoHideController` gets `private bool _suppressed;` and `public void Suppress(bool suppressed) { _suppressed = suppressed; }`. In `Poll`, when `_suppressed`, reveal when the cursor is inside `StripBoundsDip` or `EdgeZoneDip`, then `continue` without ever hiding.

- [ ] **Step 3: Add the minimize helper**

```csharp
public static void Minimize(WindowId window) =>
    PInvoke.ShowWindow(new HWND((nint)window.Value), SHOW_WINDOW_CMD.SW_MINIMIZE);
```

- [ ] **Step 4: Build and check**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual: drop outside adds a second app and keeps the first; Shift-click does the same; a drop inside cancels; auto-hide stays open during the drag; the ghost follows the pointer.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.App/StageCoordinator.cs src/StageManager4win.App/Input/AutoHideController.cs src/StageManager4win.App/Windows/WindowActivation.cs
git commit -m "feat: add apps to the stage by drop or shift-click"
```

---

### Task 10: Remove a window by dragging it onto the strip

**Files:**
- Modify: `src/StageManager4win.App/Windows/WindowEventWatcher.cs`
- Modify: `src/StageManager4win.App/Windows/WindowSource.cs`
- Modify: `src/StageManager4win.App/StageCoordinator.cs`

**Interfaces:**
- Consumes: `StageRemoval.ShouldRemove`, `StripHitArea.TilesDip`, `AutoHideController.Suppress`, `WindowActivation.Minimize`.
- Produces: `WindowEventKind.MoveSizeStart`, `WindowEventKind.MoveSizeEnd`; `public WindowBounds WindowSource.GetBounds(nint handle)`; `StageModel.RemoveFromStage`.

- [ ] **Step 1: Hook the move events**

`WindowEventKind` gains `MoveSizeStart` and `MoveSizeEnd`. `Start()` adds `Hook(PInvoke.EVENT_SYSTEM_MOVESIZESTART, PInvoke.EVENT_SYSTEM_MOVESIZESTART)` and the same for `EVENT_SYSTEM_MOVESIZEEND`. `OnWinEvent` maps them to the two kinds before the default branch (they pass the same `hwnd`/idObject filter).

Make `WindowSource.GetBounds` accessible: add `public WindowBounds GetBounds(nint handle) => GetBounds(new HWND(handle));`.

- [ ] **Step 2: Handle the move in the coordinator**

Add `private sealed record MovingWindow(WindowId Window, string MonitorId, int Width, int Height);` and a `MovingWindow? _moving` field. In `OnWindowEvent`:

```csharp
case WindowEventKind.MoveSizeStart:
    OnMoveSizeStart(hwnd);
    return;
case WindowEventKind.MoveSizeEnd:
    OnMoveSizeEnd(hwnd);
    return;
```

`OnMoveSizeStart`: look up the snapshot; require `state.Stage.Contains(id)`; read `_source.GetBounds(hwnd)`; store `_moving`; `_autoHide.Suppress(true)`.

`OnMoveSizeEnd`: require `_moving` with the same `WindowId`; read the end bounds, the monitor, and the cursor DIP position; require the cursor inside one of `StripHitArea.TilesDip`; call

```csharp
StageRemoval.ShouldRemove(
    wasOnStage: true,
    sameMonitor: snapshot.MonitorId == moving.MonitorId,
    startWidth: moving.Width, startHeight: moving.Height,
    endWidth: bounds.Width, endHeight: bounds.Height,
    cursorOnTile: onTile)
```

and on true call `_model.RemoveFromStage(id)`, `WindowActivation.Minimize(id)`, `Refresh()`, `ScheduleWorkspaceSave()`. Always clear `_moving` and `_autoHide.Suppress(false)`.

- [ ] **Step 3: Build and check**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual: drag a stage window by its title bar onto a strip tile and release — it minimizes and appears at the strip head; drag it to the left edge to snap — it snaps and stays; resize it — nothing happens; drag it onto the strip gap — nothing happens.

- [ ] **Step 4: Commit**

```bash
git add src/StageManager4win.App/Windows/WindowEventWatcher.cs src/StageManager4win.App/Windows/WindowSource.cs src/StageManager4win.App/StageCoordinator.cs
git commit -m "feat: remove a stage window by dragging it onto the strip"
```

---

### Task 11: Wallpaper click toggles show desktop

**Files:**
- Create: `src/StageManager4win.App/Input/DesktopClickWatcher.cs`
- Modify: `src/StageManager4win.App/StageCoordinator.cs`

**Interfaces:**
- Consumes: `DesktopClick.IsToggle`, `DesktopClick.IsDesktopClass`, `StageCoordinator.ShowDesktop`.
- Produces: `internal sealed class DesktopClickWatcher : IDisposable` with `event Action? WallpaperClicked`, `void Start()`, `void Dispose()`.

- [ ] **Step 1: Implement the watcher**

Use CsWin32 `SetWindowsHookEx` with `WINDOWS_HOOK_ID.WH_MOUSE_LL`, a `HOOKPROC` field kept alive for the lifetime, `CallNextHookEx`, and `UnhookWindowsHookEx`. On `WM_LBUTTONDOWN`, if `IsWallpaper(point)` (see below), record the point and `Environment.TickCount64`. On `WM_LBUTTONUP`, if a record exists, compute the delta and elapsed, and when `DesktopClick.IsToggle(dx, dy, elapsed)` post `WallpaperClicked` through the captured `SynchronizationContext`; always clear the record. Return `CallNextHookEx` in every path.

`IsWallpaper(POINT point)`:
1. `WindowFromPoint`; walk `GetAncestor(GA_ROOT)` and `GetParent` until null, collecting class names; require the root class to satisfy `DesktopClick.IsDesktopClass` (walk the chain, not only the root).
2. Call `AccessibleObjectFromPoint(point, out IAccessible? acc, out object child)` (CsWin32 if generated, else `[DllImport("oleacc.dll")]`). Reject when `child` is an `int` child id other than 0, or when `acc.get_accRole(0)` is `ROLE_SYSTEM_LISTITEM` (0x22). Reject on any exception or null: fail closed.
3. Reject when the point is inside a strip tile (the coordinator passes a `Func<POINT, bool> isOverlayPoint` predicate; the coordinator checks its `_stripAreas`). Simpler: the low-level hook sees the point before the overlay handles it, so exclude the strip rect via a constructor callback.

`Start()` installs the hook on the UI thread; `Dispose()` unhooks.

- [ ] **Step 2: Wire it in the coordinator**

Add `private DesktopClickWatcher? _desktopClick;`. In `Start()`, after the watcher: `_desktopClick = new DesktopClickWatcher(point => IsOverStrip(point)); _desktopClick.WallpaperClicked += ShowDesktop; _desktopClick.Start();`. Dispose it in `Dispose()` before the overlays. `IsOverStrip` converts the screen point to each monitor's DIP and checks `_stripAreas`' `StripBoundsDip`.

- [ ] **Step 3: Build and check**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual: clicking the wallpaper shows the desktop; clicking again restores; clicking an icon selects it and does not toggle; clicking the taskbar or a strip tile does not toggle; dragging on the wallpaper (rubber band) does not toggle.

- [ ] **Step 4: Commit**

```bash
git add src/StageManager4win.App/Input/DesktopClickWatcher.cs src/StageManager4win.App/StageCoordinator.cs
git commit -m "feat: toggle show desktop on a wallpaper click"
```

---

### Task 12: Persist the stage

**Files:**
- Modify: `src/StageManager4win.Core/Settings/MonitorWorkspace.cs`
- Modify: `src/StageManager4win.App/StageCoordinator.cs`
- Test: `tests/StageManager4win.Core.Tests/WorkspaceStateStoreTests.cs`

**Interfaces:**
- Produces: `MonitorWorkspace(string MonitorId, IReadOnlyList<WindowIdentity> StripOrder, WindowIdentity? Foreground, IReadOnlyList<WindowIdentity>? Stage = null)`.

- [ ] **Step 1: Write the failing test**

Append to `WorkspaceStateStoreTests.cs`:

```csharp
[Fact]
public void Stage_RoundTrips()
{
    var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var store = new WorkspaceStateStore(directory);
        store.Save(new WorkspaceState(new[]
        {
            new MonitorWorkspace(
                "M1",
                new[] { new WindowIdentity("A", "a1") },
                new WindowIdentity("B", "b1"),
                new[] { new WindowIdentity("B", "b1") })
        }));

        var loaded = store.Load();
        var monitor = Assert.Single(loaded.Monitors);
        Assert.NotNull(monitor.Stage);
        Assert.Single(monitor.Stage!);
        Assert.Equal("B", monitor.Stage![0].AppId);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}

[Fact]
public void Load_WithoutStage_LeavesItNull()
{
    var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "workspace.json"),
            "{\"Monitors\":[{\"MonitorId\":\"M1\",\"StripOrder\":[],\"Foreground\":null}]}");
        var monitor = Assert.Single(new WorkspaceStateStore(directory).Load().Monitors);
        Assert.Null(monitor.Stage);
    }
    finally
    {
        Directory.Delete(directory, true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj --filter WorkspaceStateStoreTests`
Expected: FAIL — `MonitorWorkspace` has no `Stage`.

- [ ] **Step 3: Add the field and wire save/load**

Add the trailing optional `Stage` parameter. In `OnWorkspaceSaveTick`, build `var stage = state.Stage.Select(...).ToList()` and pass it.

In `LoadWorkspace`, after `ReorderStrip`:

- When `monitor.Stage is { Count: > 0 }`, map each identity to a window id (search `_model.Windows` on that monitor, first match by `AppId` + `Title` not already used), build `stageIds`; then `stripIds = all monitor windows in the model's current order that are not in stageIds`; `foregroundId` from `monitor.Foreground`; call `_model.Restore(new MonitorState(monitor.MonitorId, stageIds, stripIds, foregroundId, foregroundSnapshot?.AppId))`.
- When `monitor.Stage` is null or empty, keep the existing `SetForeground` behaviour.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/StageManager4win.Core/Settings/MonitorWorkspace.cs src/StageManager4win.App/StageCoordinator.cs tests/StageManager4win.Core.Tests/WorkspaceStateStoreTests.cs
git commit -m "feat: persist the multi-app stage"
```

---

### Task 13: Accessibility for adding to the stage

**Files:**
- Create: `src/StageManager4win.App/Accessibility/AddToStageUiaProvider.cs`
- Modify: `src/StageManager4win.App/Accessibility/StripUiaProvider.cs`
- Modify: `src/StageManager4win.App/Accessibility/TileUiaProvider.cs`
- Modify: `src/StageManager4win.App/Overlay/OverlayWindow.cs`

**Interfaces:**
- Consumes: `TileVisual.AppWindowCount`, `TileVisual.Window`.
- Produces: `StripUiaProvider(nint hwnd, Action<WindowId> invoke, Action<WindowId> add)`; the strip's children interleave a tile and an "Add <App> to the stage" button.

- [ ] **Step 1: Add the add-button provider**

`AddToStageUiaProvider` implements `IRawElementProviderSimple`, `IRawElementProviderFragment`, `IInvokeProvider`:
- control type Button
- name `$"Add {appName} to the stage"`
- automation id `$"add|{appId}|{window.Value}"`
- bounds = the tile's bounds
- runtime id `{ UiaInterop.UiaAppendRuntimeId, unchecked((int)window.Value) ^ 0x5A5A5A }`
- parent/siblings via the root
- `Invoke()` calls the add callback

- [ ] **Step 2: Interleave children in StripUiaProvider**

Replace `_tiles` with `IRawElementProviderFragment[] _children` plus `TileUiaProvider[] _tiles` (for hit testing and bounds). In `Update`, build both per tile (`tile`, `add`), in that order, and set the root bounds to the union of the tile bounds. `Navigate(FirstChild/LastChild)` uses `_children`. `NavigateSibling(IRawElementProviderFragment provider, int delta)` indexes `_children`. `ElementProviderFromPoint` still scans `_tiles`.

`TileUiaProvider`: change `Navigate` to pass `this` as `IRawElementProviderFragment`; its name becomes `data.WindowCount > 1 ? $"{AppName} — {WindowCount} windows" : $"{AppName} — {Title}"`; add `WindowCount` to `TileData` and read `tile.AppWindowCount` in `Update`.

`OverlayWindow.EnsureUiaRoot`: `_uiaRoot ??= new StripUiaProvider(Handle, InvokeTile, DropTile);` where `DropTile` raises `TileDropped` on the UI thread (same `BeginInvoke` pattern as `InvokeTile`).

- [ ] **Step 3: Build and check**

Run on the Windows VM: `dotnet build src/StageManager4win.App/StageManager4win.App.csproj`
Expected: build succeeds. Manual with Narrator: the strip reads each app once with its window count, and the adjacent "Add … to the stage" button builds a multi-app stage.

- [ ] **Step 4: Commit**

```bash
git add src/StageManager4win.App/Accessibility src/StageManager4win.App/Overlay/OverlayWindow.cs
git commit -m "feat: expose add-to-stage to accessibility"
```

---

## Final verification

- [ ] `dotnet test tests/StageManager4win.Core.Tests/StageManager4win.Core.Tests.csproj` — all pass.
- [ ] On the Windows VM: `dotnet build StageManager4win.sln -c Release` — succeeds.
- [ ] Manual checklist from the spec §8: stack and z-order, ghost and region, drop outside, Shift-click, drop inside, window drag to a tile, window drag to the edge, minimize, wallpaper click, icon click, auto-hide, UIA.
- [ ] Commit the design doc and this plan if they are not yet committed.
