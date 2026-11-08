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
