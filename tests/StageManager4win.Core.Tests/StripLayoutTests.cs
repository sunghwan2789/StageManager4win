using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Tests;

public class StripLayoutTests
{
    private static readonly StripLayoutOptions Options = new(
        MonitorWidth: 1710, MonitorHeight: 1107, WorkAreaTop: 25, WorkAreaHeight: 1055);

    private static WindowKey K(long id, int processId = 1) => new(new WindowId(id), processId);

    private static StripItem Item(
        long id, double aspect, int appCount = 1, params long[] memberIds)
    {
        var members = (memberIds.Length == 0 ? new[] { id } : memberIds)
            .Select(memberId => K(memberId))
            .ToArray();
        return new StripItem($"set-{id}", members, appCount, aspect);
    }

    [Fact]
    public void WideItem_FitsTheWidth()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 16.0 / 9.0) }, Options);

        var union = layout.Tiles[0].UnionBoundsDip;
        Assert.Equal(160, union.Width, 3);
        Assert.Equal(90, union.Height, 3);
    }

    [Fact]
    public void TallItem_FitsTheHeight()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 3.0 / 4.0) }, Options);

        var union = layout.Tiles[0].UnionBoundsDip;
        Assert.Equal(90, union.Width, 3);
        Assert.Equal(120, union.Height, 3);
    }

    [Fact]
    public void InvalidAspect_UsesFourByThree()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, double.NaN) }, Options);

        Assert.Equal(160, layout.Tiles[0].UnionBoundsDip.Width, 3);
        Assert.Equal(120, layout.Tiles[0].UnionBoundsDip.Height, 3);
    }

    [Fact]
    public void Cards_ShareTheTrueMiddleY()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1, 1, 1, 2, 3) }, Options);

        var placement = layout.Tiles[0];
        var middle = placement.UnionBoundsDip.Y + placement.UnionBoundsDip.Height / 2;
        Assert.Equal(3, placement.Cards.Count);
        Assert.All(placement.Cards, card =>
            Assert.Equal(middle, card.BoundsDip.Y + card.BoundsDip.Height / 2, 6));
    }

    [Fact]
    public void LeftFan_FrontCardExtendsInward()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1, 1, 1, 2, 3) }, Options);

        var cards = layout.Tiles[0].Cards;
        Assert.True(cards[^1].BoundsDip.X > cards[0].BoundsDip.X);
        Assert.True(cards[^1].BoundsDip.X + cards[^1].BoundsDip.Width
            > cards[0].BoundsDip.X + cards[0].BoundsDip.Width);
    }

    [Fact]
    public void Cards_StackIndexIsFrontRelative()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1, 1, 1, 2, 3) }, Options);

        Assert.Equal(new[] { 2, 1, 0 }, layout.Tiles[0].Cards.Select(card => card.StackIndex));
    }

    [Fact]
    public void RightFan_FrontCardExtendsInwardAndBackCardMeetsTheUnionRight()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1, 1, 1, 2, 3) }, Options with { Edge = StripEdge.Right });

        var placement = layout.Tiles[0];
        var front = placement.Cards[^1].BoundsDip;
        var back = placement.Cards[0].BoundsDip;
        Assert.True(front.X < back.X);
        Assert.Equal(
            placement.UnionBoundsDip.X + placement.UnionBoundsDip.Width,
            back.X + back.Width,
            6);
    }

    [Fact]
    public void UnionWidth_IsCappedAtThreeHundred()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 16.0 / 9.0, 1, Enumerable.Range(1, 20).Select(i => (long)i).ToArray()) },
            Options);

        Assert.Equal(TileStack.MaxUnionWidth, layout.Tiles[0].UnionBoundsDip.Width, 6);
    }

    [Fact]
    public void OversizedUnion_StaysInsideTheLeftMonitor()
    {
        var options = Options with { MonitorWidth = 800 };
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 16.0 / 9.0, 1, Enumerable.Range(1, 20).Select(i => (long)i).ToArray()) },
            options);

        Assert.Equal(0, layout.Tiles[0].UnionBoundsDip.X, 6);
    }

    [Fact]
    public void OversizedUnion_StaysInsideTheRightMonitor()
    {
        var options = Options with { MonitorWidth = 800, Edge = StripEdge.Right };
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 16.0 / 9.0, 1, Enumerable.Range(1, 20).Select(i => (long)i).ToArray()) },
            options);

        var union = layout.Tiles[0].UnionBoundsDip;
        Assert.Equal(500, union.X, 6);
        Assert.Equal(800, union.X + union.Width, 6);
    }

    [Fact]
    public void BadgeCluster_UsesFourAppSlotsPlusN()
    {
        var five = StripLayoutCalculator.Calculate(
            new[] { Item(1, 3.0 / 4.0, 5, 1, 2, 3, 4, 5) }, Options).Tiles[0];
        var many = StripLayoutCalculator.Calculate(
            new[] { Item(1, 3.0 / 4.0, 6, 1, 2, 3, 4, 5) }, Options).Tiles[0];

        Assert.Equal(124, five.BadgeBoundsDip.Width, 6);
        Assert.Equal(five.BadgeBoundsDip.Width, many.BadgeBoundsDip.Width, 6);
    }

    [Fact]
    public void BadgeCluster_UsesGapsWhenItFits()
    {
        var placement = StripLayoutCalculator.Calculate(
            new[] { Item(1, 3.0 / 4.0, 3, 1, 2, 3) }, Options).Tiles[0];

        Assert.Equal(92, placement.BadgeBoundsDip.Width, 6);
    }

    [Fact]
    public void BadgeCluster_AnchorsInsideTheUnionBottom()
    {
        var placement = StripLayoutCalculator.Calculate(
            new[] { Item(1, 3.0 / 4.0, 5, 1, 2, 3, 4, 5) }, Options).Tiles[0];
        var union = placement.UnionBoundsDip;
        var badge = placement.BadgeBoundsDip;

        Assert.Equal(28, badge.Height, 6);
        Assert.Equal(union.X + 6, badge.X, 6);
        Assert.Equal(union.Y + union.Height - 6, badge.Y + badge.Height, 6);
    }

    [Fact]
    public void BadgeCluster_RightEdgeAnchorsInsideTheUnionBottomRight()
    {
        var placement = StripLayoutCalculator.Calculate(
            new[] { Item(1, 3.0 / 4.0, 5, 1, 2, 3, 4, 5) },
            Options with { Edge = StripEdge.Right }).Tiles[0];
        var union = placement.UnionBoundsDip;
        var badge = placement.BadgeBoundsDip;

        Assert.Equal(union.X + union.Width - 6, badge.X + badge.Width, 6);
        Assert.Equal(union.Y + union.Height - 6, badge.Y + badge.Height, 6);
    }

    [Fact]
    public void Tiles_AreCenteredVerticallyInTheWorkArea()
    {
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 1), Item(2, 1), Item(3, 1), Item(4, 1), Item(5, 1) }, Options);

        // Five 120-high unions plus four 30 gaps = 720. (1055 - 720) / 2 = 167.5.
        Assert.Equal(192.5, layout.Tiles[0].UnionBoundsDip.Y, 3);
    }

    [Fact]
    public void GapAndOrder_ArePreserved()
    {
        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1), Item(2, 1) }, Options);

        Assert.Equal("set-1", layout.Tiles[0].SetId);
        Assert.Equal("set-2", layout.Tiles[1].SetId);
        Assert.Equal(layout.Tiles[0].UnionBoundsDip.Y + 120 + 30, layout.Tiles[1].UnionBoundsDip.Y, 3);
    }

    [Fact]
    public void Overflow_ReportsScrollRange()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options);

        // Eight 120-high unions plus seven 30 gaps = 1170; the view is 1055.
        Assert.Equal(1170, layout.ContentHeight, 3);
        Assert.Equal(115, layout.MaxScroll, 3);
        Assert.Equal(8, layout.Tiles.Count);
        Assert.Equal(25, layout.Tiles[0].UnionBoundsDip.Y, 3);
    }

    [Fact]
    public void ScrollOffset_MovesContentUp()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 50);

        Assert.Equal(-25, layout.Tiles[0].UnionBoundsDip.Y, 3);
    }

    [Fact]
    public void ScrollOffset_IsClampedToTheEnd()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item(i, 1)).ToArray();

        var layout = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 9999);

        Assert.Equal(-90, layout.Tiles[0].UnionBoundsDip.Y, 3);
        var last = layout.Tiles[^1].UnionBoundsDip;
        Assert.Equal(1080, last.Y + last.Height, 3);
    }

    [Fact]
    public void FittingContent_IgnoresScrollOffset()
    {
        var items = new[] { Item(1, 1), Item(2, 1) };

        var centered = StripLayoutCalculator.Calculate(items, Options);
        var scrolled = StripLayoutCalculator.Calculate(items, Options, scrollOffset: 500);

        Assert.Equal(0, centered.MaxScroll, 3);
        Assert.Equal(centered.Tiles[0].UnionBoundsDip.Y, scrolled.Tiles[0].UnionBoundsDip.Y, 3);
    }

    [Fact]
    public void RightEdge_AnchorsToTheRight()
    {
        var options = Options with { Edge = StripEdge.Right };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 4.0 / 3.0) }, options);

        // Strip width 220, union width 160: x = 1710 - 220 + 30.
        Assert.Equal(1520, layout.Tiles[0].UnionBoundsDip.X, 3);
    }

    [Fact]
    public void StripWidth_IsClamped()
    {
        var narrow = Options with { MonitorWidth = 800 };

        var layout = StripLayoutCalculator.Calculate(new[] { Item(1, 1) }, narrow);

        // 800 * 0.12 = 96, clamped to 220; union 120 wide -> x = 50.
        Assert.Equal(50, layout.Tiles[0].UnionBoundsDip.X, 3);
    }

    [Fact]
    public void Calculate_LeftEdge_StripBoundsIsTheColumn()
    {
        var options = new StripLayoutOptions(1920, 1080, 0, 1040, StripEdge.Left);
        var layout = StripLayoutCalculator.Calculate(
            new[] { Item(1, 16.0 / 9.0) }, options);

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
            new[] { Item(1, 16.0 / 9.0) }, options);

        Assert.Equal(1689.6, layout.StripBounds.X, 3);
        Assert.Equal(230.4, layout.StripBounds.Width, 3);
    }

    [Fact]
    public void Calculate_NoItems_ReturnsTheStripColumn()
    {
        var layout = StripLayoutCalculator.Calculate(Array.Empty<StripItem>(), Options);

        Assert.Empty(layout.Tiles);
        Assert.Equal(0, layout.ContentHeight);
        Assert.Equal(0, layout.MaxScroll);
        Assert.Equal(Options.WorkAreaHeight, layout.StripBounds.Height);
    }

    [Fact]
    public void HitBounds_CoversTheUnion()
    {
        var placement = StripLayoutCalculator.Calculate(new[] { Item(1, 1) }, Options).Tiles[0];

        Assert.Equal(
            TileStack.HitBounds(placement.UnionBoundsDip),
            placement.HitBoundsDip);
    }
}
