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
