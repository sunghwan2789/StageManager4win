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
    public void Overflow_ReportsScrollRange()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options);

        // Eight 120-high tiles plus seven 30 gaps = 1170; the view is 1055.
        Assert.Equal(1170, layout.ContentHeight, 3);
        Assert.Equal(115, layout.MaxScroll, 3);
        Assert.Equal(8, layout.Tiles.Count);
        Assert.Equal(25, layout.Tiles[0].Bounds.Y, 3);
    }

    [Fact]
    public void ScrollOffset_MovesContentUp()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 50);

        Assert.Equal(-25, layout.Tiles[0].Bounds.Y, 3);
    }

    [Fact]
    public void ScrollOffset_IsClampedToTheEnd()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 9999);

        Assert.Equal(-90, layout.Tiles[0].Bounds.Y, 3);
        var last = layout.Tiles[^1].Bounds;
        Assert.Equal(1080, last.Y + last.Height, 3);
    }

    [Fact]
    public void FittingContent_IgnoresScrollOffset()
    {
        var items = new[] { Item(1, 1), Item(2, 1) };

        var centered = StripLayoutCalculator.Calculate(items, Options);
        var scrolled = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 500);

        Assert.Equal(0, centered.MaxScroll, 3);
        Assert.Equal(centered.Tiles[0].Bounds.Y, scrolled.Tiles[0].Bounds.Y, 3);
        Assert.Equal(25 + (1055 - 270) / 2.0, scrolled.Tiles[0].Bounds.Y, 3);
    }

    [Fact]
    public void RightEdge_AnchorsToTheRight()
    {
        var options = Options with { Edge = StripEdge.Right };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 4.0 / 3.0) }, options);

        // Strip width 220, tile width 160: x = 1710 - 220 + 30.
        Assert.Equal(1520, layout.Tiles[0].Bounds.X, 3);
    }

    [Fact]
    public void StripWidth_IsClamped()
    {
        var narrow = Options with { MonitorWidth = 800 };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1) }, narrow);

        // 800 * 0.12 = 96, clamped to 220; tile 120 wide -> x = 50.
        Assert.Equal(50, layout.Tiles[0].Bounds.X, 3);
    }

    [Fact]
    public void Calculate_LeftEdge_StripBoundsIsTheColumn()
    {
        var options = new StripLayoutOptions(1920, 1080, 0, 1040, StripEdge.Left);
        var layout = StripLayoutCalculator.Calculate(
            new[] { new StripItem(new WindowId(1), 16.0 / 9.0) }, options);

        Assert.Equal(0, layout.StripBounds.X);
        Assert.Equal(0, layout.StripBounds.Y);
        Assert.Equal(230.4, layout.StripBounds.Width, 3);
        Assert.Equal(1040, layout.StripBounds.Height);
    }

    [Fact]
    public void Calculate_RightEdge_StripBoundsIsTheColumn()
    {
        var options = new StripLayoutOptions(1920, 1080, 0, 1040, StripEdge.Right);
        var layout = StripLayoutCalculator.Calculate(
            new[] { new StripItem(new WindowId(1), 16.0 / 9.0) }, options);

        Assert.Equal(1689.6, layout.StripBounds.X, 3);
        Assert.Equal(230.4, layout.StripBounds.Width, 3);
    }
}
