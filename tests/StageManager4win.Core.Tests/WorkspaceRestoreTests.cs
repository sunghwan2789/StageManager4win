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

    private static WindowKey K(long id) => new(new WindowId(id), (int)id);

    private static StageSet Set(string id, params long[] ids)
    {
        var keys = ids.Select(id => K(id)).ToArray();
        return new StageSet(id, keys, keys[^1], new Dictionary<WindowKey, WindowGeometry>());
    }

    [Fact]
    public void Plan_KeepsWindowsThatAreOnTheCurrentStageButNotInTheSavedStage()
    {
        var windows = Windows(W(1, "A", "a"), W(2, "A", "a2"), W(3, "B", "b"), W(4, "C", "c"));
        var current = new MonitorState(
            "M1",
            Set("active", 3),
            new[] { Set("s4", 4), Set("s1", 1), Set("s2", 2) });
        var saved = new MonitorWorkspace(
            "M1",
            new[] { new WindowIdentity("B", "b") },
            null,
            new[] { new WindowIdentity("A", "a") });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new[] { new WindowId(1) }, plan!.Stage);
        Assert.Equal(new[] { new WindowId(3), new WindowId(4), new WindowId(2) }, plan.Strip);
        Assert.DoesNotContain(new WindowId(3), plan.Stage);
    }

    [Fact]
    public void Plan_PreservesTheCurrentStripOrder()
    {
        var windows = Windows(W(1, "A", "a"), W(2, "B", "b"), W(3, "C", "c"));
        var current = new MonitorState(
            "M1",
            null,
            new[] { Set("s2", 2), Set("s1", 1), Set("s3", 3) });
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
        var current = new MonitorState("M1", Set("active", 1), Array.Empty<StageSet>());
        var saved = new MonitorWorkspace("M1", Array.Empty<WindowIdentity>(), null);

        Assert.Null(WorkspaceRestore.Plan(current, saved, windows));
    }

    [Fact]
    public void Plan_RestoresAForegroundThatIsOnTheCurrentStage()
    {
        var windows = Windows(W(1, "A", "a"), W(3, "B", "b"));
        var current = new MonitorState(
            "M1",
            Set("active", 3),
            new[] { Set("s1", 1) });
        var saved = new MonitorWorkspace(
            "M1",
            Array.Empty<WindowIdentity>(),
            new WindowIdentity("B", "b"),
            new[] { new WindowIdentity("A", "a") });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new WindowId(3), plan!.Foreground);
    }

    [Fact]
    public void Plan_PreservesTheSavedStageOrder()
    {
        var windows = Windows(W(1, "A", "a"), W(2, "B", "b"), W(3, "C", "c"));
        var current = new MonitorState(
            "M1",
            null,
            new[] { Set("s1", 1), Set("s2", 2), Set("s3", 3) });
        var saved = new MonitorWorkspace(
            "M1",
            Array.Empty<WindowIdentity>(),
            null,
            new[]
            {
                new WindowIdentity("C", "c"),
                new WindowIdentity("A", "a"),
                new WindowIdentity("B", "b")
            });

        var plan = WorkspaceRestore.Plan(current, saved, windows);

        Assert.NotNull(plan);
        Assert.Equal(new[] { new WindowId(3), new WindowId(1), new WindowId(2) }, plan!.Stage);
        Assert.Empty(plan.Strip);
    }
}
