using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class TileHitTestTests
{
    private static readonly TilePlacement[] Tiles =
    {
        new(new WindowId(1), new RectD(10, 10, 100, 50)),
        new(new WindowId(2), new RectD(10, 100, 100, 50))
    };

    [Fact]
    public void PointInside_ReturnsTheWindow()
    {
        Assert.Equal(new WindowId(2), TileHitTest.Hit(Tiles, 50, 120));
    }

    [Fact]
    public void PointOnTheEdge_ReturnsTheWindow()
    {
        Assert.Equal(new WindowId(1), TileHitTest.Hit(Tiles, 10, 10));
    }

    [Fact]
    public void PointOutside_ReturnsNull()
    {
        Assert.Null(TileHitTest.Hit(Tiles, 5, 5));
    }

    [Fact]
    public void EmptyList_ReturnsNull()
    {
        Assert.Null(TileHitTest.Hit(Array.Empty<TilePlacement>(), 1, 1));
    }
}
