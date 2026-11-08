using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class WorkspaceRestoreTests
{
    private static WindowSnapshot W(long id, string app, string title) =>
        new(new WindowId(id), (int)id, app, app, title, null,
            new WindowBounds(0, 0, 100, 100), "M1",
            IsVisible: true, IsMinimized: false, IsCloaked: false, IsFullscreen: false);

    private static IReadOnlyDictionary<WindowId, WindowSnapshot> Windows(params WindowSnapshot[] windows) =>
        windows.ToDictionary(window => window.Id);

    [Fact]
    public void Plan_KeepsWindowsThatAreOnTheCurrentStageButNotInTheSavedStage()
    {
        var windows = Windows(W(1, "A", "a"), W(2, "A", "a2"), W(3, "B", "b"), W(4, "C", "c"));
        var current = new MonitorState(
            "M1",
            new[] { new WindowId(3) },
            new[] { new WindowId(4), new WindowId(1), new WindowId(2) },
            new WindowId(3),
            "B");
        var saved = new MonitorWorkspace(
            "M1",
            new[] { new WindowIdentity("B", "b") },
            null,
            new[] { new WindowIdentity("A", "a") });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new[] { new WindowId(1) }, plan!.Stage);
        Assert.Contains(new WindowId(3), plan.Strip);
        Assert.DoesNotContain(new WindowId(3), plan.Stage);
        Assert.Contains(new WindowId(2), plan.Strip);
        Assert.Contains(new WindowId(4), plan.Strip);
    }

    [Fact]
    public void Plan_PreservesTheCurrentStripOrder()
    {
        var windows = Windows(W(1, "A", "a"), W(2, "B", "b"), W(3, "C", "c"));
        var current = new MonitorState(
            "M1",
            Array.Empty<WindowId>(),
            new[] { new WindowId(2), new WindowId(1), new WindowId(3) },
            null,
            null);
        var saved = new MonitorWorkspace(
            "M1",
            Array.Empty<WindowIdentity>(),
            null,
            new[] { new WindowIdentity("A", "a") });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new[] { new WindowId(1) }, plan!.Stage);
        Assert.Equal(new[] { new WindowId(2), new WindowId(3) }, plan.Strip);
    }

    [Fact]
    public void Plan_WithoutASavedStage_ReturnsNull()
    {
        var windows = Windows(W(1, "A", "a"));
        var current = new MonitorState(
            "M1",
            new[] { new WindowId(1) },
            Array.Empty<WindowId>(),
            new WindowId(1),
            "A");
        var saved = new MonitorWorkspace("M1", Array.Empty<WindowIdentity>(), null);

        Assert.Null(WorkspaceRestore.Plan(current, saved, windows));
    }

    [Fact]
    public void Plan_RestoresAForegroundThatIsOnTheCurrentStage()
    {
        var windows = Windows(W(1, "A", "a"), W(3, "B", "b"));
        var current = new MonitorState(
            "M1",
            new[] { new WindowId(3) },
            new[] { new WindowId(1) },
            null,
            null);
        var saved = new MonitorWorkspace(
            "M1",
            Array.Empty<WindowIdentity>(),
            new WindowIdentity("B", "b"),
            new[] { new WindowIdentity("A", "a") });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new WindowId(3), plan!.Foreground);
        Assert.Equal("B", plan.ActiveAppId);
    }
}
