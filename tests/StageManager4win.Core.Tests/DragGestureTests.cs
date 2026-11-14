using StageManager4win.Core.Layout;

namespace StageManager4win.Core.Tests;

public class DragGestureTests
{
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

    [Fact]
    public void ShouldDrop_OnlyOnTheSourceMonitorOutsideTheStrip()
    {
        var monitor = new RectD(0, 0, 1000, 800);
        var strip = new RectD(0, 0, 80, 800);

        Assert.True(DragGesture.ShouldDrop(monitor, strip, 500, 400));
        Assert.False(DragGesture.ShouldDrop(monitor, strip, 40, 400));
        Assert.False(DragGesture.ShouldDrop(monitor, strip, -1, 400));
        Assert.False(DragGesture.ShouldDrop(monitor, strip, 1000, 400));
    }
}
