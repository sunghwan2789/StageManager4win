using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using Xunit;

namespace StageManager4win.Core.Tests;

public class StripLayoutTests
{
    private static readonly StripLayoutOptions Options = new(
        MonitorWidth: 1710, MonitorHeight: 1107, WorkAreaTop: 25, WorkAreaHeight: 1055);

    private static StripItem Item(long id, double aspect) => new(new WindowId(id), aspect);

    [Fact]
    public void WideItem_FitsTheWidth()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 16.0 / 9.0) }, Options);

        var tile = layout.Tiles[0].Bounds;
        Assert.Equal(160, tile.Width, 3);
        Assert.Equal(90, tile.Height, 3);
    }

    [Fact]
    public void TallItem_FitsTheHeight()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 3.0 / 4.0) }, Options);

        var tile = layout.Tiles[0].Bounds;
        Assert.Equal(90, tile.Width, 3);
        Assert.Equal(120, tile.Height, 3);
    }

    [Fact]
    public void InvalidAspect_UsesFourByThree()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, double.NaN) }, Options);

        Assert.Equal(160, layout.Tiles[0].Bounds.Width, 3);
        Assert.Equal(120, layout.Tiles[0].Bounds.Height, 3);
    }

    [Fact]
    public void Tiles_AreCenteredVerticallyInTheWorkArea()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1), Item(2, 1), Item(3, 1), Item(4, 1), Item(5, 1) }, Options);

        // Five 120-high tiles plus four 30 gaps = 720. (1055 - 720) / 2 = 167.5.
        Assert.Equal(192.5, layout.Tiles[0].Bounds.Y, 3);
    }

    [Fact]
    public void GapAndOrder_ArePreserved()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1), Item(2, 1) }, Options);

        Assert.Equal(new WindowId(1), layout.Tiles[0].Window);
        Assert.Equal(new WindowId(2), layout.Tiles[1].Window);
        Assert.Equal(layout.Tiles[0].Bounds.Y + 120 + 30, layout.Tiles[1].Bounds.Y, 3);
    }

    [Fact]
    public void Overflow_LimitsVisibleCount()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options);

        Assert.Equal(7, layout.VisibleCount);
        Assert.Equal(7, layout.Tiles.Count);
    }

    [Fact]
    public void FirstVisibleIndex_SkipsItems()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options, firstVisibleIndex: 2);

        Assert.Equal(2, layout.FirstVisibleIndex);
        Assert.Equal(new WindowId(3), layout.Tiles[0].Window);
    }

    [Fact]
    public void FirstVisibleIndex_IsClamped()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1) }, Options, firstVisibleIndex: 99);

        Assert.Empty(layout.Tiles);
        Assert.Equal(1, layout.FirstVisibleIndex);
    }

    [Fact]
    public void RightEdge_AnchorsToTheRight()
    {
        var options = Options with { Edge = StripEdge.Right };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 4.0 / 3.0) }, options);

        // Strip width 205.2, tile width 160: x = 1710 - 205.2 + 22.6.
        Assert.Equal(1527.4, layout.Tiles[0].Bounds.X, 3);
    }

    [Fact]
    public void StripWidth_IsClamped()
    {
        var narrow = Options with { MonitorWidth = 800 };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1) }, narrow);

        // 800 * 0.12 = 96, clamped to 160; tile 120 wide -> x = 20.
        Assert.Equal(20, layout.Tiles[0].Bounds.X, 3);
    }
}
