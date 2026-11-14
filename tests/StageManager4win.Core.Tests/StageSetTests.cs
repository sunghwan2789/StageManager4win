using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class StageSetTests
{
    private static WindowSnapshot Snapshot(long id, int processId) =>
        new(new WindowId(id), processId, "app", "App", $"window {id}", null,
            new WindowBounds(0, 0, 800, 600), "M1",
            IsVisible: true, IsMinimized: false, IsCloaked: false, IsFullscreen: false);

    [Fact]
    public void WindowKey_EqualityIncludesTheProcessId()
    {
        var first = new WindowKey(new WindowId(1), 10);
        var recycled = new WindowKey(new WindowId(1), 11);

        Assert.NotEqual(first, recycled);
        Assert.Equal(first, new WindowKey(new WindowId(1), 10));
    }

    [Fact]
    public void WindowKey_ForSnapshot_UsesTheCurrentProcess()
    {
        var key = WindowKey.For(Snapshot(7, 42));

        Assert.Equal(new WindowId(7), key.Window);
        Assert.Equal(42, key.ProcessId);
    }

    [Fact]
    public void StageSet_PreservesMemberOrderAndExposesTheLastMemberAsFront()
    {
        var back = new WindowKey(new WindowId(1), 10);
        var front = new WindowKey(new WindowId(2), 20);
        var set = new StageSet(
            "set-000001",
            new[] { back, front },
            front,
            new Dictionary<WindowKey, WindowGeometry>());

        Assert.Equal(new[] { back, front }, set.Members);
        Assert.Equal(front, set.Front);
    }

    [Fact]
    public void StageSet_ContainsUsesTheFullWindowKey()
    {
        var key = new WindowKey(new WindowId(1), 10);
        var set = new StageSet(
            "set-000001",
            new[] { key },
            key,
            new Dictionary<WindowKey, WindowGeometry>());

        Assert.True(set.Contains(key));
        Assert.False(set.Contains(new WindowKey(new WindowId(1), 11)));
    }

    [Fact]
    public void StageSet_GeometryIsLookedUpByWindowKey()
    {
        var key = new WindowKey(new WindowId(1), 10);
        var geometry = new WindowGeometry(
            new RectD(10, 20, 800, 600), "M1", 96, IsMaximized: false, RestoreBoundsDip: null);
        var set = new StageSet(
            "set-000001",
            new[] { key },
            key,
            new Dictionary<WindowKey, WindowGeometry> { [key] = geometry });

        Assert.Same(geometry, set.Geometry[key]);
    }
}
