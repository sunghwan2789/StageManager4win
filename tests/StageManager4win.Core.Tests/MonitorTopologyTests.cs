using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class MonitorTopologyTests
{
    private static readonly MonitorDescriptor Left = new(
        "M1", new WindowBounds(0, 0, 1000, 1000), new WindowBounds(0, 0, 1000, 960), 96);
    private static readonly MonitorDescriptor Right = new(
        "M2", new WindowBounds(1000, 0, 1000, 1000), new WindowBounds(1000, 0, 1000, 960), 96);

    [Fact]
    public void IdAt_FindsTheContainingMonitor()
    {
        Assert.Equal("M2", MonitorTopology.IdAt(new[] { Left, Right }, 1500, 500));
    }

    [Fact]
    public void IdAt_ReturnsNullOutsideEveryMonitor()
    {
        Assert.Null(MonitorTopology.IdAt(new[] { Left, Right }, 2500, 500));
    }

    [Fact]
    public void IdForBounds_UsesTheWindowCentre()
    {
        var bounds = new WindowBounds(-100, 0, 400, 400);

        Assert.Equal("M1", MonitorTopology.IdForBounds(new[] { Left, Right }, bounds));
    }

    [Fact]
    public void IdForBounds_FallsBackToTheNearestMonitor()
    {
        var bounds = new WindowBounds(1000, 2000, 200, 200);

        Assert.Equal("M2", MonitorTopology.IdForBounds(new[] { Left, Right }, bounds));
    }

    [Fact]
    public void IdForBounds_EmptyList_ReturnsNull()
    {
        Assert.Null(MonitorTopology.IdForBounds(Array.Empty<MonitorDescriptor>(), new WindowBounds(0, 0, 10, 10)));
    }

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
}
