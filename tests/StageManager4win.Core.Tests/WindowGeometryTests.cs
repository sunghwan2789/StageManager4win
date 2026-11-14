using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class WindowGeometryTests
{
    private static MonitorDescriptor Monitor(
        string id, int left, int top, int width, int height) =>
        new(id, new WindowBounds(left, top, width, height), new WindowBounds(left, top, width, height), 96);

    [Fact]
    public void FromPixels_At96Dpi_KeepsPixelsAsDip()
    {
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(100, 50, 800, 600), "M1", 96, isMaximized: false);

        Assert.Equal(100, geometry.BoundsDip.X, 3);
        Assert.Equal(50, geometry.BoundsDip.Y, 3);
        Assert.Equal(800, geometry.BoundsDip.Width, 3);
        Assert.Equal(600, geometry.BoundsDip.Height, 3);
        Assert.Equal("M1", geometry.MonitorId);
        Assert.Equal(96u, geometry.Dpi);
        Assert.False(geometry.IsMaximized);
        Assert.Null(geometry.RestoreBoundsDip);
    }

    [Fact]
    public void FromPixels_At144Dpi_ScalesToDip()
    {
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(150, 75, 1200, 900), "M1", 144, isMaximized: false);

        Assert.Equal(100, geometry.BoundsDip.X, 3);
        Assert.Equal(50, geometry.BoundsDip.Y, 3);
        Assert.Equal(800, geometry.BoundsDip.Width, 3);
        Assert.Equal(600, geometry.BoundsDip.Height, 3);
    }

    [Fact]
    public void ToPixels_ScalesBackToTheTargetDpi()
    {
        var geometry = new WindowGeometry(
            new RectD(100, 50, 800, 600), "M1", 144, IsMaximized: false, RestoreBoundsDip: null);

        var pixels = geometry.ToPixels(144);

        Assert.Equal(150, pixels.Left);
        Assert.Equal(75, pixels.Top);
        Assert.Equal(1200, pixels.Width);
        Assert.Equal(900, pixels.Height);
    }

    [Fact]
    public void InvalidDpi_IsTreatedAs96()
    {
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(100, 50, 800, 600), "M1", 0, isMaximized: false);

        Assert.Equal(96u, geometry.Dpi);
        Assert.Equal(100, geometry.BoundsDip.X, 3);
    }

    [Fact]
    public void RestoreBoundsToPixels_UsesTheNormalRectangleForAMaximizedWindow()
    {
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(0, 0, 1920, 1040),
            "M1",
            96,
            isMaximized: true,
            restoreBounds: new WindowBounds(10, 20, 300, 200));

        var pixels = geometry.RestoreBoundsToPixels(96);

        Assert.Equal(10, pixels.Left);
        Assert.Equal(20, pixels.Top);
        Assert.Equal(300, pixels.Width);
        Assert.Equal(200, pixels.Height);
    }

    [Fact]
    public void RestoreBoundsToPixels_FallsBackToTheCurrentBounds()
    {
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(10, 20, 300, 200), "M1", 96, isMaximized: false);

        var pixels = geometry.RestoreBoundsToPixels(96);

        Assert.Equal(new WindowBounds(10, 20, 300, 200), pixels);
    }

    [Fact]
    public void ClampInto_MovesAWindowInsideTheWorkArea()
    {
        var geometry = new WindowGeometry(
            new RectD(-50, 900, 400, 300), "M1", 96, IsMaximized: false, RestoreBoundsDip: null);

        var clamped = geometry.ClampInto(new RectD(0, 0, 1920, 1040));

        Assert.Equal(0, clamped.BoundsDip.X, 3);
        Assert.Equal(740, clamped.BoundsDip.Y, 3);
        Assert.Equal(400, clamped.BoundsDip.Width, 3);
        Assert.Equal(300, clamped.BoundsDip.Height, 3);
    }

    [Fact]
    public void ClampInto_PinsAnOversizedWindowToTheTopLeft()
    {
        var geometry = new WindowGeometry(
            new RectD(100, 100, 3000, 2000), "M1", 96, IsMaximized: false, RestoreBoundsDip: null);

        var clamped = geometry.ClampInto(new RectD(0, 25, 1920, 1000));

        Assert.Equal(0, clamped.BoundsDip.X, 3);
        Assert.Equal(25, clamped.BoundsDip.Y, 3);
        Assert.Equal(3000, clamped.BoundsDip.Width, 3);
        Assert.Equal(2000, clamped.BoundsDip.Height, 3);
    }

    [Fact]
    public void SelectMonitor_ChoosesTheLargestIntersection()
    {
        var monitors = new[]
        {
            Monitor("left", 0, 0, 1000, 1000),
            Monitor("right", 1000, 0, 1000, 1000)
        };

        var selected = WindowGeometry.SelectMonitor(
            monitors, new RectD(900, 100, 400, 300));

        Assert.Equal("right", selected!.Id);
    }

    [Fact]
    public void SelectMonitor_ConvertsSavedDipToPixelsAtTheSavedDpi()
    {
        var monitors = new[]
        {
            new MonitorDescriptor("left", new WindowBounds(0, 0, 1000, 1000),
                new WindowBounds(0, 0, 1000, 1000), 96),
            new MonitorDescriptor("right", new WindowBounds(1000, 0, 1000, 1000),
                new WindowBounds(1000, 0, 1000, 1000), 192)
        };
        var geometry = WindowGeometry.FromPixels(
            new WindowBounds(1100, 100, 200, 400), "right", 192, isMaximized: false);

        var selected = WindowGeometry.SelectMonitor(
            monitors, geometry.BoundsDip, geometry.Dpi);

        Assert.Equal("right", selected!.Id);
    }

    [Fact]
    public void SelectMonitor_WithoutIntersection_ChoosesTheNearestCenter()
    {
        var monitors = new[]
        {
            Monitor("left", 0, 0, 1000, 1000),
            Monitor("right", 1000, 0, 1000, 1000)
        };

        var selected = WindowGeometry.SelectMonitor(
            monitors, new RectD(2500, 100, 100, 100));

        Assert.Equal("right", selected!.Id);
    }

    [Fact]
    public void SelectMonitor_WithoutMonitors_ReturnsNull()
    {
        Assert.Null(WindowGeometry.SelectMonitor(
            Array.Empty<MonitorDescriptor>(), new RectD(0, 0, 100, 100)));
    }
}
